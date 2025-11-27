using HC.AiProcessor.Application.v1.Commands;
using HC.Packages.AiProcessor.V1.Models;

namespace HC.AiProcessor.Worker.Queue.Handlers;

internal sealed record GenerateAiProductAttributeEmbeddingsMessage : IAiProcessorQueueMessage
{
    public required AiProcessorGenerateProductAttributeEmbeddingRequest[] Requests { get; set; }
}

internal sealed class GenerateAiProductAttributeEmbeddingsMessageHandler(
    IMediator mediator,
    IAiProcessorTaskCompletionSource taskCompletionSource
) : IRequestHandler<GenerateAiProductAttributeEmbeddingsMessage>
{
    private readonly IMediator _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));

    private readonly IAiProcessorTaskCompletionSource _taskCompletionSource =
        taskCompletionSource ?? throw new ArgumentNullException(nameof(taskCompletionSource));

    public async Task Handle(GenerateAiProductAttributeEmbeddingsMessage message, CancellationToken ct)
    {
        GenerateAiProductAttributeEmbeddingsCommandResult result =
            await _mediator.Send(new GenerateAiProductAttributeEmbeddingsCommand(message.Requests), ct);

        _taskCompletionSource.SetResult(result.Response);
    }
}
