using HC.AiProcessor.Worker.Queue;
using HC.AiProcessor.Worker.Queue.Handlers;
using HC.Packages.AiProcessor.V1.Models;
using HC.Packages.Contracts.V1.Commands;
using MassTransit;

namespace HC.AiProcessor.Worker.Consumers;

internal sealed class ExternalAiProcessorBatchPopulateAttributesRequestConsumer(IAiProcessorQueue queue)
    : IConsumer<ExternalActionRequestCommand<AiProcessorBatchRequest<AiProcessorPopulateAttributesRequest>>>
{
    private readonly IAiProcessorQueue _queue = queue ?? throw new ArgumentNullException(nameof(queue));

    public async Task Consume(
        ConsumeContext<ExternalActionRequestCommand<AiProcessorBatchRequest<AiProcessorPopulateAttributesRequest>>>
            context)
    {
        await _queue.EnqueueMessageAsync(
            message: new ExternalBatchPopulateAttributesMessage { Command = context.Message },
            context.CancellationToken);
    }
}
