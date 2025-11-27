using HC.AiProcessor.Application.v1.Commands;
using HC.Packages.AiProcessor.V1.Models;
using HC.Packages.Contracts.V1.Commands;
using MassTransit;

namespace HC.AiProcessor.Worker.Queue.Handlers;

internal sealed record ExternalRephraseMessage : IAiProcessorQueueMessage
{
    public required ExternalActionRequestCommand<AiProcessorRephraseRequest> Command { get; set; }
}

internal class ExternalRephraseRequestMessageHandler(
    IMediator mediator,
    IAiProcessorTaskCompletionSource taskCompletionSource,
    IPublishEndpoint publishEndpoint) : IRequestHandler<ExternalRephraseMessage>
{
    private readonly IMediator _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));

    private readonly IAiProcessorTaskCompletionSource _taskCompletionSource =
        taskCompletionSource ?? throw new ArgumentNullException(nameof(taskCompletionSource));

    private readonly IPublishEndpoint _publishEndpoint =
        publishEndpoint ?? throw new ArgumentNullException(nameof(publishEndpoint));

    public async Task Handle(ExternalRephraseMessage message, CancellationToken ct)
    {
        ExternalActionRequestCommand<AiProcessorRephraseRequest> command = message.Command;
        RephraseCommandResult result = await _mediator.Send(new RephraseCommand(command.Data!), ct);

        _taskCompletionSource.SetResult(result.Response);

        await _publishEndpoint.Publish(
            new ExternalActionResponseCommand<AiProcessorRephraseResponse>
            {
                RequestType = command.Type,
                RequestId = command.Id,
                Data = result.Response
            }, ct);
    }
}
