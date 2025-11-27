using HC.AiProcessor.Application.v1.Commands;
using HC.Packages.AiProcessor.V1.Models;

namespace HC.AiProcessor.Worker.Queue.Handlers;

internal sealed record RefreshAiProductsMessage : IAiProcessorQueueMessage
{
    public required AiProcessorRefreshProductRequest[] Requests { get; set; }
}

internal sealed class RefreshAiProductMessagesHandler(
    IMediator mediator,
    IAiProcessorTaskCompletionSource taskCompletionSource) : IRequestHandler<RefreshAiProductsMessage>
{
    private readonly IMediator _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));

    private readonly IAiProcessorTaskCompletionSource _taskCompletionSource =
        taskCompletionSource ?? throw new ArgumentNullException(nameof(taskCompletionSource));

    public async Task Handle(RefreshAiProductsMessage message, CancellationToken ct)
    {
        RefreshAiProductCommandResult result =
            await _mediator.Send(new RefreshAiProductCommand(message.Requests), ct);

        _taskCompletionSource.SetResult(result.Response);
    }
}
