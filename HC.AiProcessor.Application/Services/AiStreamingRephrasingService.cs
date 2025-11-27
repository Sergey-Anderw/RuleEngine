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

public interface IAiStreamingRephrasingService
{
    Task<IAsyncEnumerable<AiProcessorStreamingRephraseResponse>> RephraseAsync(
        AiProcessorStreamingRephraseRequest request,
        CancellationToken ct = default);
}

internal sealed class ChatGptStreamingRephrasingService(
    IServiceProvider serviceProvider,
    ISystemClock clock,
    IOptions<AiProcessorConfig> aiProcessorConfigAccessor,
    ITextGenerationServiceFactory textGenerationServiceFactory,
    ITemplateEngine templateEngine)
    : AiProcessorChatCompletionServiceBase<ChatGptRephrasingSettings>(
        aiSettingsType: AiSettingsType.StreamingRephrasingChatGpt,
        serviceProvider,
        clock,
        aiProcessorConfigAccessor,
        textGenerationServiceFactory), IAiStreamingRephrasingService
{
    private readonly ITemplateEngine _templateEngine =
        templateEngine ?? throw new ArgumentNullException(nameof(templateEngine));

    public async Task<IAsyncEnumerable<AiProcessorStreamingRephraseResponse>> RephraseAsync(
        AiProcessorStreamingRephraseRequest request,
        CancellationToken ct = default)
    {
        await TryLoadSettingsAsync(request.ClientId, ct);

        ChatGptRephrasingSettings rephrasingSettings = GetSettings(request.ClientId, request.Flow);
        ITextGenerationService textGenerationService = GetTextGenerationService(request.ClientId);

        var executionSettings = new OpenAIPromptExecutionSettings
        {
            ChatSystemPrompt = RenderPrompt(rephrasingSettings.SetupRequest, request)
        };
        string prompt = RenderPrompt(rephrasingSettings.Prompt, request)!;

        IAsyncEnumerable<StreamingTextContent> streamingTextContents = textGenerationService
            .GetStreamingTextContentsAsync(prompt, executionSettings, cancellationToken: ct);

        return GetStreamingContentAsync(streamingTextContents, ct);
    }

    private string? RenderPrompt(string? template, AiProcessorStreamingRephraseRequest request)
    {
        if (string.IsNullOrWhiteSpace(template))
            return null;

        var ctx = new RephrasingPromptRenderContext
        {
            ToneOfVoiceInstructions = request.ToneOfVoice,
            Content = request.Text
        };

        IParsedTemplate? parsedTemplate = _templateEngine.ParseTemplate(template);
        string result = parsedTemplate!.Render(ctx);

        return result;
    }

    private static async IAsyncEnumerable<AiProcessorStreamingRephraseResponse> GetStreamingContentAsync(
        IAsyncEnumerable<StreamingTextContent> streamingTextContents,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (StreamingTextContent textContent in streamingTextContents.WithCancellation(ct))
        {
            if (textContent.IsLastPart())
                break;

            yield return new AiProcessorStreamingRephraseResponse { Text = textContent.Text };
        }
    }
}
