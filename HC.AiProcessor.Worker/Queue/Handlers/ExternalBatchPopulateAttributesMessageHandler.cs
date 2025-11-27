using HC.AiProcessor.Application.v1.Commands;
using HC.Packages.AiProcessor.V1.Models;
using HC.Packages.Contracts.V1.Commands;
using MassTransit;

namespace HC.AiProcessor.Worker.Queue.Handlers;

internal sealed record ExternalBatchPopulateAttributesMessage : IAiProcessorQueueMessage
{
    public required ExternalActionRequestCommand<AiProcessorBatchRequest<AiProcessorPopulateAttributesRequest>> Command
    {
        get;
        set;
    }
}

internal class ExternalBatchPopulateAttributesRequestMessageHandler(
    IMediator mediator,
    IAiProcessorTaskCompletionSource taskCompletionSource,
    IPublishEndpoint publishEndpoint) : IRequestHandler<ExternalBatchPopulateAttributesMessage>
{
    private readonly IMediator _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));

    private readonly IAiProcessorTaskCompletionSource _taskCompletionSource =
        taskCompletionSource ?? throw new ArgumentNullException(nameof(taskCompletionSource));

    private readonly IPublishEndpoint _publishEndpoint =
        publishEndpoint ?? throw new ArgumentNullException(nameof(publishEndpoint));

    public async Task Handle(ExternalBatchPopulateAttributesMessage message, CancellationToken ct)
    {
        ExternalActionRequestCommand<AiProcessorBatchRequest<AiProcessorPopulateAttributesRequest>> command =
            message.Command;
        BatchPopulateAttributesCommandResult result =
            await _mediator.Send(new BatchPopulateAttributesCommand(command.Data!), ct);

        _taskCompletionSource.SetResult(result.Response);

        await _publishEndpoint.Publish(
            new ExternalActionResponseCommand<AiProcessorBatchResponse<AiProcessorPopulateAttributesResponse>>
            {
                RequestType = command.Type,
                RequestId = command.Id,
                Data = result.Response
            }, ct);
    }
}
