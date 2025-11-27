using HC.AiProcessor.Application.v1.Commands;
using HC.Packages.AiProcessor.V1.Models;

namespace HC.AiProcessor.Worker.Queue.Handlers;

internal sealed record DeleteAiProductAttributeEmbeddingsMessage : IAiProcessorQueueMessage
{
    public required AiProcessorDeleteProductAttributeEmbeddingRequest[] Requests { get; set; }
}

internal sealed class DeleteAiProductAttributeEmbeddingsMessageHandler(
    IMediator mediator,
    IAiProcessorTaskCompletionSource taskCompletionSource
) : IRequestHandler<DeleteAiProductAttributeEmbeddingsMessage>
{
    private readonly IMediator _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));

    private readonly IAiProcessorTaskCompletionSource _taskCompletionSource =
        taskCompletionSource ?? throw new ArgumentNullException(nameof(taskCompletionSource));

    public async Task Handle(DeleteAiProductAttributeEmbeddingsMessage message, CancellationToken ct)
    {
        DeleteAiProductAttributeEmbeddingsCommandResult result =
            await _mediator.Send(new DeleteAiProductAttributeEmbeddingsCommand(message.Requests), ct);

        _taskCompletionSource.SetResult(result.Response);
    }
}
