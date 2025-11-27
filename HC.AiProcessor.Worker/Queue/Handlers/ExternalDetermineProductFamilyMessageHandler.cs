using HC.AiProcessor.Application.v1.Commands;
using HC.Packages.AiProcessor.V1.Models;
using HC.Packages.Contracts.V1.Commands;
using MassTransit;

namespace HC.AiProcessor.Worker.Queue.Handlers;

internal sealed record ExternalDetermineProductFamilyMessage : IAiProcessorQueueMessage
{
    public required ExternalActionRequestCommand<AiProcessorDetermineProductFamilyRequest> Command { get; set; }
}

internal sealed class ExternalDetermineProductFamilyMessageHandler(
    IMediator mediator,
    IAiProcessorTaskCompletionSource taskCompletionSource,
    IPublishEndpoint publishEndpoint) : IRequestHandler<ExternalDetermineProductFamilyMessage>
{
    private readonly IMediator _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));

    private readonly IAiProcessorTaskCompletionSource _taskCompletionSource =
        taskCompletionSource ?? throw new ArgumentNullException(nameof(taskCompletionSource));

    private readonly IPublishEndpoint _publishEndpoint =
        publishEndpoint ?? throw new ArgumentNullException(nameof(publishEndpoint));

    public async Task Handle(ExternalDetermineProductFamilyMessage message, CancellationToken ct)
    {
        ExternalActionRequestCommand<AiProcessorDetermineProductFamilyRequest> command = message.Command;
        DetermineProductFamilyCommandResult result =
            await _mediator.Send(new DetermineProductFamilyCommand(command.Data!), ct);

        _taskCompletionSource.SetResult(result.Response);

        await _publishEndpoint.Publish(
            new ExternalActionResponseCommand<AiProcessorDetermineProductFamilyResponse>
            {
                RequestType = command.Type,
                RequestId = command.Id,
                Data = result.Response
            }, ct);
    }
}
