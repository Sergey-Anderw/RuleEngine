using System.Diagnostics.CodeAnalysis;
using HC.AiProcessor.Application.Constants;
using HC.AiProcessor.Application.Models;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Embeddings;

namespace HC.AiProcessor.Application.Services;

public interface ITextEmbeddingGenerationServiceFactory
{
    [Experimental("SKEXP0001")]
    ITextEmbeddingGenerationService Create(ChatGptTextEmbeddingGenerationConfig config);
}

internal sealed class TextEmbeddingGenerationServiceFactory(
    IHttpClientFactory httpClientFactory) : ITextEmbeddingGenerationServiceFactory
{
    private readonly IHttpClientFactory _httpClientFactory =
        httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));

    [Experimental("SKEXP0001")]
    public ITextEmbeddingGenerationService Create(ChatGptTextEmbeddingGenerationConfig config)
    {
        HttpClient httpClient = _httpClientFactory.CreateClient(WellKnownHttpClients.AiClient);

        Kernel kernel = Kernel.CreateBuilder()
            .AddOpenAITextEmbeddingGeneration(
                config.ModelId,
                config.ApiKey,
                httpClient: httpClient,
                dimensions: 512)
            .Build();

        var textEmbeddingGenerationService = kernel.GetRequiredService<ITextEmbeddingGenerationService>();
        return textEmbeddingGenerationService;
    }
}
