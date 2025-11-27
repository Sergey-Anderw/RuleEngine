using System.Text.Json;
using HC.AiProcessor.Application.Common;
using HC.AiProcessor.Application.Constants;
using HC.AiProcessor.Application.Exceptions;
using HC.AiProcessor.Application.Models;
using HC.AiProcessor.Entity.Ai.Enums;
using HC.Packages.AiProcessor.V1.Models;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.TextGeneration;

namespace HC.AiProcessor.Application.Services;

public interface IAiTranslationService
{
    Task<AiProcessorTranslateResponse> TranslateAsync(
        AiProcessorTranslateRequest request,
        CancellationToken cancellationToken = default);

    Task<AiProcessorBatchResponse<AiProcessorTranslateResponse>> BatchTranslateAsync(
        AiProcessorBatchRequest<AiProcessorTranslateRequest> request,
        CancellationToken cancellationToken = default);
}

internal sealed class ChatGptTranslationService(
    IServiceProvider serviceProvider,
    ISystemClock clock,
    IOptions<AiProcessorConfig> aiProcessorConfigAccessor,
    ITextGenerationServiceFactory textGenerationServiceFactory,
    ITemplateEngine templateEngine,
    IAiTextGenerationInputBatchProcessor textGenerationBatchProcessor,
    ILogger<ChatGptTranslationService> logger)
    : AiProcessorChatCompletionServiceBase<ChatGptTranslationSettings>(
        aiSettingsType: AiSettingsType.TranslationChatGpt,
        serviceProvider,
        clock,
        aiProcessorConfigAccessor,
        textGenerationServiceFactory), IAiTranslationService
{
    private readonly ITemplateEngine _templateEngine =
        templateEngine ?? throw new ArgumentNullException(nameof(templateEngine));

    private readonly IAiTextGenerationInputBatchProcessor _textGenerationBatchProcessor =
        textGenerationBatchProcessor ?? throw new ArgumentNullException(nameof(textGenerationBatchProcessor));

    private readonly ILogger<ChatGptTranslationService> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<AiProcessorTranslateResponse> TranslateAsync(
        AiProcessorTranslateRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await TryLoadSettingsAsync(request.ClientId, cancellationToken);

        ChatGptTranslationSettings translationSettings = GetSettings(request.ClientId, request.Flow);
        ITextGenerationService textGenerationService = GetTextGenerationService(request.ClientId);

        var executionSettings = new OpenAIPromptExecutionSettings
        {
            ChatSystemPrompt = RenderPrompt(translationSettings.SetupRequest, request)
        };
        string prompt = RenderPrompt(translationSettings.Prompt, request)!;

        IReadOnlyList<TextContent> textContents = await textGenerationService.GetTextContentsAsync(
            prompt,
            executionSettings,
            cancellationToken: cancellationToken);

        string content = string.Join(Environment.NewLine, textContents);
        var items = AiAgentResponseHelper.ToObjectFromJson<Dictionary<string, string>>(content);

        var response = new AiProcessorTranslateResponse { Items = items };
        return response;
    }

    public async Task<AiProcessorBatchResponse<AiProcessorTranslateResponse>> BatchTranslateAsync(
        AiProcessorBatchRequest<AiProcessorTranslateRequest> request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        int inputsCount = request.Inputs.Count;

        if (inputsCount == 0)
        {
            return new AiProcessorBatchResponse<AiProcessorTranslateResponse> { Outputs = [] };
        }

        AiProcessorTranslateRequest firstRequest = request.Inputs.First().Body;

        if (inputsCount > 1)
        {
            IEnumerable<AiProcessorTranslateRequest> otherRequests = request.Inputs.Skip(1).Select(x => x.Body);

            if (otherRequests.Any(x => x.ClientId != firstRequest.ClientId || x.Flow != firstRequest.Flow))
                throw new InvalidOperationException("All requests must have the same client id and flow.");
        }

        await TryLoadSettingsAsync(firstRequest.ClientId, cancellationToken);

        ChatCompletionConfig config = GetConfig(firstRequest.ClientId);
        ChatGptTranslationSettings translationSettings = GetSettings(firstRequest.ClientId, firstRequest.Flow);

        AiProcessorBatchResponse<AiTextGenerationOutput> batchProcessorResponse =
            await _textGenerationBatchProcessor.ProcessAsync(
                request.Inputs
                    .Select(x => new AiProcessorBatchInput<AiTextGenerationInput>
                    {
                        Id = x.Id,
                        Body = new AiTextGenerationInput
                        {
                            SystemPrompt = RenderPrompt(translationSettings.SetupRequest, x.Body),
                            UserPrompt = RenderPrompt(translationSettings.Prompt, x.Body)
                        }
                    }),
                config,
                cancellationToken);

        if (batchProcessorResponse.Error is not null)
        {
            return new AiProcessorBatchResponse<AiProcessorTranslateResponse>
                { Error = batchProcessorResponse.Error };
        }

        var response = new AiProcessorBatchResponse<AiProcessorTranslateResponse>
        {
            Outputs = batchProcessorResponse.Outputs!
                .Select(x =>
                {
                    if (x.Error is not null)
                    {
                        return new AiProcessorBatchOutput<AiProcessorTranslateResponse>
                        {
                            Id = x.Id,
                            Error = x.Error
                        };
                    }

                    try
                    {
                        var items = AiAgentResponseHelper.ToObjectFromJson<Dictionary<string, string>>(x.Body!.Content);

                        return new AiProcessorBatchOutput<AiProcessorTranslateResponse>
                        {
                            Id = x.Id,
                            Body = new AiProcessorTranslateResponse { Items = items }
                        };
                    }
                    catch (AiProcessorException ex)
                    {
                        return new AiProcessorBatchOutput<AiProcessorTranslateResponse>
                        {
                            Id = x.Id,
                            Error = new AiProcessorBatchError
                            {
                                Code = ex.Code,
                                Message = ex.Message
                            }
                        };
                    }
                    catch (Exception ex)
                    {
                        return new AiProcessorBatchOutput<AiProcessorTranslateResponse>
                        {
                            Id = x.Id,
                            Error = new AiProcessorBatchError
                            {
                                Code = ErrorCodes.UnknownError,
                                Message = ex.Message
                            }
                        };
                    }
                })
                .ToArray()
        };

        return response;
    }

    private string? RenderPrompt(string? template, AiProcessorTranslateRequest request)
    {
        if (string.IsNullOrWhiteSpace(template))
            return null;

        var ctx = new TranslationPromptRenderContext
        {
            Content = JsonSerializer.Serialize(request.Items, JsonSettingsExtensions.Default),
            SourceLanguage = request.SourceLanguage,
            TargetLanguage = request.TargetLanguage
        };

        IParsedTemplate? parsedTemplate = _templateEngine.ParseTemplate(template);
        string result = parsedTemplate!.Render(ctx);

        return result;
    }
}
