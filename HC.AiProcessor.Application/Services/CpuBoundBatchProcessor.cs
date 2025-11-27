using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Tasks.Dataflow;
using HC.AiProcessor.Application.Constants;
using HC.Packages.AiProcessor.V1.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HC.AiProcessor.Application.Services;

public interface ICpuBoundBatchProcessor
{
    Task<AiProcessorBatchResponse<TOutput>> ProcessAsync<TInput, TOutput>(
        IEnumerable<AiProcessorBatchInput<TInput>> inputs,
        Func<AiProcessorBatchInput<TInput>, Task<AiProcessorBatchOutput<TOutput>>> process,
        CpuBoundBatchProcessingOptions? processingOptions = null,
        CancellationToken cancellationToken = default);
}

public record CpuBoundBatchProcessingOptions
{
    public int MaxDegreeOfParallelism { get; set; } = Math.Clamp(Environment.ProcessorCount * 8, 16, 128);
}

public record DataflowBatchProcessorOptions
{
    public int MaxSendRetries { get; set; } = 3;
    public int RetryDelayMs { get; set; } = 100;
}

internal sealed class DataflowBatchProcessor : ICpuBoundBatchProcessor
{
    private const string Tag = "DATAFLOW_BATCH_PROCESSOR";

    private readonly DataflowBatchProcessorOptions _options;
    private readonly ILogger<DataflowBatchProcessor> _logger;

    public DataflowBatchProcessor(
        IOptions<DataflowBatchProcessorOptions> optionsAccessor,
        ILogger<DataflowBatchProcessor> logger)
    {
        ArgumentNullException.ThrowIfNull(optionsAccessor);
        ArgumentNullException.ThrowIfNull(logger);

        _options = optionsAccessor.Value;
        _logger = logger;
    }

    public async Task<AiProcessorBatchResponse<TOutput>> ProcessAsync<TInput, TOutput>(
        IEnumerable<AiProcessorBatchInput<TInput>> inputs,
        Func<AiProcessorBatchInput<TInput>, Task<AiProcessorBatchOutput<TOutput>>> process,
        CpuBoundBatchProcessingOptions? processingOptions = null,
        CancellationToken cancellationToken = default)
    {
        processingOptions ??= new CpuBoundBatchProcessingOptions();

        var stopwatch = Stopwatch.StartNew();

        _logger.LogDebug("[{Tag}] Batch processing started.", Tag);

        try
        {
            IReadOnlyCollection<AiProcessorBatchOutput<TOutput>> outputs =
                await InternalProcessAsync(inputs, process, processingOptions, cancellationToken);

            _logger.LogDebug("[{Tag}] Batch processing completed.", Tag);

            if (!_logger.IsEnabled(LogLevel.Debug))
            {
                return new AiProcessorBatchResponse<TOutput> { Outputs = outputs };
            }

            var completedCount = 0;
            var failedCount = 0;

            foreach (AiProcessorBatchOutput<TOutput> output in outputs)
            {
                if (output.Error is null)
                {
                    completedCount++;
                    continue;
                }

                failedCount++;
            }

            _logger.LogDebug(
                "[{Tag}] Batch processing finished, completed: {CompletedCount}, failed: {FailedCount}, processing time: {ProcessingTimeMs}ms.",
                Tag,
                completedCount,
                failedCount,
                stopwatch.ElapsedMilliseconds);

            return new AiProcessorBatchResponse<TOutput> { Outputs = outputs };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{Tag}] Batch processing failed.", Tag);

            _logger.LogDebug(
                "[{Tag}] Batch processing finished, processing time: {ProcessingTimeMs}ms.",
                Tag,
                stopwatch.ElapsedMilliseconds);

            return new AiProcessorBatchResponse<TOutput>
            {
                Error = new AiProcessorBatchError
                {
                    Code = ErrorCodes.BatchFailedError,
                    Message = ex.Message
                }
            };
        }
    }

    private async Task<ConcurrentBag<AiProcessorBatchOutput<TOutput>>> InternalProcessAsync<TInput, TOutput>(
        IEnumerable<AiProcessorBatchInput<TInput>> inputs,
        Func<AiProcessorBatchInput<TInput>, Task<AiProcessorBatchOutput<TOutput>>> process,
        CpuBoundBatchProcessingOptions processingOptions,
        CancellationToken cancellationToken)
    {
        var results = new ConcurrentBag<AiProcessorBatchOutput<TOutput>>();
        var options = new ExecutionDataflowBlockOptions
        {
            MaxDegreeOfParallelism = processingOptions.MaxDegreeOfParallelism,
            BoundedCapacity = processingOptions.MaxDegreeOfParallelism * 2,
            CancellationToken = cancellationToken
        };

        var transformBlock =
            new TransformBlock<AiProcessorBatchInput<TInput>, AiProcessorBatchOutput<TOutput>>(
                async input =>
                {
                    var itemStopwatch = Stopwatch.StartNew();

                    _logger.LogDebug("[{Tag}] Input processing started, id: {Id}.", Tag, input.Id);

                    try
                    {
                        AiProcessorBatchOutput<TOutput> output = await process(input);

                        _logger.LogDebug("[{Tag}] Input processing completed, id: {Id}.", Tag, input.Id);

                        return output;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[{Tag}] Input processing failed, id: {CustomId}.", Tag, input.Id);

                        return new AiProcessorBatchOutput<TOutput>
                        {
                            Id = input.Id,
                            Error = new AiProcessorBatchError
                            {
                                Code = ErrorCodes.InputProcessingError,
                                Message = ex.Message
                            }
                        };
                    }
                    finally
                    {
                        _logger.LogDebug(
                            "[{Tag}] Input processing finished, id: {Id}, processing time: {ProcessingTimeMs}ms.",
                            Tag,
                            input.Id,
                            itemStopwatch.ElapsedMilliseconds);
                    }
                },
                options);

        var actionBlock = new ActionBlock<AiProcessorBatchOutput<TOutput>>(
            result => results.Add(result),
            options);

        transformBlock.LinkTo(actionBlock, new DataflowLinkOptions { PropagateCompletion = true });

        foreach (AiProcessorBatchInput<TInput> input in inputs)
        {
            var sent = false;
            for (var attempt = 0; attempt < _options.MaxSendRetries && !sent; attempt++)
            {
                sent = await transformBlock.SendAsync(input, cancellationToken);
                if (!sent)
                {
                    await Task.Delay(_options.RetryDelayMs, cancellationToken);
                }
            }

            if (sent)
            {
                continue;
            }

            _logger.LogError(
                "[{Tag}] Failed to send request {CustomId} to the block after retries.",
                Tag, input.Id);

            results.Add(new AiProcessorBatchOutput<TOutput>
            {
                Id = input.Id,
                Error = new AiProcessorBatchError
                {
                    Code = ErrorCodes.InputProcessingError,
                    Message = $"Failed to send request {input.Id} after {_options.MaxSendRetries} retries."
                }
            });
        }

        transformBlock.Complete();

        await actionBlock.Completion;

        return results;
    }
}
