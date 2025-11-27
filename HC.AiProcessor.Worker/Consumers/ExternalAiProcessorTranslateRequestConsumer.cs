using HC.AiProcessor.Worker.Queue;
using HC.AiProcessor.Worker.Queue.Handlers;
using HC.Packages.AiProcessor.V1.Models;
using HC.Packages.Contracts.V1.Commands;
using MassTransit;

namespace HC.AiProcessor.Worker.Consumers;

internal sealed class ExternalAiProcessorTranslateRequestConsumer(IAiProcessorQueue queue)
    : IConsumer<ExternalActionRequestCommand<AiProcessorTranslateRequest>>
{
    private readonly IAiProcessorQueue _queue = queue ?? throw new ArgumentNullException(nameof(queue));

    public async Task Consume(ConsumeContext<ExternalActionRequestCommand<AiProcessorTranslateRequest>> context)
    {
        await _queue.EnqueueMessageAsync(
            message: new ExternalTranslateMessage { Command = context.Message },
            context.CancellationToken);
    }
}
