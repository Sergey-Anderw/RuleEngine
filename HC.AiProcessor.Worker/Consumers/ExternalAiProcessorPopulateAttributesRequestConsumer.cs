using HC.AiProcessor.Worker.Queue;
using HC.AiProcessor.Worker.Queue.Handlers;
using HC.Packages.AiProcessor.V1.Models;
using HC.Packages.Contracts.V1.Commands;
using MassTransit;

namespace HC.AiProcessor.Worker.Consumers;

internal sealed class ExternalAiProcessorPopulateAttributesRequestConsumer(IAiProcessorQueue queue)
    : IConsumer<ExternalActionRequestCommand<AiProcessorPopulateAttributesRequest>>
{
    private readonly IAiProcessorQueue _queue = queue ?? throw new ArgumentNullException(nameof(queue));

    public async Task Consume(
        ConsumeContext<ExternalActionRequestCommand<AiProcessorPopulateAttributesRequest>> context)
    {
        await _queue.EnqueueMessageAsync(
            message: new ExternalPopulateAttributesMessage { Command = context.Message },
            context.CancellationToken);
    }
}
