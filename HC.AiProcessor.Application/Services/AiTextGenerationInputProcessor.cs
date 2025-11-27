using System.ClientModel;
using System.ClientModel.Primitives;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text;
using HC.AiProcessor.Application.Constants;
using HC.AiProcessor.Application.Exceptions;
using HC.AiProcessor.Application.Models;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Responses;

namespace HC.AiProcessor.Application.Services;

public interface IAiTextGenerationInputProcessor
{
    Task<AiTextGenerationOutput> ProcessAsync(
        AiTextGenerationInput input,
        ChatCompletionConfig config,
        CancellationToken cancellationToken = default);
}

internal sealed class AiTextGenerationInputProcessor(
    IHttpClientFactory httpClientFactory,
    ILogger<AiTextGenerationInputProcessor> logger) : IAiTextGenerationInputProcessor
{
    private const string Tag = "TEXT_GENERATION_INPUT_PROCESSOR";

    // Setting this can make replies slower
    private const bool MaxOutputTokenCountEnabled = true;

    private readonly IHttpClientFactory _httpClientFactory =
        httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));

    private readonly ILogger<AiTextGenerationInputProcessor> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    [Experimental("OPENAI001")]
    public async Task<AiTextGenerationOutput> ProcessAsync(
        AiTextGenerationInput input,
        ChatCompletionConfig config,
        CancellationToken cancellationToken = default)
    {
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            if (MaxOutputTokenCountEnabled)
            {
                _logger.LogDebug(
                    "[{Tag}] Starting text generation. SystemPrompt='{SystemPrompt}', UserPrompt='{UserPrompt}', " +
                    "WebSearchEnabled={WebSearchEnabled}, Temperature={Temperature}, MaxOutputTokenCount={MaxOutputTokenCount}, " +
                    "OutputFormat={OutputFormat}.",
                    Tag,
                    input.SystemPrompt,
                    input.UserPrompt,
                    input.WebSearchEnabled,
                    input.Temperature,
                    input.MaxOutputTokenCount,
                    input.OutputTextFormat?.GetType().Name ?? "none");
            }
            else
            {
                _logger.LogDebug(
                    "[{Tag}] Starting text generation. SystemPrompt='{SystemPrompt}', UserPrompt='{UserPrompt}', " +
                    "WebSearchEnabled={WebSearchEnabled}, Temperature={Temperature}, OutputFormat={OutputFormat}.",
                    Tag,
                    input.SystemPrompt,
                    input.UserPrompt,
                    input.WebSearchEnabled,
                    input.Temperature,
                    input.OutputTextFormat?.GetType().Name ?? "none");
            }
        }

        AiTextGenerationOutput output = await InternalProcessAsync(input, config, cancellationToken);

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "[{Tag}] Finished text generation. Output length={Length}, Content='{Content}'.",
                Tag,
                output.Content?.Length ?? 0,
                output.Content ?? "null");
        }

        return output;
    }

    [Experimental("OPENAI001")]
    private async Task<AiTextGenerationOutput> InternalProcessAsync(
        AiTextGenerationInput input,
        ChatCompletionConfig config,
        CancellationToken cancellationToken)
    {
        switch (config.Model)
        {
            case "gpt-4o-mini-search-preview":
            case "gpt-4o-search-preview":
            {
                try
                {
                    return await GetTextContentByChatCompletionsApiAsync(input, config, cancellationToken);
                }
                catch (HttpOperationException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    throw new InsufficientQuotaException(ex.Message);
                }
            }
            default:
            {
                try
                {
                    return await GetTextContentByResponsesApiAsync(input, config, cancellationToken);
                }
                catch (ClientResultException ex) when (ex.Status == (int) HttpStatusCode.TooManyRequests)
                {
                    throw new InsufficientQuotaException(ex.Message);
                }
            }
        }
    }

    [Experimental("OPENAI001")]
    private async Task<AiTextGenerationOutput> GetTextContentByResponsesApiAsync(
        AiTextGenerationInput input,
        ChatCompletionConfig config,
        CancellationToken ct)
    {
        HttpClient httpClient = _httpClientFactory.CreateClient(WellKnownHttpClients.AiClient);
        OpenAIResponseClient responseClient = new OpenAIResponseClient(
            config.Model,
            credential: new ApiKeyCredential(config.ApiKey),
            options: new OpenAIClientOptions { Transport = new HttpClientPipelineTransport(httpClient) });

        var inputItems = new List<ResponseItem>();

        if (!string.IsNullOrWhiteSpace(input.SystemPrompt))
        {
            inputItems.Add(ResponseItem.CreateDeveloperMessageItem(input.SystemPrompt));
        }

        if (!string.IsNullOrWhiteSpace(input.UserPrompt))
        {
            inputItems.Add(ResponseItem.CreateUserMessageItem(input.UserPrompt));
        }

        ResponseTextFormat textFormat;

        switch (input.OutputTextFormat)
        {
            case null:
            case AiTextGenerationInput.TextFormat:
                textFormat = ResponseTextFormat.CreateTextFormat();
                break;
            case AiTextGenerationInput.JsonObjectFormat:
                textFormat = ResponseTextFormat.CreateJsonObjectFormat();
                break;
            default:
                textFormat = ResponseTextFormat.CreateJsonSchemaFormat(
                    "Response",
                    BinaryData.FromString(((AiTextGenerationInput.JsonSchemaFormat) input.OutputTextFormat).Schema),
                    jsonSchemaIsStrict: true);
                break;
        }

        var options = new ResponseCreationOptions
        {
            TextOptions = new ResponseTextOptions { TextFormat = textFormat },
        };

        if (MaxOutputTokenCountEnabled)
        {
            options.MaxOutputTokenCount = config.MaxOutputTokenCount;
        }

        if (input.WebSearchEnabled)
        {
            options.Tools.Add(ResponseTool.CreateWebSearchTool());
        }

        if (input.Temperature.HasValue)
        {
            options.Temperature = input.Temperature;
        }
        
        if (config.Model.Contains("gpt-5"))
        {
            options.ReasoningOptions = new ResponseReasoningOptions
            {
                ReasoningEffortLevel = ResponseReasoningEffortLevel.Medium
            };
        }

        OpenAIResponse response = await responseClient.CreateResponseAsync(inputItems, options, ct);

        foreach (ResponseItem item in response.OutputItems)
        {
            if (item is not MessageResponseItem message)
                continue;
            string? content = message.Content?.FirstOrDefault()?.Text;

            return new AiTextGenerationOutput { Content = content };
        }

        return new AiTextGenerationOutput();
    }

    [Experimental("OPENAI001")]
    private async Task<AiTextGenerationOutput> GetTextContentByChatCompletionsApiAsync(
        AiTextGenerationInput input,
        ChatCompletionConfig config,
        CancellationToken ct)
    {
        HttpClient httpClient = _httpClientFactory.CreateClient(WellKnownHttpClients.AiClient);
        var chatClient = new ChatClient(
            config.Model,
            credential: new ApiKeyCredential(config.ApiKey),
            options: new OpenAIClientOptions { Transport = new HttpClientPipelineTransport(httpClient) });

        var messages = new List<ChatMessage>();

        if (!string.IsNullOrWhiteSpace(input.SystemPrompt))
        {
            messages.Add(ChatMessage.CreateSystemMessage(input.SystemPrompt));
        }

        if (!string.IsNullOrWhiteSpace(input.UserPrompt))
        {
            messages.Add(ChatMessage.CreateUserMessage(input.UserPrompt));
        }

        ChatResponseFormat? responseFormat;

        switch (input.OutputTextFormat)
        {
            case null:
            case AiTextGenerationInput.TextFormat:
                responseFormat = ChatResponseFormat.CreateTextFormat();
                break;
            case AiTextGenerationInput.JsonObjectFormat:
                responseFormat = ChatResponseFormat.CreateJsonObjectFormat();
                break;
            default:
                responseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                    "Response",
                    BinaryData.FromString(((AiTextGenerationInput.JsonSchemaFormat) input.OutputTextFormat).Schema),
                    jsonSchemaIsStrict: true);
                break;
        }

        var options = new ChatCompletionOptions
        {
            ResponseFormat = responseFormat
        };

        if (MaxOutputTokenCountEnabled)
        {
            options.MaxOutputTokenCount = config.MaxOutputTokenCount;
        }

        if (input.WebSearchEnabled)
        {
            options.WebSearchOptions = new ChatWebSearchOptions();
        }

        if (input.Temperature.HasValue)
        {
            options.Temperature = input.Temperature;
        }

        var contentStringBuilder = new StringBuilder();

        await foreach (StreamingChatCompletionUpdate update in
                       chatClient.CompleteChatStreamingAsync(messages, options, ct))
        {
            foreach (ChatMessageContentPart contentPart in update.ContentUpdate)
            {
                if (contentPart.Kind != ChatMessageContentPartKind.Text)
                    continue;
                contentStringBuilder.Append(contentPart.Text);
            }
        }

        string content = contentStringBuilder.ToString().Trim();
        return new AiTextGenerationOutput { Content = content };
    }
}
