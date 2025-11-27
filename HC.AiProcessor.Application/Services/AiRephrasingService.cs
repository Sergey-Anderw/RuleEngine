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

public interface IAiRephrasingService
{
    Task<AiProcessorRephraseResponse> RephraseAsync(
        AiProcessorRephraseRequest request,
        CancellationToken cancellationToken = default);

    Task<AiProcessorBatchResponse<AiProcessorRephraseResponse>> BatchRephraseAsync(
        AiProcessorBatchRequest<AiProcessorRephraseRequest> request,
        CancellationToken cancellationToken = default);
}

internal sealed class ChatGptRephrasingService(
    IServiceProvider serviceProvider,
    ISystemClock clock,
    IOptions<AiProcessorConfig> aiProcessorConfigAccessor,
    ITextGenerationServiceFactory textGenerationServiceFactory,
    ITemplateEngine templateEngine,
    IAiTextGenerationInputBatchProcessor textGenerationBatchProcessor,
    ILogger<ChatGptRephrasingService> logger)
    : AiProcessorChatCompletionServiceBase<ChatGptRephrasingSettings>(
        aiSettingsType: AiSettingsType.RephrasingChatGpt,
        serviceProvider,
        clock,
        aiProcessorConfigAccessor,
        textGenerationServiceFactory), IAiRephrasingService
{
    private readonly ITemplateEngine _templateEngine =
        templateEngine ?? throw new ArgumentNullException(nameof(templateEngine));

    private readonly IAiTextGenerationInputBatchProcessor _textGenerationBatchProcessor =
        textGenerationBatchProcessor ?? throw new ArgumentNullException(nameof(textGenerationBatchProcessor));

    private readonly ILogger<ChatGptRephrasingService> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<AiProcessorRephraseResponse> RephraseAsync(
        AiProcessorRephraseRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await TryLoadSettingsAsync(request.ClientId, cancellationToken);

        ChatGptRephrasingSettings rephrasingSettings = GetSettings(request.ClientId, request.Flow);
        ITextGenerationService textGenerationService = GetTextGenerationService(request.ClientId);

        var executionSettings = new OpenAIPromptExecutionSettings
        {
            ChatSystemPrompt = RenderPrompt(rephrasingSettings.SetupRequest, request)
        };
        string prompt = RenderPrompt(rephrasingSettings.Prompt, request)!;

        IReadOnlyList<TextContent> textContents = await textGenerationService.GetTextContentsAsync(
            prompt,
            executionSettings,
            cancellationToken: cancellationToken);

        string content = string.Join(Environment.NewLine, textContents);
        var items = AiAgentResponseHelper.ToObjectFromJson<Dictionary<string, string>>(content);

        var response = new AiProcessorRephraseResponse { Items = items };
        return response;
    }

    public async Task<AiProcessorBatchResponse<AiProcessorRephraseResponse>> BatchRephraseAsync(
        AiProcessorBatchRequest<AiProcessorRephraseRequest> request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        int inputsCount = request.Inputs.Count;

        if (inputsCount == 0)
        {
            return new AiProcessorBatchResponse<AiProcessorRephraseResponse> { Outputs = [] };
        }

        AiProcessorRephraseRequest firstRequest = request.Inputs.First().Body;

        if (inputsCount > 1)
        {
            IEnumerable<AiProcessorRephraseRequest> otherRequests = request.Inputs.Skip(1).Select(x => x.Body);

            if (otherRequests.Any(x => x.ClientId != firstRequest.ClientId || x.Flow != firstRequest.Flow))
                throw new InvalidOperationException("All requests must have the same client id and flow.");
        }

        await TryLoadSettingsAsync(firstRequest.ClientId, cancellationToken);

        ChatCompletionConfig config = GetConfig(firstRequest.ClientId);
        ChatGptRephrasingSettings rephrasingSettings = GetSettings(firstRequest.ClientId, firstRequest.Flow);

        AiProcessorBatchResponse<AiTextGenerationOutput> batchProcessorResponse =
            await _textGenerationBatchProcessor.ProcessAsync(
                request.Inputs
                    .Select(x => new AiProcessorBatchInput<AiTextGenerationInput>
                    {
                        Id = x.Id,
                        Body = new AiTextGenerationInput
                        {
                            SystemPrompt = RenderPrompt(rephrasingSettings.SetupRequest, x.Body),
                            UserPrompt = RenderPrompt(rephrasingSettings.Prompt, x.Body)
                        }
                    }),
                config,
                cancellationToken);

        if (batchProcessorResponse.Error is not null)
        {
            return new AiProcessorBatchResponse<AiProcessorRephraseResponse>
                { Error = batchProcessorResponse.Error };
        }

        var response = new AiProcessorBatchResponse<AiProcessorRephraseResponse>
        {
            Outputs = batchProcessorResponse.Outputs!
                .Select(x =>
                {
                    if (x.Error is not null)
                    {
                        return new AiProcessorBatchOutput<AiProcessorRephraseResponse>
                        {
                            Id = x.Id,
                            Error = x.Error
                        };
                    }

                    try
                    {
                        var items = AiAgentResponseHelper.ToObjectFromJson<Dictionary<string, string>>(x.Body!.Content);

                        return new AiProcessorBatchOutput<AiProcessorRephraseResponse>
                        {
                            Id = x.Id,
                            Body = new AiProcessorRephraseResponse { Items = items }
                        };
                    }
                    catch (AiProcessorException ex)
                    {
                        return new AiProcessorBatchOutput<AiProcessorRephraseResponse>
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
                        return new AiProcessorBatchOutput<AiProcessorRephraseResponse>
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

    private string? RenderPrompt(string? template, AiProcessorRephraseRequest request)
    {
        if (string.IsNullOrWhiteSpace(template))
            return null;

        var ctx = new RephrasingPromptRenderContext
        {
            Content = JsonSerializer.Serialize(request.Items, JsonSettingsExtensions.Default),
            ToneOfVoiceInstructions = request.ToneOfVoice
        };

        IParsedTemplate? parsedTemplate = _templateEngine.ParseTemplate(template);
        string result = parsedTemplate!.Render(ctx);

        return result;
    }
}
