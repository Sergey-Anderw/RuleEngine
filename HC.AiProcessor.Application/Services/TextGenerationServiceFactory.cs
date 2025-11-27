using System.ClientModel;
using HC.AiProcessor.Application.Constants;
using HC.AiProcessor.Application.Models;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents.OpenAI;
using Microsoft.SemanticKernel.TextGeneration;
using OpenAI;

namespace HC.AiProcessor.Application.Services;

public interface ITextGenerationServiceFactory
{
    ITextGenerationService Create(ChatCompletionConfig config);
}

internal sealed class TextGenerationServiceFactory(IHttpClientFactory httpClientFactory) : ITextGenerationServiceFactory
{
    private readonly IHttpClientFactory _httpClientFactory =
        httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));

    public ITextGenerationService Create(ChatCompletionConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        OpenAIClient client = OpenAIAssistantAgent.CreateOpenAIClient(
            apiKey: new ApiKeyCredential(config.ApiKey),
            httpClient: _httpClientFactory.CreateClient(WellKnownHttpClients.AiClient));

        Kernel kernel = Kernel.CreateBuilder()
            .AddOpenAIChatCompletion(config.Model, client)
            .Build();

        var textGenerationService = kernel.GetRequiredService<ITextGenerationService>();
        return textGenerationService;
    }
}
