using HC.AiProcessor.Application.v1.Commands;
using HC.Packages.AiProcessor.V1.Models;
using HC.Packages.Contracts.V1.Commands;
using MassTransit;

namespace HC.AiProcessor.Worker.Queue.Handlers;

internal sealed record ExternalPopulateAttributesMessage : IAiProcessorQueueMessage
{
    public required ExternalActionRequestCommand<AiProcessorPopulateAttributesRequest> Command { get; set; }
}

internal class ExternalPopulateAttributesRequestMessageHandler(
    IMediator mediator,
    IAiProcessorTaskCompletionSource taskCompletionSource,
    IPublishEndpoint publishEndpoint) : IRequestHandler<ExternalPopulateAttributesMessage>
{
    private readonly IMediator _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));

    private readonly IAiProcessorTaskCompletionSource _taskCompletionSource =
        taskCompletionSource ?? throw new ArgumentNullException(nameof(taskCompletionSource));

    private readonly IPublishEndpoint _publishEndpoint =
        publishEndpoint ?? throw new ArgumentNullException(nameof(publishEndpoint));

    public async Task Handle(ExternalPopulateAttributesMessage message, CancellationToken ct)
    {
        ExternalActionRequestCommand<AiProcessorPopulateAttributesRequest> command = message.Command;
        PopulateAttributesCommandResult result = await _mediator.Send(new PopulateAttributesCommand(command.Data!), ct);

        _taskCompletionSource.SetResult(result.Response);

        await _publishEndpoint.Publish(
            new ExternalActionResponseCommand<AiProcessorPopulateAttributesResponse>
            {
                RequestType = command.Type,
                RequestId = command.Id,
                Data = result.Response
            }, ct);
    }
}
