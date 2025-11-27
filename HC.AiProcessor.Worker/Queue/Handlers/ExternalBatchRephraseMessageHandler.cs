using HC.AiProcessor.Application.v1.Commands;
using HC.Packages.AiProcessor.V1.Models;
using HC.Packages.Contracts.V1.Commands;
using MassTransit;

namespace HC.AiProcessor.Worker.Queue.Handlers;

internal sealed record ExternalBatchRephraseMessage : IAiProcessorQueueMessage
{
    public required ExternalActionRequestCommand<AiProcessorBatchRequest<AiProcessorRephraseRequest>> Command
    {
        get;
        set;
    }
}

internal class ExternalBatchRephraseRequestMessageHandler(
    IMediator mediator,
    IAiProcessorTaskCompletionSource taskCompletionSource,
    IPublishEndpoint publishEndpoint) : IRequestHandler<ExternalBatchRephraseMessage>
{
    private readonly IMediator _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));

    private readonly IAiProcessorTaskCompletionSource _taskCompletionSource =
        taskCompletionSource ?? throw new ArgumentNullException(nameof(taskCompletionSource));

    private readonly IPublishEndpoint _publishEndpoint =
        publishEndpoint ?? throw new ArgumentNullException(nameof(publishEndpoint));

    public async Task Handle(ExternalBatchRephraseMessage message, CancellationToken ct)
    {
        ExternalActionRequestCommand<AiProcessorBatchRequest<AiProcessorRephraseRequest>> command = message.Command;
        BatchRephraseCommandResult result =
            await _mediator.Send(new BatchRephraseCommand(command.Data!), ct);

        _taskCompletionSource.SetResult(result.Response);

        await _publishEndpoint.Publish(
            new ExternalActionResponseCommand<AiProcessorBatchResponse<AiProcessorRephraseResponse>>
            {
                RequestType = command.Type,
                RequestId = command.Id,
                Data = result.Response
            }, ct);
    }
}
