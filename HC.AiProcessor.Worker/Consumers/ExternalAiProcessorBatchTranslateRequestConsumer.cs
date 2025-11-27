using HC.AiProcessor.Worker.Queue;
using HC.AiProcessor.Worker.Queue.Handlers;
using HC.Packages.AiProcessor.V1.Models;
using HC.Packages.Contracts.V1.Commands;
using MassTransit;

namespace HC.AiProcessor.Worker.Consumers;

public class ExternalAiProcessorBatchTranslateRequestConsumer(IAiProcessorQueue queue)
    : IConsumer<ExternalActionRequestCommand<AiProcessorBatchRequest<AiProcessorTranslateRequest>>>
{
    private readonly IAiProcessorQueue _queue = queue ?? throw new ArgumentNullException(nameof(queue));

    public async Task Consume(
        ConsumeContext<ExternalActionRequestCommand<AiProcessorBatchRequest<AiProcessorTranslateRequest>>> context)
    {
        await _queue.EnqueueMessageAsync(
            message: new ExternalBatchTranslateMessage { Command = context.Message },
            context.CancellationToken);
    }
}
