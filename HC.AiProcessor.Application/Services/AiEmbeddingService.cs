using System.Diagnostics.CodeAnalysis;
using HC.AiProcessor.Application.Models;
using HC.AiProcessor.Entity.Ai.Enums;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel.Embeddings;
using Pgvector;

namespace HC.AiProcessor.Application.Services;

public interface IAiEmbeddingService
{
    Task<Vector> EmbeddingAsync(
        long clientId,
        string text,
        CancellationToken ct = default);

    Task<IList<Vector>> EmbeddingsAsync(
        long clientId,
        IList<string> texts,
        CancellationToken ct = default);
}

internal sealed class ChatGptEmbeddingService(
    IServiceProvider serviceProvider,
    ISystemClock clock,
    IOptions<AiProcessorConfig> aiProcessorConfigAccessor,
    ITextEmbeddingGenerationServiceFactory textEmbeddingGenerationServiceFactory)
    : AiProcessorTextEmbeddingGenerationServiceBase<ChatGptEmbeddingSettings>(
        aiSettingsType: AiSettingsType.EmbeddingChatGpt,
        serviceProvider,
        clock,
        aiProcessorConfigAccessor,
        textEmbeddingGenerationServiceFactory), IAiEmbeddingService
{
    [Experimental("SKEXP0001")]
    public async Task<Vector> EmbeddingAsync(long clientId, string text, CancellationToken ct = default)
    {
        await TryLoadSettingsAsync(clientId, ct);

        ITextEmbeddingGenerationService embeddingGenerator = GetTextEmbeddingGenerationService(clientId);

        IList<ReadOnlyMemory<float>> embeddings =
            await embeddingGenerator.GenerateEmbeddingsAsync([text], cancellationToken: ct);

        return new Vector(embeddings.First());
    }

    [Experimental("SKEXP0001")]
    public async Task<IList<Vector>> EmbeddingsAsync(
        long clientId,
        IList<string> texts,
        CancellationToken ct = default)
    {
        await TryLoadSettingsAsync(clientId, ct);

        ITextEmbeddingGenerationService embeddingGenerator = GetTextEmbeddingGenerationService(clientId);

        IList<ReadOnlyMemory<float>> embeddings =
            await embeddingGenerator.GenerateEmbeddingsAsync(texts, cancellationToken: ct);

        return embeddings
            .Select(x => new Vector(x))
            .ToList();
    }
}
