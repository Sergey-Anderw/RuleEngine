using HC.AiProcessor.Worker.Queue;
using HC.AiProcessor.Worker.Queue.Extensions;

namespace HC.AiProcessor.Worker.Workers;

internal sealed class AiProcessorQueueWorker(
    IAiProcessorQueue queue,
    ILogger<AiProcessorQueueWorker> logger
) : BackgroundService
{
    private readonly IAiProcessorQueue _queue = queue ?? throw new ArgumentNullException(nameof(queue));

    private readonly ILogger<AiProcessorQueueWorker>
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation($"{nameof(AiProcessorQueueWorker)} Starting");

        try
        {
            await InternalExecuteAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error during {nameof(AiProcessorQueueWorker)} executing");
        }
    }

    private async Task InternalExecuteAsync(CancellationToken ct)
    {
        await _queue.InitializeAsync(ct);

        while (!ct.IsCancellationRequested)
        {
            await _queue.WaitForMessageAsync(ct);
            await _queue.ExecuteNextMessageAsync(ct);
        }
    }
}
