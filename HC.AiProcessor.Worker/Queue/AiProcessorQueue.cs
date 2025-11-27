using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using HC.AiProcessor.Entity.Ai;
using HC.AiProcessor.Entity.Ai.Enums;
using HC.AiProcessor.Infrastructure.Repositories.Ai;
using HC.Packages.Common.Extensions;
using HC.Packages.Persistent.Infrastructure;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace HC.AiProcessor.Worker.Queue;

public interface IAiProcessorQueue
{
    bool IsEmpty { get; }
    event Action MessagesEnqueued;

    Task InitializeAsync(CancellationToken ct);
    Task ExecuteNextMessageAsync(CancellationToken ct);
    Task EnqueueMessageAsync(IAiProcessorQueueMessage message, CancellationToken ct);
    Task EnqueueMessagesAsync(IReadOnlyCollection<IAiProcessorQueueMessage> messages, CancellationToken ct);
}

internal sealed record AiProcessorQueueMessageData(IAiProcessorQueueMessage Data, string Version = "1.0");

internal sealed record AiProcessorQueueMessageResultData(object? Data, string Version = "1.0");

internal sealed class AiProcessorQueue(
    IServiceProvider serviceProvider,
    ISystemClock clock,
    ILogger<AiProcessorQueue> logger,
    IOptions<AiProcessorQueueConfig> configAccessor)
    : IAiProcessorQueue
{
    private readonly IServiceProvider _serviceProvider =
        serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

    private readonly ISystemClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    private readonly ILogger<AiProcessorQueue> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly AiProcessorQueueConfig _config = configAccessor.Value;

    private readonly ConcurrentQueue<long> _aiProcessorTasksQueue = new();
    private readonly SemaphoreSlim _executionSemaphore = new(1, 1);
    private readonly SemaphoreSlim _storageSemaphore = new(1, 1);

    public bool IsEmpty => _aiProcessorTasksQueue.IsEmpty;
    public event Action? MessagesEnqueued;

    public async Task InitializeAsync(CancellationToken ct)
    {
        await DoScopedDbWork(async (repository, _) =>
        {
            IReadOnlyCollection<AiProcessorTask> incompleteTasks = await repository.GetActive(ct);
            foreach (AiProcessorTask task in incompleteTasks
                         .OrderByDescending(x => x.Status)
                         .ThenBy(x => x.UpdatedAt))
            {
                _aiProcessorTasksQueue.Enqueue(task.Id);
            }
        });
    }

    public async Task ExecuteNextMessageAsync(CancellationToken ct)
    {
        if (!_aiProcessorTasksQueue.TryDequeue(out long aiProcessorTaskId))
            return;

        await _executionSemaphore.WaitAsync(ct);

        try
        {
            IAiProcessorQueueMessage message = await DoScopedDbWork(async (repository, uow) =>
            {
                AiProcessorTask aiProcessorTask = await repository.Get(aiProcessorTaskId, ct);

                var messageData = aiProcessorTask.Data.Deserialize<AiProcessorQueueMessageData>()!;
                IAiProcessorQueueMessage message = messageData.Data;

                aiProcessorTask.Status = AiProcessorTaskStatusType.InProgress;
                aiProcessorTask.ExecutionStart = _clock.UtcNow;

                await uow.SaveChangesAsync(ct);

                return message;
            });

            AsyncRetryPolicy retryPolicy = Policy
                .Handle<Exception>()
                .WaitAndRetryAsync(
                    _config.ExecutionRetryCount,
                    _ => TimeSpan.FromSeconds(_config.ExecutionRetryDurationInSeconds),
                    (exception, span) =>
                    {
                        _logger.LogWarning(
                            exception,
                            $"Failed to execute message {message.GetType().Name}. Retrying in {span.TotalSeconds} seconds");
                    });

            bool isDbUpdated = await DoScopedWork(async scope =>
            {
                var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

                await retryPolicy.ExecuteAsync(() => mediator.Send(message, ct));

                var resultProvider = scope.ServiceProvider.GetRequiredService<IAiProcessorTaskResultProvider>();
                if (resultProvider.Result is null)
                    return false;

                var repository = scope.ServiceProvider.GetRequiredService<IAiProcessorTaskRepository>();
                using var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

                AiProcessorTask aiProcessorTask = await repository.Get(aiProcessorTaskId, ct);
                aiProcessorTask.Result =
                    new AiProcessorQueueMessageResultData(resultProvider.Result).ToJsonNode()!.AsObject();
                aiProcessorTask.Status = AiProcessorTaskStatusType.Success;
                aiProcessorTask.ExecutionEnd = _clock.UtcNow;

                await uow.SaveChangesAsync(ct);

                return true;
            });

            if (isDbUpdated)
                return;

            await DoScopedDbWork(async (repository, uow) =>
            {
                AiProcessorTask aiProcessorTask = await repository.Get(aiProcessorTaskId, ct);
                aiProcessorTask.Status = AiProcessorTaskStatusType.Success;
                aiProcessorTask.ExecutionEnd = _clock.UtcNow;

                await uow.SaveChangesAsync(ct);
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"An error occured while handling ai processor task {aiProcessorTaskId}.");

            await DoScopedDbWork(async (repository, uow) =>
            {
                AiProcessorTask aiProcessorTask = await repository.Get(aiProcessorTaskId, ct);

                aiProcessorTask.Log = GetExceptionLog(ex);
                aiProcessorTask.Status = AiProcessorTaskStatusType.Failed;
                aiProcessorTask.ExecutionEnd = _clock.UtcNow;

                await uow.SaveChangesAsync(ct);
            });
        }
        finally
        {
            GC.Collect();
            _executionSemaphore.Release();
        }
    }

    public async Task EnqueueMessageAsync(IAiProcessorQueueMessage message, CancellationToken ct)
    {
        await EnqueueMessagesAsync([message], ct);
    }

    public async Task EnqueueMessagesAsync(IReadOnlyCollection<IAiProcessorQueueMessage> messages, CancellationToken ct)
    {
        if (messages.Count == 0)
            return;

        await _storageSemaphore.WaitAsync(ct);

        try
        {
            List<long> ids = await DoScopedDbWork(async (repository, uow) =>
            {
                var tasks = new List<AiProcessorTask>();

                foreach (IAiProcessorQueueMessage message in messages)
                {
                    AiProcessorTask aiProcessorTask = await repository.CreateEmpty(ct);
                    aiProcessorTask.Data = new AiProcessorQueueMessageData(message).ToJsonNode()!.AsObject();

                    tasks.Add(aiProcessorTask);
                }

                await uow.BulkSaveChangesAsync(ct);

                return tasks
                    .Select(x => x.Id)
                    .ToList();
            });

            foreach (long id in ids)
            {
                _aiProcessorTasksQueue.Enqueue(id);
            }

            MessagesEnqueued?.Invoke();
        }
        finally
        {
            _storageSemaphore.Release();
        }
    }

    private async Task DoScopedDbWork(Func<IAiProcessorTaskRepository, IUnitOfWork, Task> action)
    {
        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAiProcessorTaskRepository>();
        using var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await action(repository, uow);
    }

    private async Task<T> DoScopedDbWork<T>(Func<IAiProcessorTaskRepository, IUnitOfWork, Task<T>> action)
    {
        return await DoScopedWork<T>(async scope =>
        {
            var repository = scope.ServiceProvider.GetRequiredService<IAiProcessorTaskRepository>();
            using var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            T result = await action(repository, uow);
            return result;
        });
    }

    private async Task<T> DoScopedWork<T>(Func<IServiceScope, Task<T>> action)
    {
        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();

        T result = await action(scope);
        return result;
    }

    private static string GetExceptionLog(Exception ex)
    {
        var log = new StringBuilder();
        log.AppendLine($"Error. Message: {ex.Message}");
        log.AppendLine($"Stack trace: {ex.StackTrace}");

        var inner = ex.InnerException;

        while (inner is not null)
        {
            log.AppendLine($"Inner: {inner.Message}: {inner.StackTrace}");
            inner = inner.InnerException;
        }

        return log.ToString();
    }
}

internal sealed record AiProcessorQueueConfig
{
    public int ExecutionRetryDurationInSeconds { get; set; } = 30;
    public int ExecutionRetryCount { get; set; } = 3;
}
