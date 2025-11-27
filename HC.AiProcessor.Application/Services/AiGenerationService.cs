using System.Runtime.CompilerServices;
using HC.AiProcessor.Application.Models;
using HC.AiProcessor.Application.Extensions;
using HC.AiProcessor.Entity.Ai.Enums;
using HC.Packages.AiProcessor.V1.Models;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.TextGeneration;

namespace HC.AiProcessor.Application.Services;

public interface IAiGenerationService
{
    Task<AiProcessorGenerateResponse> GenerateAsync(
        AiProcessorGenerateRequest request,
        CancellationToken cancellationToken = default);

    Task<AiProcessorBatchResponse<AiProcessorGenerateResponse>> BatchGenerateAsync(
        AiProcessorBatchRequest<AiProcessorGenerateRequest> request,
        CancellationToken cancellationToken = default);

    Task<IAsyncEnumerable<AiProcessorStreamingGenerateResponse>> StreamingGenerateAsync(
        AiProcessorStreamingGenerateRequest request,
        CancellationToken cancellationToken = default);
}

internal sealed class ChatGptGenerationService(
    IServiceProvider serviceProvider,
    ISystemClock clock,
    IOptions<AiProcessorConfig> aiProcessorConfigAccessor,
    ITextGenerationServiceFactory textGenerationServiceFactory,
    ITemplateEngine templateEngine,
    IAiTextGenerationInputBatchProcessor textGenerationBatchProcessor,
    ILogger<ChatGptGenerationService> logger)
    : AiProcessorChatCompletionServiceBase<ChatGptGenerationSettings>(
        aiSettingsType: AiSettingsType.GenerationChatGpt,
        serviceProvider,
        clock,
        aiProcessorConfigAccessor,
        textGenerationServiceFactory), IAiGenerationService
{
    private const string Tag = "AI_GENERATION";

    private readonly ITemplateEngine _templateEngine =
        templateEngine ?? throw new ArgumentNullException(nameof(templateEngine));

    private readonly IAiTextGenerationInputBatchProcessor _textGenerationBatchProcessor =
        textGenerationBatchProcessor ?? throw new ArgumentNullException(nameof(textGenerationBatchProcessor));

    private readonly ILogger<ChatGptGenerationService> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<AiProcessorGenerateResponse> GenerateAsync(
        AiProcessorGenerateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Attributes.Count == 0)
        {
            _logger.LogWarning(
                "[{Tag}] Request has no attributes. Returning empty response. ClientId={ClientId}, Flow={Flow}",
                Tag, request.ClientId, request.Flow);
            return new AiProcessorGenerateResponse();
        }

        await TryLoadSettingsAsync(request.ClientId, cancellationToken);

        ChatGptGenerationSettings generationSettings = GetSettings(request.ClientId, request.Flow);
        ITextGenerationService textGenerationService = GetTextGenerationService(request.ClientId);

        var executionSettings = new OpenAIPromptExecutionSettings
        {
            ChatSystemPrompt = RenderPrompt(generationSettings.SetupRequest, request)
        };
        string prompt = RenderPrompt(generationSettings.Prompt, request)!;

        IReadOnlyList<TextContent> textContents = await textGenerationService.GetTextContentsAsync(
            prompt,
            executionSettings,
            cancellationToken: cancellationToken);

        var response = new AiProcessorGenerateResponse
        {
            Text = string.Join(Environment.NewLine, textContents)
        };

        return response;
    }

    public async Task<AiProcessorBatchResponse<AiProcessorGenerateResponse>> BatchGenerateAsync(
        AiProcessorBatchRequest<AiProcessorGenerateRequest> request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        int inputsCount = request.Inputs.Count;

        if (inputsCount == 0)
        {
            return new AiProcessorBatchResponse<AiProcessorGenerateResponse> { Outputs = [] };
        }

        AiProcessorGenerateRequest firstRequest = request.Inputs.First().Body;

        if (inputsCount > 1)
        {
            IEnumerable<AiProcessorGenerateRequest> otherRequests =
                request.Inputs.Skip(1).Select(x => x.Body);

            if (otherRequests.Any(x => x.ClientId != firstRequest.ClientId || x.Flow != firstRequest.Flow))
                throw new InvalidOperationException("All requests must have the same client id and flow.");
        }

        await TryLoadSettingsAsync(firstRequest.ClientId, cancellationToken);

        ChatCompletionConfig config = GetConfig(firstRequest.ClientId);
        ChatGptGenerationSettings generationSettings = GetSettings(firstRequest.ClientId, firstRequest.Flow);

        AiProcessorBatchResponse<AiTextGenerationOutput> batchProcessorResponse =
            await _textGenerationBatchProcessor.ProcessAsync(
                request.Inputs
                    .Select(x => new AiProcessorBatchInput<AiTextGenerationInput>
                    {
                        Id = x.Id,
                        Body = new AiTextGenerationInput
                        {
                            SystemPrompt = RenderPrompt(generationSettings.SetupRequest, x.Body),
                            UserPrompt = RenderPrompt(generationSettings.Prompt, x.Body)
                        }
                    }),
                config,
                cancellationToken);

        if (batchProcessorResponse.Error is not null)
        {
            return new AiProcessorBatchResponse<AiProcessorGenerateResponse>
                { Error = batchProcessorResponse.Error };
        }

        var response = new AiProcessorBatchResponse<AiProcessorGenerateResponse>
        {
            Outputs = batchProcessorResponse.Outputs!
                .Select(x => new AiProcessorBatchOutput<AiProcessorGenerateResponse>
                {
                    Id = x.Id,
                    Body = x.Body is not null ? new AiProcessorGenerateResponse { Text = x.Body.Content } : null,
                    Error = x.Error
                })
                .ToList()
        };

        return response;
    }

    public async Task<IAsyncEnumerable<AiProcessorStreamingGenerateResponse>> StreamingGenerateAsync(
        AiProcessorStreamingGenerateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await TryLoadSettingsAsync(request.ClientId, cancellationToken);

        ChatGptGenerationSettings generationSettings = GetSettings(request.ClientId, request.Flow);
        ITextGenerationService textGenerationService = GetTextGenerationService(request.ClientId);

        var executionSettings = new OpenAIPromptExecutionSettings
        {
            ChatSystemPrompt = RenderPrompt(generationSettings.SetupRequest, request)
        };
        string prompt = RenderPrompt(generationSettings.Prompt, request)!;

        IAsyncEnumerable<StreamingTextContent> streamingTextContents = textGenerationService
            .GetStreamingTextContentsAsync(prompt, executionSettings, cancellationToken: cancellationToken);

        return GetStreamingContentAsync(streamingTextContents, cancellationToken);
    }

    private string? RenderPrompt(string? template, AiProcessorGenerateRequestBase request)
    {
        if (string.IsNullOrWhiteSpace(template))
            return null;

        var ctx = new GenerationPromptRenderContext
        {
            Language = request.Language.Trim(),
            ToneOfVoiceInstructions = request.ToneOfVoice.Trim(),
            AttributeValues = request.Attributes
                .DistinctBy(x => x.Label.Trim())
                .ToDictionary(x => x.Label.Trim(), x => x.Value.Trim()),
            AttributeDescriptions = request.Attributes
                .Where(x => !string.IsNullOrWhiteSpace(x.Description))
                .DistinctBy(x => x.Label.Trim())
                .ToDictionary(x => x.Label.Trim(), x => x.Description!.Trim()),
            MinLength = GetLengthValue(request.MinLength),
            MaxLength = GetLengthValue(request.MaxLength),
            AllowHtml = request.AllowHtml,
            AdditionalInstructions = request.AdditionalInstructions?.Trim()
        };

        IParsedTemplate? parsedTemplate = _templateEngine.ParseTemplate(template);
        string result = parsedTemplate!.Render(ctx);

        return result;
    }

    private static async IAsyncEnumerable<AiProcessorStreamingGenerateResponse> GetStreamingContentAsync(
        IAsyncEnumerable<StreamingTextContent> streamingTextContents,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (StreamingTextContent textContent in streamingTextContents.WithCancellation(ct))
        {
            if (textContent.IsLastPart())
                break;

            yield return new AiProcessorStreamingGenerateResponse { Text = textContent.Text };
        }
    }

    private static int? GetLengthValue(int value)
    {
        value = Math.Max(0, value);
        if (value == 0)
            return null;
        return value;
    }
}
