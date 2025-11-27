using System.Text;
using System.Text.Json;
using HC.AiProcessor.Application.Common;
using HC.AiProcessor.Application.Constants;
using HC.AiProcessor.Application.Exceptions;
using HC.AiProcessor.Application.Models;
using HC.AiProcessor.Entity.Ai.Enums;
using HC.Packages.AiProcessor.V1.Models;
using HC.Packages.Catalog.Contracts.V1.Enums;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace HC.AiProcessor.Application.Services;

public interface IAiAttributesPopulationService
{
    Task<AiProcessorPopulateAttributesResponse> PopulateAttributesAsync(
        AiProcessorPopulateAttributesRequest request,
        CancellationToken cancellationToken = default);

    Task<AiProcessorBatchResponse<AiProcessorPopulateAttributesResponse>> BatchPopulateAttributesAsync(
        AiProcessorBatchRequest<AiProcessorPopulateAttributesRequest> request,
        CancellationToken cancellationToken = default);
}

internal sealed partial class ChatGptAttributesPopulationService(
    IServiceProvider serviceProvider,
    ISystemClock clock,
    IOptions<AiProcessorConfig> aiProcessorConfigAccessor,
    ITemplateEngine templateEngine,
    ICpuBoundBatchProcessor cpuBoundBatchProcessor,
    IAiTextGenerationInputProcessor aiTextGenerationInputProcessor,
    ILogger<ChatGptAttributesPopulationService> logger)
    : AiProcessorServiceBase<AttributesPopulationConfig, AttributesPopulationSettings>(
        aiSettingsType: AiSettingsType.AttributesPopulationChatGpt,
        serviceProvider,
        clock,
        aiProcessorConfigAccessor), IAiAttributesPopulationService
{
    private const string Tag = "AI_POPULATION";

    private const int MaxAttributesPerRequest = int.MaxValue;
    private const int MaxOptionsCountPerAttribute = 10;

    private const int OptionExamplesCountPerAttribute = MaxOptionsCountPerAttribute / 2;
    private const bool UseRandomOptionExamples = false;

    private const int BatchProcessingRetryCount = 5;
    private const int BatchProcessingMaxParallelRequests = 50;

    private const bool AppendSourcesToReason = true;

    private const string DateFormat = "yyyy-MM-dd";

    private readonly IServiceProvider _serviceProvider =
        serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

    private readonly ITemplateEngine _templateEngine =
        templateEngine ?? throw new ArgumentNullException(nameof(templateEngine));

    private readonly ICpuBoundBatchProcessor _cpuBoundBatchProcessor =
        cpuBoundBatchProcessor ?? throw new ArgumentNullException(nameof(cpuBoundBatchProcessor));

    private readonly IAiTextGenerationInputProcessor _aiTextGenerationInputProcessor =
        aiTextGenerationInputProcessor ?? throw new ArgumentNullException(nameof(aiTextGenerationInputProcessor));

    private readonly ILogger<ChatGptAttributesPopulationService> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<AiProcessorPopulateAttributesResponse> PopulateAttributesAsync(
        AiProcessorPopulateAttributesRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await TryLoadSettingsAsync(request.ClientId, cancellationToken);

        AttributesPopulationConfig config = GetConfig(request.ClientId);

        if (!GetSettings(request.ClientId).TryGetValue(
                request.Flow,
                out AttributesPopulationSettings? settings) || settings is null)
            throw new Exception($"AI settings for flow {request.Flow} was not found");

        AttributesPopulationContext context = CreateContext(config, settings, [request]);

        AttributesPopulationInput input = CreateAttributesPopulationInput(request, context);

        AiTextGenerationOutput textGenerationOutput = await _aiTextGenerationInputProcessor.ProcessAsync(
            input.TextGenerationInput,
            config,
            cancellationToken);

        AiProcessorPopulateAttributesResponse response = await GetResponseAsync(
            output: new AttributesPopulationOutput
            {
                Input = input,
                TextGenerationOutput = textGenerationOutput
            },
            context,
            cancellationToken);

        return response;
    }

    public async Task<AiProcessorBatchResponse<AiProcessorPopulateAttributesResponse>> BatchPopulateAttributesAsync(
        AiProcessorBatchRequest<AiProcessorPopulateAttributesRequest> request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        int inputsCount = request.Inputs.Count;

        if (inputsCount == 0)
        {
            return new AiProcessorBatchResponse<AiProcessorPopulateAttributesResponse> { Outputs = [] };
        }

        AiProcessorPopulateAttributesRequest firstRequest = request.Inputs.First().Body;

        if (inputsCount > 1)
        {
            IEnumerable<AiProcessorPopulateAttributesRequest>
                otherRequests = request.Inputs.Skip(1).Select(x => x.Body);

            if (otherRequests.Any(x => x.ClientId != firstRequest.ClientId || x.Flow != firstRequest.Flow))
                throw new InvalidOperationException("All requests must have the same client id and flow.");
        }

        await TryLoadSettingsAsync(firstRequest.ClientId, cancellationToken);

        AttributesPopulationConfig config = GetConfig(firstRequest.ClientId);

        if (!GetSettings(firstRequest.ClientId).TryGetValue(
                firstRequest.Flow,
                out AttributesPopulationSettings? settings) || settings is null)
            throw new KeyNotFoundException($"AI settings for flow {firstRequest.Flow} was not found");

        AttributesPopulationContext context = CreateContext(
            config,
            settings,
            request.Inputs.Select(x => x.Body));

        AsyncRetryPolicy retryPolicy = CreateBatchProcessingRetryPolicy();

        AiProcessorBatchResponse<AttributesPopulationOutput> batchProcessorResponse =
            await _cpuBoundBatchProcessor.ProcessAsync(
                request.Inputs,
                process: async i =>
                {
                    AttributesPopulationInput input = CreateAttributesPopulationInput(i.Body, context);
                    AiTextGenerationOutput textGenerationOutput = await retryPolicy.ExecuteAsync(
                        _ => _aiTextGenerationInputProcessor
                            .ProcessAsync(input.TextGenerationInput, config, cancellationToken),
                        cancellationToken);

                    return new AiProcessorBatchOutput<AttributesPopulationOutput>
                    {
                        Id = i.Id,
                        Body = new AttributesPopulationOutput
                        {
                            Input = input,
                            TextGenerationOutput = textGenerationOutput
                        }
                    };
                },
                processingOptions: new CpuBoundBatchProcessingOptions
                {
                    MaxDegreeOfParallelism = BatchProcessingMaxParallelRequests
                },
                cancellationToken);

        AiProcessorBatchResponse<AiProcessorPopulateAttributesResponse> response = await GetBatchResponseAsync(
            batchProcessorResponse,
            context,
            cancellationToken);

        return response;
    }

    private async Task<AiProcessorPopulateAttributesResponse> GetResponseAsync(
        AttributesPopulationOutput output,
        AttributesPopulationContext context,
        CancellationToken cancellationToken = default)
    {
        ProcessedAttributesPopulationOutput processedOutput = ProcessAttributesPopulationOutput(output);

        List<UnrecognizedSelectableAttributePopulationResult> unrecognizedResults = processedOutput.UnrecognizedResults;
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> optionsMappingResults;

        if (unrecognizedResults.Count != 0)
        {
            optionsMappingResults = await GetOptionsMappingResultsAsync(
                CreateSyncOptionsMappingProcessor(context.Settings),
                unrecognizedResults,
                context,
                cancellationToken);
        }
        else
        {
            optionsMappingResults = new Dictionary<string, IReadOnlyDictionary<string, string>>();
        }

        AiProcessorPopulateAttributesResponse response =
            CreatePopulateAttributesResponse(processedOutput, optionsMappingResults);

        return response;
    }

    private async Task<AiProcessorBatchResponse<AiProcessorPopulateAttributesResponse>> GetBatchResponseAsync(
        AiProcessorBatchResponse<AttributesPopulationOutput> batchProcessorResponse,
        AttributesPopulationContext context,
        CancellationToken cancellationToken = default)
    {
        if (batchProcessorResponse.Error is not null)
        {
            return new AiProcessorBatchResponse<AiProcessorPopulateAttributesResponse>
            {
                Error = batchProcessorResponse.Error
            };
        }

        var batchOutputs = new List<AiProcessorBatchOutput<AiProcessorPopulateAttributesResponse>>();
        var processedOutputs = new Dictionary<string, ProcessedAttributesPopulationOutput>();

        foreach (AiProcessorBatchOutput<AttributesPopulationOutput> batchOutput in batchProcessorResponse.Outputs!)
        {
            if (batchOutput.Error is not null)
            {
                batchOutputs.Add(
                    new AiProcessorBatchOutput<AiProcessorPopulateAttributesResponse>
                    {
                        Id = batchOutput.Id,
                        Error = batchOutput.Error
                    });
                continue;
            }

            try
            {
                ProcessedAttributesPopulationOutput processedOutput =
                    ProcessAttributesPopulationOutput(batchOutput.Body!);
                processedOutputs[batchOutput.Id] = processedOutput;
            }
            catch (AiProcessorException ex)
            {
                batchOutputs.Add(
                    new AiProcessorBatchOutput<AiProcessorPopulateAttributesResponse>
                    {
                        Id = batchOutput.Id,
                        Error = new AiProcessorBatchError { Code = ex.Code, Message = ex.Message }
                    });
            }
            catch (Exception ex)
            {
                batchOutputs.Add(
                    new AiProcessorBatchOutput<AiProcessorPopulateAttributesResponse>
                    {
                        Id = batchOutput.Id,
                        Error = new AiProcessorBatchError { Code = ErrorCodes.UnknownError, Message = ex.Message }
                    });
            }
        }

        List<UnrecognizedSelectableAttributePopulationResult> unrecognizedResults = processedOutputs
            .Values
            .SelectMany(x => x.UnrecognizedResults)
            .ToList();

        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> optionsMappingResults;

        if (unrecognizedResults.Count != 0)
        {
            optionsMappingResults = await GetOptionsMappingResultsAsync(
                CreateAsyncOptionsMappingProcessor(context.Settings),
                unrecognizedResults,
                context,
                cancellationToken);
        }
        else
        {
            optionsMappingResults = new Dictionary<string, IReadOnlyDictionary<string, string>>();
        }

        foreach ((string id, ProcessedAttributesPopulationOutput processedOutput) in processedOutputs)
        {
            try
            {
                AiProcessorPopulateAttributesResponse response =
                    CreatePopulateAttributesResponse(processedOutput, optionsMappingResults);

                batchOutputs.Add(new AiProcessorBatchOutput<AiProcessorPopulateAttributesResponse>
                {
                    Id = id,
                    Body = response
                });
            }
            catch (AiProcessorException ex)
            {
                batchOutputs.Add(
                    new AiProcessorBatchOutput<AiProcessorPopulateAttributesResponse>
                    {
                        Id = id,
                        Error = new AiProcessorBatchError { Code = ex.Code, Message = ex.Message }
                    });
            }
            catch (Exception ex)
            {
                batchOutputs.Add(
                    new AiProcessorBatchOutput<AiProcessorPopulateAttributesResponse>
                    {
                        Id = id,
                        Error = new AiProcessorBatchError { Code = ErrorCodes.UnknownError, Message = ex.Message }
                    });
            }
        }

        return new AiProcessorBatchResponse<AiProcessorPopulateAttributesResponse> { Outputs = batchOutputs };
    }

    private ProcessedAttributesPopulationOutput ProcessAttributesPopulationOutput(AttributesPopulationOutput output)
    {
        AiTextGenerationOutput textGenerationOutput = output.TextGenerationOutput;
        string? content = textGenerationOutput.Content;

        var populationResponse = AiAgentResponseHelper.ToObjectFromJson<AttributesPopulationResponse>(content);

        List<UnrecognizedSelectableAttributePopulationResult> unrecognizedResults =
            GetUnrecognizedSelectableAttributePopulationResult(
                populationResponse.Results,
                output.Input.Request.Attributes);

        return new ProcessedAttributesPopulationOutput
        {
            Response = populationResponse,
            UnrecognizedResults = unrecognizedResults,
            Input = output.Input
        };
    }

    private void TryRepairResults(
        IReadOnlyCollection<AttributePopulationResult> results,
        IReadOnlyCollection<AiProcessorPopulateAttributesRequest.Attribute> attributes,
        IReadOnlyCollection<string> selectableAttributeCodesToRepair,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> optionsMappingResults)
    {
        foreach (string attributeCode in selectableAttributeCodesToRepair)
        {
            _logger.LogDebug("[{Tag}] Processing attribute code: {AttributeCode}", Tag, attributeCode);

            AttributePopulationResult? result = results.FirstOrDefault(x => x.Code == attributeCode);
            if (result is null)
            {
                _logger.LogWarning("[{Tag}] No result found for attribute code: {AttributeCode}", Tag, attributeCode);
                continue;
            }

            AiProcessorPopulateAttributesRequest.Attribute? attribute =
                attributes.FirstOrDefault(x => x.Code == attributeCode);

            if (attribute is null)
            {
                _logger.LogWarning("[{Tag}] No attribute metadata found for code: {AttributeCode}", Tag, attributeCode);
                continue;
            }

            switch (attribute.ValueType)
            {
                case AttributeValueTypeEnum.Select:
                {
                    var optionValue = result.Value.ToString();

                    if (string.IsNullOrWhiteSpace(optionValue))
                    {
                        _logger.LogWarning(
                            "[{Tag}] Empty or null option value for attribute: {AttributeCode}",
                            Tag, attributeCode);
                        continue;
                    }

                    AiProcessorPopulateAttributesRequest.AttributeOption? option = attribute.Settings?.Options?
                        .FirstOrDefault(x => string.Equals(x.Value, optionValue, StringComparison.Ordinal));

                    if (option is not null)
                    {
                        _logger.LogDebug(
                            "[{Tag}] Exact match found for option value: {Value} in attribute: {AttributeCode}",
                            Tag, optionValue, attributeCode);
                        continue;
                    }

                    _logger.LogWarning(
                        "[{Tag}] Exact match not found for option value: {Value} in attribute: {AttributeCode}",
                        Tag, optionValue, attributeCode);

                    option = attribute.Settings?.Options?
                        .FirstOrDefault(x => string.Equals(x.Value, optionValue, StringComparison.OrdinalIgnoreCase));

                    if (option is not null)
                    {
                        result.Value = option.Value;
                        _logger.LogInformation(
                            "[{Tag}] Case-insensitive match applied for option value: {OriginalValue} => {MappedValue} in attribute: {AttributeCode}",
                            Tag, optionValue, option.Value, attributeCode);
                        continue;
                    }

                    if (!optionsMappingResults.TryGetValue(
                            attributeCode,
                            out IReadOnlyDictionary<string, string>? optionsMappingResult))
                    {
                        _logger.LogWarning(
                            "[{Tag}] No mapping found for attribute: {AttributeCode}",
                            Tag, attributeCode);
                        continue;
                    }

                    if (!optionsMappingResult.TryGetValue(optionValue, out string? mappedOptionValue) ||
                        string.IsNullOrWhiteSpace(mappedOptionValue))
                    {
                        _logger.LogWarning(
                            "[{Tag}] No valid mapped value found for option: {OptionValue} in attribute: {AttributeCode}",
                            Tag, optionValue, attributeCode);
                        continue;
                    }

                    option = attribute.Settings?.Options?
                        .FirstOrDefault(x => string.Equals(x.Value, mappedOptionValue, StringComparison.Ordinal));

                    if (option is null)
                    {
                        _logger.LogWarning(
                            "[{Tag}] Mapped option value not found in options: {MappedValue} for attribute: {AttributeCode}",
                            Tag, mappedOptionValue, attributeCode);
                        continue;
                    }

                    result.Value = option.Value;
                    _logger.LogInformation(
                        "[{Tag}] Mapped option value applied: {OriginalValue} => {MappedValue} for attribute: {AttributeCode}",
                        Tag, optionValue, option.Value, attributeCode);
                    break;
                }
                case AttributeValueTypeEnum.MultiSelect:
                {
                    var optionValues = (result.Value as List<string>)?.Distinct().ToList();

                    if (optionValues is null || optionValues.Count == 0)
                    {
                        _logger.LogWarning(
                            "[{Tag}] No values to process for multi-select attribute: {AttributeCode}",
                            Tag, attributeCode);
                        continue;
                    }

                    var validOptionValues = new List<string>();

                    foreach (string optionValue in optionValues)
                    {
                        if (string.IsNullOrWhiteSpace(optionValue))
                        {
                            _logger.LogWarning(
                                "[{Tag}] Skipping empty or whitespace option value in attribute: {AttributeCode}",
                                Tag, attributeCode);
                            continue;
                        }

                        AiProcessorPopulateAttributesRequest.AttributeOption? option = attribute.Settings?.Options?
                            .FirstOrDefault(x => string.Equals(x.Value, optionValue, StringComparison.Ordinal));

                        if (option is not null)
                        {
                            validOptionValues.Add(optionValue);
                            _logger.LogDebug(
                                "[{Tag}] Exact match retained: {Value} for attribute: {AttributeCode}",
                                Tag, optionValue, attributeCode);
                            continue;
                        }

                        _logger.LogWarning(
                            "[{Tag}] Exact match not found for: {OptionValue} in attribute: {AttributeCode}",
                            Tag, optionValue, attributeCode);

                        option = attribute.Settings?.Options?
                            .FirstOrDefault(x =>
                                string.Equals(x.Value, optionValue, StringComparison.OrdinalIgnoreCase));

                        if (option is not null)
                        {
                            validOptionValues.Add(option.Value);
                            _logger.LogInformation(
                                "[{Tag}] Case-insensitive match applied: {OriginalValue} => {MappedValue} for attribute: {AttributeCode}",
                                Tag, optionValue, option.Value, attributeCode);
                            continue;
                        }

                        if (!optionsMappingResults.TryGetValue(
                                attributeCode,
                                out IReadOnlyDictionary<string, string>? optionsMappingResult))
                        {
                            _logger.LogWarning(
                                "[{Tag}] No mapping found for attribute: {AttributeCode}",
                                Tag, attributeCode);
                            continue;
                        }

                        if (!optionsMappingResult.TryGetValue(optionValue, out string? mappedOptionValue) ||
                            string.IsNullOrWhiteSpace(mappedOptionValue))
                        {
                            _logger.LogWarning(
                                "[{Tag}] No valid mapped value found for option: {OptionValue} in attribute: {AttributeCode}",
                                Tag, optionValue, attributeCode);
                            continue;
                        }

                        option = attribute.Settings?.Options?
                            .FirstOrDefault(x => string.Equals(x.Value, mappedOptionValue, StringComparison.Ordinal));

                        if (option is null)
                        {
                            _logger.LogWarning(
                                "[{Tag}] Mapped option value not found in options: {MappedValue} for attribute: {AttributeCode}",
                                Tag, mappedOptionValue, attributeCode);
                            continue;
                        }

                        validOptionValues.Add(option.Value);
                        _logger.LogInformation(
                            "[{Tag}] Mapped option applied: {OriginalValue} => {MappedValue} in attribute: {AttributeCode}",
                            Tag, optionValue, option.Value, attributeCode);
                    }

                    if (validOptionValues.Count == 0)
                    {
                        _logger.LogWarning(
                            "[{Tag}] No valid options found after repair for attribute: {AttributeCode}",
                            Tag, attributeCode);
                        continue;
                    }

                    result.Value = validOptionValues;
                    _logger.LogInformation(
                        "[{Tag}] Valid values updated for attribute: {AttributeCode}: {Values}",
                        Tag, attributeCode, string.Join(", ", validOptionValues));
                    break;
                }
            }
        }
    }

    private AttributesPopulationInput CreateAttributesPopulationInput(
        AiProcessorPopulateAttributesRequest request,
        AttributesPopulationContext context)
    {
        var promptAttributes = new List<PromptAttribute>();

        var optimizedSelectableAttributeCodes = new List<string>();
        foreach (AiProcessorPopulateAttributesRequest.Attribute attribute in request.Attributes)
        {
            var promptAttribute = new PromptAttribute
            {
                Id = attribute.Id,
                Code = attribute.Code,
                Label = attribute.Label,
                Description = attribute.Description,
                ValueType = GetPromptAttributeValueType(attribute.ValueType ?? AttributeValueTypeEnum.Undefined),
                Settings = IsAttributeSettingsEmpty(attribute.Settings)
                    ? null
                    : new PromptAttributeSettings
                    {
                        Minimum = attribute.Settings!.Minimum,
                        Maximum = attribute.Settings.Maximum,
                        AllowNegative = attribute.Settings.AllowNegative,
                        FractionDigits = attribute.Settings.FractionDigits,
                        ValidationRule = attribute.Settings.ValidationRule,
                        AllowHtml = attribute.Settings.AllowHtml,
                        MinimumDate = attribute.Settings.MinimumDate,
                        MaximumDate = attribute.Settings.MaximumDate,
                        Options = attribute.Settings.Options
                    }
            };

            if (attribute.ValueType != AttributeValueTypeEnum.Select &&
                attribute.ValueType != AttributeValueTypeEnum.MultiSelect)
            {
                promptAttributes.Add(promptAttribute);
                continue;
            }

            if (attribute.Settings?.Options is null || attribute.Settings.Options.Count <= MaxOptionsCountPerAttribute)
            {
                promptAttributes.Add(promptAttribute);
                continue;
            }

            _logger.LogWarning(
                "[{Tag}] Attribute '{AttributeCode}' has too many options ({OptionsCount}).",
                Tag, attribute.Code, attribute.Settings.Options.Count);

            promptAttribute.Settings = new PromptAttributeSettings
            {
                OptionExamples = (UseRandomOptionExamples
                        ? GetRandomExamples(attribute.Settings?.Options!)
                        : GetExamples(attribute.Settings?.Options!))
                    .Select(x => x.Value)
                    .ToList()
            };

            promptAttributes.Add(promptAttribute);
            optimizedSelectableAttributeCodes.Add(attribute.Code);
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            if (optimizedSelectableAttributeCodes.Count == 0)
            {
                _logger.LogDebug(
                    "[{Tag}] Attributes: {AttributesJson}.",
                    Tag, JsonSerializer.Serialize(request.Attributes, options: JsonSettingsExtensions.Default));
            }
            else
            {
                _logger.LogDebug(
                    "[{Tag}] Original attributes: {OriginalAttributesJson}.",
                    Tag, JsonSerializer.Serialize(request.Attributes, options: JsonSettingsExtensions.Default));
                _logger.LogDebug(
                    "[{Tag}] Updated attributes: {UpdatedAttributesJson}.",
                    Tag, JsonSerializer.Serialize(promptAttributes, options: JsonSettingsExtensions.Default));
            }
        }

        string systemPrompt = RenderAttributesPopulationPrompt(
            request.Language,
            request.Label,
            promptAttributes,
            context.Settings.SetupRequest);
        string userPrompt = RenderAttributesPopulationPrompt(
            request.Language,
            request.Label,
            promptAttributes,
            context.Settings.Prompt);

        AttributesPopulationConfig config = context.Config;

        return new AttributesPopulationInput
        {
            Request = request,
            TextGenerationInput = new AiTextGenerationInput
            {
                MaxOutputTokenCount = config.MaxOutputTokenCount,
                SystemPrompt = systemPrompt,
                UserPrompt = userPrompt,
                WebSearchEnabled = true,
                OutputTextFormat =
                    new AiTextGenerationInput.JsonSchemaFormat { Schema = _outputJsonSchemaFactory.Value }
            }
        };
    }

    private string RenderAttributesPopulationPrompt(
        string language,
        string label,
        IEnumerable<PromptAttribute> attributes,
        string template)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(attributes);
        ArgumentException.ThrowIfNullOrWhiteSpace(template);

        string attributesJson = JsonSerializer.Serialize(
            attributes,
            options: JsonSettingsExtensions.Default);
        var ctx = new AttributesPopulationRenderContext
        {
            Language = language,
            Label = label,
            AttributesJson = attributesJson
        };

        IParsedTemplate? parsedTemplate = _templateEngine.ParseTemplate(template);
        string result = parsedTemplate!.Render(ctx);

        return result;
    }

    private AsyncRetryPolicy CreateBatchProcessingRetryPolicy()
    {
        // 2 + 4 + 8 + 16 + 32 = 62
        AsyncRetryPolicy? retryPolicy = Policy
            .Handle<Exception>()
            .WaitAndRetryAsync(
                retryCount: BatchProcessingRetryCount,
                sleepDurationProvider: (retryAttempt, _, _) => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetryAsync: (outcome, timespan, retryAttempt, _) =>
                {
                    _logger.LogWarning(
                        "[{Tag}] Retry {RetryAttempt} due to {ExceptionType}: {ExceptionMessage}. Waiting {Delay} before next attempt.",
                        Tag,
                        retryAttempt,
                        outcome?.GetType().Name ?? "UnknownException",
                        outcome?.Message ?? "No message",
                        timespan);

                    return Task.CompletedTask;
                });

        return retryPolicy;
    }

    private AiProcessorPopulateAttributesResponse CreatePopulateAttributesResponse(
        ProcessedAttributesPopulationOutput output,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> optionsMappingResults)
    {
        IReadOnlyCollection<AttributePopulationResult> results = output.Response.Results;

        IReadOnlyCollection<AiProcessorPopulateAttributesRequest.Attribute> attributes =
            output.Input.Request.Attributes;

        IReadOnlyCollection<SearchResult>? validSearchResults = null;

        if (optionsMappingResults.Count != 0 && output.UnrecognizedResults.Count != 0)
        {
            List<string> selectableAttributeCodesToRepair = output.UnrecognizedResults
                .Select(x => x.Code)
                .ToList();
            TryRepairResults(results, attributes, selectableAttributeCodesToRepair, optionsMappingResults);
        }

        var populatedAttributes = new List<AiProcessorPopulateAttributesResponse.PopulatedAttribute>();
        var unpopulatedAttributeCodes = new List<string>();

        foreach (AiProcessorPopulateAttributesRequest.Attribute attribute in attributes)
        {
            AttributePopulationResult? result = results.FirstOrDefault(x => x.Code == attribute.Code);

            if (result is null)
            {
                _logger.LogWarning("[{Tag}] Response contained unknown attribute code '{AttributeCode}'. Skipping.",
                    Tag, attribute.Code);
                unpopulatedAttributeCodes.Add(attribute.Code);
                continue;
            }

            object value;

            switch (attribute.ValueType)
            {
                case AttributeValueTypeEnum.Bool:
                {
                    if (!bool.TryParse(result.Value.ToString(), out bool boolValue))
                    {
                        _logger.LogWarning(
                            "[{Tag}] Value for bool attribute '{AttributeCode}' is empty. Skipping value.",
                            Tag, attribute.Code);
                        unpopulatedAttributeCodes.Add(attribute.Code);
                        continue;
                    }

                    value = boolValue;
                    break;
                }
                case AttributeValueTypeEnum.IntegerNumber:
                {
                    if (!long.TryParse(result.Value.ToString(), out long integerNumberValue))
                    {
                        _logger.LogWarning(
                            "[{Tag}] Value for integer number attribute '{AttributeCode}' is empty. Skipping value.",
                            Tag, attribute.Code);
                        unpopulatedAttributeCodes.Add(attribute.Code);
                        continue;
                    }

                    value = integerNumberValue;
                    break;
                }
                case AttributeValueTypeEnum.RealNumber:
                {
                    if (!double.TryParse(result.Value.ToString(), out double realNumberValue))
                    {
                        _logger.LogWarning(
                            "[{Tag}] Value for real number attribute '{AttributeCode}' is empty. Skipping value.",
                            Tag, attribute.Code);
                        unpopulatedAttributeCodes.Add(attribute.Code);
                        continue;
                    }

                    value = realNumberValue;
                    break;
                }
                case AttributeValueTypeEnum.Text:
                {
                    var textValue = result.Value.ToString();
                    if (string.IsNullOrWhiteSpace(textValue))
                    {
                        _logger.LogWarning(
                            "[{Tag}] Value for text attribute '{AttributeCode}' is empty. Skipping value.",
                            Tag, attribute.Code);
                        unpopulatedAttributeCodes.Add(attribute.Code);
                        continue;
                    }

                    value = textValue;
                    break;
                }
                case AttributeValueTypeEnum.RichText:
                {
                    var richTextValue = result.Value.ToString();
                    if (string.IsNullOrWhiteSpace(richTextValue))
                    {
                        _logger.LogWarning(
                            "[{Tag}] Value for rich text attribute '{AttributeCode}' is empty. Skipping value.",
                            Tag, attribute.Code);
                        unpopulatedAttributeCodes.Add(attribute.Code);
                        continue;
                    }

                    value = richTextValue;
                    break;
                }
                case AttributeValueTypeEnum.Date:
                {
                    var dateValue = result.Value.ToString();

                    if (string.IsNullOrWhiteSpace(dateValue))
                    {
                        _logger.LogWarning(
                            "[{Tag}] Value for date attribute '{AttributeCode}' is empty. Skipping value.",
                            Tag, attribute.Code);
                        unpopulatedAttributeCodes.Add(attribute.Code);
                        continue;
                    }

                    if (!IsValidDate(dateValue))
                    {
                        _logger.LogWarning(
                            "[{Tag}] Value for date attribute '{AttributeCode}' has not valid date format: '{Value}'. Skipping value.",
                            Tag, attribute.Code, dateValue);
                        unpopulatedAttributeCodes.Add(attribute.Code);
                        continue;
                    }

                    value = dateValue;
                    break;
                }
                case AttributeValueTypeEnum.DateRange:
                {
                    if (result.Value is not DateRange dateRangeValue)
                    {
                        _logger.LogWarning(
                            "[{Tag}] Value for date range attribute '{AttributeCode}' is empty. Skipping value.",
                            Tag, attribute.Code);
                        unpopulatedAttributeCodes.Add(attribute.Code);
                        continue;
                    }

                    if (!IsValidDate(dateRangeValue.From))
                    {
                        _logger.LogWarning(
                            "[{Tag}] Property 'From' for date range attribute '{AttributeCode}' has not valid date format: '{From}'. Skipping value.",
                            Tag, attribute.Code, dateRangeValue.From);
                        unpopulatedAttributeCodes.Add(attribute.Code);
                        continue;
                    }

                    if (!IsValidDate(dateRangeValue.To))
                    {
                        _logger.LogWarning(
                            "[{Tag}] Property 'To' for date range attribute '{AttributeCode}' has not valid date format: '{To}'. Skipping value.",
                            Tag, attribute.Code, dateRangeValue.To);
                        unpopulatedAttributeCodes.Add(attribute.Code);
                        continue;
                    }

                    value = dateRangeValue;
                    break;
                }
                case AttributeValueTypeEnum.Select:
                {
                    var item = result.Value.ToString();
                    if (string.IsNullOrWhiteSpace(item))
                    {
                        _logger.LogWarning(
                            "[{Tag}] Value for select attribute '{AttributeCode}' is empty. Skipping value.",
                            Tag, attribute.Code);
                        unpopulatedAttributeCodes.Add(attribute.Code);
                        continue;
                    }

                    AiProcessorPopulateAttributesRequest.AttributeOption? option =
                        attribute.Settings?.Options?.FirstOrDefault(x => x.Value == item);
                    if (option is null)
                    {
                        _logger.LogWarning(
                            "[{Tag}] GPT returned unknown value '{OptionValue}' for select attribute '{AttributeCode}'. Skipping value.",
                            Tag, item, attribute.Code);
                        unpopulatedAttributeCodes.Add(attribute.Code);
                        continue;
                    }

                    value = option.Code;
                    break;
                }
                case AttributeValueTypeEnum.MultiSelect:
                {
                    if (result.Value is not List<string> items || items.Count == 0)
                    {
                        _logger.LogWarning(
                            "[{Tag}] Value for multi-select attribute '{AttributeCode}' is empty. Skipping value.",
                            Tag, attribute.Code);
                        unpopulatedAttributeCodes.Add(attribute.Code);
                        continue;
                    }

                    var optionCodes = new List<string>();
                    foreach (string item in items)
                    {
                        AiProcessorPopulateAttributesRequest.AttributeOption? option =
                            attribute.Settings?.Options?.FirstOrDefault(x => x.Value == item);
                        if (option is null)
                        {
                            _logger.LogWarning(
                                "[{Tag}] GPT returned unknown value '{OptionValue}' for multi-select attribute '{AttributeCode}'. Skipping value.",
                                Tag, item, attribute.Code);
                            continue;
                        }

                        optionCodes.Add(option.Code);
                    }

                    if (optionCodes.Count == 0)
                    {
                        _logger.LogWarning(
                            "[{Tag}] No valid options found for multi-select attribute '{AttributeCode}",
                            Tag, attribute.Code);
                        unpopulatedAttributeCodes.Add(attribute.Code);
                        continue;
                    }

                    value = optionCodes;
                    break;
                }
                case AttributeValueTypeEnum.StringArray:
                {
                    if (result.Value is not List<string> items || items.Count == 0)
                    {
                        _logger.LogWarning(
                            "[{Tag}] Value for string array attribute '{AttributeCode}' is empty. Skipping value.",
                            Tag, attribute.Code);
                        unpopulatedAttributeCodes.Add(attribute.Code);
                        continue;
                    }

                    items = items
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .SelectMany(x => x.Split(',', StringSplitOptions.RemoveEmptyEntries))
                        .Select(x => x.Trim())
                        .Distinct()
                        .ToList();

                    if (items.Count == 0)
                    {
                        _logger.LogWarning(
                            "[{Tag}] Value for string array attribute '{AttributeCode}' does not contain valid elements. Skipping value.",
                            Tag, attribute.Code);
                        unpopulatedAttributeCodes.Add(attribute.Code);
                        continue;
                    }

                    value = items;
                    break;
                }
                default:
                    throw new Exception($"Unsupported value type: {attribute.ValueType}");
            }

            // just in case

            if (result.Confidence < 0)
            {
                _logger.LogWarning(
                    "[{Tag}] Confidence value for attribute {Code} was below 0. Resetting to 0.",
                    Tag, result.Code);
                result.Confidence = 0;
            }

            if (result.Confidence > 1)
            {
                _logger.LogWarning(
                    "[{Tag}] Confidence value for attribute {Code} exceeded 1. Resetting to 1.",
                    Tag, result.Code);
                result.Confidence = 1;
            }

            validSearchResults ??= GetValidSearchResults(output.Response.SearchResults, attributes);

            string reason = result.Reason;
            List<string> sourceUrls = validSearchResults
                .Where(x => x.Codes.Contains(attribute.Code))
                .SelectMany(x => x.Sources)
                .Distinct()
                .ToList();

            if (AppendSourcesToReason && sourceUrls.Count != 0)
            {
                var sb = new StringBuilder(reason.Trim())
                    .AppendLine()
                    .AppendLine();

                foreach (string sourceUrl in sourceUrls)
                {
                    sb.AppendLine(sourceUrl);
                    sb.AppendLine();
                }

                reason = sb.ToString().Trim();
            }

            populatedAttributes.Add(new AiProcessorPopulateAttributesResponse.PopulatedAttribute
            {
                Id = attribute.Id,
                Code = attribute.Code,
                Value = value,
                Confidence = result.Confidence,
                Reason = reason,
                SourceUrls = sourceUrls
            });
        }

        return new AiProcessorPopulateAttributesResponse(populatedAttributes, unpopulatedAttributeCodes);
    }

    private IReadOnlyCollection<SearchResult> GetValidSearchResults(
        IEnumerable<SearchResult> searchResults,
        IReadOnlyCollection<AiProcessorPopulateAttributesRequest.Attribute> attributes)
    {
        var validSearchResults = new List<SearchResult>();

        foreach (SearchResult searchResult in searchResults)
        {
            var validCodes = new List<string>();
            var validSources = new List<string>();

            foreach (string code in searchResult.Codes)
            {
                if (string.IsNullOrWhiteSpace(code))
                {
                    _logger.LogWarning("[{Tag}] Empty attribute code.", Tag);
                    continue;
                }

                if (attributes.All(x => x.Code != code))
                {
                    _logger.LogWarning("[{Tag}] Unknown attribute code: {Code}.", Tag, code);
                    continue;
                }

                if (validCodes.Contains(code))
                {
                    _logger.LogWarning("[{Tag}] Duplicate attribute code: {Code}", Tag, code);
                    continue;
                }

                validCodes.Add(code);
            }

            if (validCodes.Count == 0)
            {
                continue;
            }

            foreach (string sourceUrl in searchResult.Sources)
            {
                if (!IsValidSourceUrl(sourceUrl))
                {
                    _logger.LogWarning(
                        "[{Tag}] Invalid source URL: {SourceUrl}, codes: {Codes}",
                        Tag,
                        sourceUrl,
                        string.Join(',', validCodes));
                    continue;
                }

                if (validSources.Contains(sourceUrl))
                {
                    _logger.LogWarning(
                        "[{Tag}] Duplicate source URL: {SourceUrl}, codes: {Codes}",
                        Tag,
                        sourceUrl,
                        string.Join(',', validCodes));
                    continue;
                }

                validSources.Add(sourceUrl);
            }

            if (validSources.Count == 0)
            {
                continue;
            }

            var validSearchResult = new SearchResult
            {
                Codes = validCodes,
                Sources = validSources
            };

            validSearchResults.Add(validSearchResult);
        }

        return validSearchResults;
    }

    private static AttributesPopulationContext CreateContext(
        AttributesPopulationConfig config,
        AttributesPopulationSettings settings,
        IEnumerable<AiProcessorPopulateAttributesRequest> requests)
    {
        var optionsPool = new Dictionary<string, List<AiProcessorPopulateAttributesRequest.AttributeOption>>();

        foreach (AiProcessorPopulateAttributesRequest request in requests)
        {
            foreach (AiProcessorPopulateAttributesRequest.Attribute attribute in request.Attributes)
            {
                if (attribute.ValueType != AttributeValueTypeEnum.Select &&
                    attribute.ValueType != AttributeValueTypeEnum.MultiSelect)
                {
                    continue;
                }

                if (attribute.Settings?.Options is null || attribute.Settings.Options.Count == 0)
                {
                    continue;
                }

                optionsPool.TryAdd(attribute.Code, attribute.Settings.Options);
            }
        }

        return new AttributesPopulationContext
        {
            Config = config,
            Settings = settings,
            OptionsPool = optionsPool
        };
    }

    private static List<UnrecognizedSelectableAttributePopulationResult>
        GetUnrecognizedSelectableAttributePopulationResult(
            IEnumerable<AttributePopulationResult> results,
            IReadOnlyCollection<AiProcessorPopulateAttributesRequest.Attribute> attributes)
    {
        var unrecognizedResults = new List<UnrecognizedSelectableAttributePopulationResult>();

        foreach (AttributePopulationResult result in results)
        {
            string attributeCode = result.Code;

            AiProcessorPopulateAttributesRequest.Attribute? attribute =
                attributes.FirstOrDefault(x => x.Code == attributeCode);

            if (attribute is null)
                continue;

            switch (attribute.ValueType)
            {
                case AttributeValueTypeEnum.Select:
                {
                    var optionValue = result.Value.ToString();

                    if (string.IsNullOrWhiteSpace(optionValue))
                        continue;

                    AiProcessorPopulateAttributesRequest.AttributeOption? option = attribute.Settings?.Options?
                        .FirstOrDefault(x => string.Equals(x.Value, optionValue, StringComparison.Ordinal));

                    if (option is not null)
                        continue;

                    option = attribute.Settings?.Options?
                        .FirstOrDefault(x => string.Equals(x.Value, optionValue, StringComparison.OrdinalIgnoreCase));

                    if (option is null)
                    {
                        unrecognizedResults.Add(
                            new UnrecognizedSelectableAttributePopulationResult
                            {
                                Code = attribute.Code,
                                Label = attribute.Label,
                                Context = result.Reason,
                                Values = [optionValue],
                                Options = attribute.Settings!.Options!
                                    .Select(x => x.Value)
                                    .Distinct()
                                    .ToList(),
                                SingleSelectionOnly = true
                            });
                    }
                    else
                    {
                        result.Value = option.Value;
                    }

                    break;
                }
                case AttributeValueTypeEnum.MultiSelect:
                {
                    var optionValues = (result.Value as List<string>)?.Distinct().ToList();

                    if (optionValues is null || optionValues.Count == 0)
                        continue;

                    var validOptionValues = new List<string>();
                    var unrecognizedOptionValues = new List<string>();

                    foreach (string optionValue in optionValues)
                    {
                        if (string.IsNullOrWhiteSpace(optionValue))
                            continue;

                        AiProcessorPopulateAttributesRequest.AttributeOption? option = attribute.Settings?.Options?
                            .FirstOrDefault(x => string.Equals(x.Value, optionValue, StringComparison.Ordinal));

                        if (option is not null)
                        {
                            validOptionValues.Add(optionValue);
                            continue;
                        }

                        option = attribute.Settings?.Options?
                            .FirstOrDefault(x =>
                                string.Equals(x.Value, optionValue, StringComparison.OrdinalIgnoreCase));

                        if (option is null)
                        {
                            unrecognizedOptionValues.Add(optionValue);
                        }
                        else
                        {
                            validOptionValues.Add(option.Value);
                        }
                    }

                    if (unrecognizedOptionValues.Count != 0)
                    {
                        unrecognizedResults.Add(
                            new UnrecognizedSelectableAttributePopulationResult
                            {
                                Code = attribute.Code,
                                Label = attribute.Label,
                                Context = result.Reason,
                                Values = unrecognizedOptionValues,
                                Options = attribute.Settings!.Options!
                                    .Select(x => x.Value)
                                    .Distinct()
                                    .ToList(),
                                SingleSelectionOnly = false
                            });
                        continue;
                    }

                    if (validOptionValues.Count != 0)
                    {
                        result.Value = validOptionValues;
                    }

                    break;
                }
            }
        }

        return unrecognizedResults;
    }
}
