using System.Runtime.CompilerServices;
using HC.AiProcessor.Application.Models;
using HC.AiProcessor.Application.Extensions;
using HC.AiProcessor.Entity.Ai.Enums;
using HC.Packages.AiProcessor.V1.Models;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.TextGeneration;

namespace HC.AiProcessor.Application.Services;

public interface IAiStreamingTranslationService
{
    Task<IAsyncEnumerable<AiProcessorStreamingTranslateResponse>> TranslateAsync(
        AiProcessorStreamingTranslateRequest request,
        CancellationToken ct = default);
}

internal sealed class ChatGptStreamingTranslationService(
    IServiceProvider serviceProvider,
    ISystemClock clock,
    IOptions<AiProcessorConfig> aiProcessorConfigAccessor,
    ITextGenerationServiceFactory textGenerationServiceFactory,
    ITemplateEngine templateEngine)
    : AiProcessorChatCompletionServiceBase<ChatGptTranslationSettings>(
        aiSettingsType: AiSettingsType.StreamingTranslationChatGpt,
        serviceProvider,
        clock,
        aiProcessorConfigAccessor,
        textGenerationServiceFactory), IAiStreamingTranslationService
{
    private readonly ITemplateEngine _templateEngine =
        templateEngine ?? throw new ArgumentNullException(nameof(templateEngine));

    public async Task<IAsyncEnumerable<AiProcessorStreamingTranslateResponse>> TranslateAsync(
        AiProcessorStreamingTranslateRequest request,
        CancellationToken ct)
    {
        await TryLoadSettingsAsync(request.ClientId, ct);

        ChatGptTranslationSettings translationSettings = GetSettings(request.ClientId, request.Flow);
        ITextGenerationService textGenerationService = GetTextGenerationService(request.ClientId);

        var executionSettings = new OpenAIPromptExecutionSettings
        {
            ChatSystemPrompt = RenderPrompt(translationSettings.SetupRequest, request)
        };
        string prompt = RenderPrompt(translationSettings.Prompt, request)!;

        IAsyncEnumerable<StreamingTextContent> streamingTextContents = textGenerationService
            .GetStreamingTextContentsAsync(prompt, executionSettings, cancellationToken: ct);

        return GetStreamingContentAsync(streamingTextContents, ct);
    }

    private string? RenderPrompt(string? template, AiProcessorStreamingTranslateRequest request)
    {
        if (string.IsNullOrWhiteSpace(template))
            return null;

        var ctx = new TranslationPromptRenderContext
        {
            Content = request.Text,
            SourceLanguage = request.SourceLanguage,
            TargetLanguage = request.TargetLanguage
        };

        IParsedTemplate? parsedTemplate = _templateEngine.ParseTemplate(template);
        string result = parsedTemplate!.Render(ctx);

        return result;
    }

    private static async IAsyncEnumerable<AiProcessorStreamingTranslateResponse> GetStreamingContentAsync(
        IAsyncEnumerable<StreamingTextContent> streamingTextContents,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (StreamingTextContent textContent in streamingTextContents.WithCancellation(ct))
        {
            if (textContent.IsLastPart())
                break;

            yield return new AiProcessorStreamingTranslateResponse { Text = textContent.Text };
        }
    }
}
