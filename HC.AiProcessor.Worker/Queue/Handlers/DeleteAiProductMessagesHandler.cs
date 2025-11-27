using HC.AiProcessor.Application.v1.Commands;
using HC.Packages.AiProcessor.V1.Models;

namespace HC.AiProcessor.Worker.Queue.Handlers;

internal sealed record DeleteAiProductsMessage : IAiProcessorQueueMessage
{
    public required AiProcessorDeleteProductRequest[] Requests { get; set; }
}

internal sealed class DeleteAiProductMessagesHandler(
    IMediator mediator,
    IAiProcessorTaskCompletionSource taskCompletionSource
) : IRequestHandler<DeleteAiProductsMessage>
{
    private readonly IMediator _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));

    private readonly IAiProcessorTaskCompletionSource _taskCompletionSource =
        taskCompletionSource ?? throw new ArgumentNullException(nameof(taskCompletionSource));

    public async Task Handle(DeleteAiProductsMessage message, CancellationToken ct)
    {
        DeleteAiProductCommandResult result =
            await _mediator.Send(new DeleteAiProductCommand(message.Requests), ct);

        _taskCompletionSource.SetResult(result.Response);
    }
}
