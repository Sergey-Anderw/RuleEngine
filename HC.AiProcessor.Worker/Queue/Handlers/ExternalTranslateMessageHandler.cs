using HC.AiProcessor.Application.v1.Commands;
using HC.Packages.AiProcessor.V1.Models;
using HC.Packages.Contracts.V1.Commands;
using MassTransit;

namespace HC.AiProcessor.Worker.Queue.Handlers;

internal sealed record ExternalTranslateMessage : IAiProcessorQueueMessage
{
    public required ExternalActionRequestCommand<AiProcessorTranslateRequest> Command { get; set; }
}

internal class ExternalTranslateRequestMessageHandler(
    IMediator mediator,
    IAiProcessorTaskCompletionSource taskCompletionSource,
    IPublishEndpoint publishEndpoint) : IRequestHandler<ExternalTranslateMessage>
{
    private readonly IMediator _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));

    private readonly IAiProcessorTaskCompletionSource _taskCompletionSource =
        taskCompletionSource ?? throw new ArgumentNullException(nameof(taskCompletionSource));

    private readonly IPublishEndpoint _publishEndpoint =
        publishEndpoint ?? throw new ArgumentNullException(nameof(publishEndpoint));

    public async Task Handle(ExternalTranslateMessage message, CancellationToken ct)
    {
        ExternalActionRequestCommand<AiProcessorTranslateRequest> command = message.Command;
        TranslateCommandResult result = await _mediator.Send(new TranslateCommand(command.Data!), ct);

        _taskCompletionSource.SetResult(result.Response);

        await _publishEndpoint.Publish(
            new ExternalActionResponseCommand<AiProcessorTranslateResponse>
            {
                RequestType = command.Type,
                RequestId = command.Id,
                Data = result.Response
            }, ct);
    }
}
