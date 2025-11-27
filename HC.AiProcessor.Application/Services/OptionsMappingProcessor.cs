using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using HC.AiProcessor.Application.Common;
using HC.AiProcessor.Application.Models;
using HC.Packages.AiProcessor.V1.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NJsonSchema;
using NJsonSchema.Generation;
using Polly;
using Polly.Retry;

namespace HC.AiProcessor.Application.Services;

public interface IOptionsMappingProcessor
{
    Task<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> ProcessAsync(
        IReadOnlyCollection<OptionsMappingInput> inputs,
        ChatCompletionConfig config,
        CancellationToken cancellationToken = default);
}

[DebuggerDisplay("Code: {Code}, Label: {Label}")]
public sealed record OptionsMappingInput
{
    public required string Code { get; init; }
    public required string Label { get; init; }
    public required IReadOnlyCollection<string> Options { get; init; }
    public required IList<string> Values { get; init; }
}

internal record OptionsMappingProcessingConfig
{
    public int MaxOptionsCountPerRequest { get; set; } = 100;
}

// TODO: temporary
internal delegate Task<OptionsMappingSettings> GetOptionsMappingSettingsAsync(
    CancellationToken cancellationToken = default);

internal abstract class OptionsMappingProcessorBase<TProcessingConfig>(
    IOptions<TProcessingConfig> optionsAccessor,
    GetOptionsMappingSettingsAsync getOptionsMappingSettingsAsync,
    ITemplateEngine templateEngine,
    ILogger<OptionsMappingProcessorBase<TProcessingConfig>> logger)
    where TProcessingConfig : OptionsMappingProcessingConfig
{
    private const bool ExcludeSchemaUriFromOutputSchema = true;
    private readonly Lazy<string> _outputJsonSchemaFactory = new(CreateOutputJsonSchema);

    private readonly GetOptionsMappingSettingsAsync _getOptionsMappingSettingsAsync = getOptionsMappingSettingsAsync ??
        throw new ArgumentNullException(nameof(getOptionsMappingSettingsAsync));

    private readonly ITemplateEngine _templateEngine =
        templateEngine ?? throw new ArgumentNullException(nameof(templateEngine));

    protected readonly TProcessingConfig ProcessingConfig = optionsAccessor.Value;

    protected readonly ILogger<OptionsMappingProcessorBase<TProcessingConfig>> Logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    protected abstract string Tag { get; }

    public async Task<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> ProcessAsync(
        IReadOnlyCollection<OptionsMappingInput> inputs,
        ChatCompletionConfig config,
        CancellationToken cancellationToken = default)
    {
        OptionsMappingSettings settings = await _getOptionsMappingSettingsAsync(cancellationToken);

        IEnumerable<AiTextGenerationInput> textGenerationInputs =
            GetOptionsMappingTextGenerationInputs(inputs, settings);

        AiProcessorBatchResponse<AiTextGenerationOutput> batchResponse =
            await ProcessTextGenerationInputs(textGenerationInputs, config, cancellationToken);

        if (batchResponse.Error is not null)
        {
            return new Dictionary<string, IReadOnlyDictionary<string, string>>();
        }

        var outputs = new List<OptionsMappingOutput>();

        foreach (AiProcessorBatchOutput<AiTextGenerationOutput> batchRequestOutput in batchResponse.Outputs!)
        {
            if (batchRequestOutput.Body is null)
                continue;

            try
            {
                var rawResponse = AiAgentResponseHelper
                    .ToObjectFromJson<OptionsMappingResponse>(batchRequestOutput.Body.Content);

                if (rawResponse.Results.Count == 0)
                    continue;

                var results = new Dictionary<string, IReadOnlyDictionary<string, string>>();

                foreach (OptionsMappingInput input in inputs)
                {
                    List<OptionsMappingResult> rawResults = rawResponse.Results
                        .Where(x => x.Code == input.Code)
                        .ToList();

                    var mappedValues = new Dictionary<string, string>();

                    foreach (var rawResult in rawResults)
                    {
                        if (input.Values.All(v => v != rawResult.Value))
                            continue;

                        if (!input.Options.Contains(rawResult.Option))
                            continue;

                        mappedValues[rawResult.Value] = rawResult.Option;
                    }

                    if (mappedValues.Count == 0)
                        continue;

                    results[input.Code] = mappedValues;
                }

                outputs.Add(new OptionsMappingOutput { Results = results });
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[{Tag}] Failed to parse GPT response {TypeName}.",
                    Tag,
                    nameof(Dictionary<string, Dictionary<string, string>>));
            }
        }

        Dictionary<string, IReadOnlyDictionary<string, string>> aggregatedResults = outputs
            .Aggregate(
                seed: new Dictionary<string, IReadOnlyDictionary<string, string>>(),
                (current, optionsMappingOutput) =>
                    current
                        .Concat(optionsMappingOutput.Results)
                        .ToDictionary(kvp => kvp.Key, kvp => kvp.Value));

        return aggregatedResults;
    }

    protected abstract Task<AiProcessorBatchResponse<AiTextGenerationOutput>> ProcessTextGenerationInputs(
        IEnumerable<AiTextGenerationInput> inputs,
        ChatCompletionConfig config,
        CancellationToken cancellationToken = default);

    private IEnumerable<AiTextGenerationInput> GetOptionsMappingTextGenerationInputs(
        IReadOnlyCollection<OptionsMappingInput> optionsMappingInputs,
        OptionsMappingSettings settings)
    {
        string systemPromptTemplate = settings.SystemPrompt;
        string userPromptTemplate = settings.UserPrompt;

        List<List<OptionsMappingInput>> inputsChunks =
            SplitOptionsMappingInputs(optionsMappingInputs, ProcessingConfig.MaxOptionsCountPerRequest);

        foreach (List<OptionsMappingInput> chunk in inputsChunks)
        {
            string optionsMappingSystemPrompt = RenderOptionsMappingPrompt(
                chunk,
                systemPromptTemplate);
            string optionsMappingUserPrompt = RenderOptionsMappingPrompt(
                chunk,
                userPromptTemplate);

            yield return new AiTextGenerationInput
            {
                SystemPrompt = optionsMappingSystemPrompt,
                UserPrompt = optionsMappingUserPrompt,
                Temperature = 0,
                OutputTextFormat =
                    new AiTextGenerationInput.JsonSchemaFormat { Schema = _outputJsonSchemaFactory.Value }
            };
        }
    }

    private static List<List<OptionsMappingInput>> SplitOptionsMappingInputs(
        IReadOnlyCollection<OptionsMappingInput> inputs,
        int maxOptionsPerChunk)
    {
        // 1. Separate records with options count greater than or equal to the limit
        List<List<OptionsMappingInput>> oversizedChunks = inputs
            .Where(x => x.Options.Count >= maxOptionsPerChunk)
            .Select(x => new List<OptionsMappingInput> { x })
            .ToList();

        // 2. Records eligible for tight packing
        List<OptionsMappingInput> toPack = inputs
            .Where(x => x.Options.Count < maxOptionsPerChunk)
            .OrderByDescending(x => x.Options.Count) // pack large first
            .ToList();

        var packedChunks = new List<List<OptionsMappingInput>>();
        var chunkSums = new List<int>();

        // 3. First-fit decreasing bin packing
        foreach (OptionsMappingInput record in toPack)
        {
            var placed = false;
            for (var i = 0; i < packedChunks.Count; i++)
            {
                if (chunkSums[i] + record.Options.Count > maxOptionsPerChunk)
                    continue;

                packedChunks[i].Add(record);
                chunkSums[i] += record.Options.Count;
                placed = true;
                break;
            }

            if (placed)
                continue;

            packedChunks.Add([record]);
            chunkSums.Add(record.Options.Count);
        }

        // 4. Combine all chunks for the result
        var result = new List<List<OptionsMappingInput>>();
        result.AddRange(oversizedChunks);
        result.AddRange(packedChunks);

        return result;
    }

    private string RenderOptionsMappingPrompt(IReadOnlyCollection<OptionsMappingInput> inputs, string template)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentException.ThrowIfNullOrWhiteSpace(template);

        var ctx = new
        {
            InputsJson = JsonSerializer.Serialize(inputs, options: JsonSettingsExtensions.Default)
        };

        IParsedTemplate? parsedTemplate = _templateEngine.ParseTemplate(template);
        string result = parsedTemplate!.Render(ctx);

        return result;
    }

    private static string CreateOutputJsonSchema()
    {
        var settings = new SystemTextJsonSchemaGeneratorSettings
        {
            SchemaNameGenerator = new CustomSchemaNameGenerator()
        };
        var generator = new JsonSchemaGenerator(settings);

        JsonSchema schema = generator.Generate(typeof(OptionsMappingResponse));

        string jsonSchema = schema.ToJson(Newtonsoft.Json.Formatting.None);

        if (ExcludeSchemaUriFromOutputSchema)
        {
            var tempJsonSchemaNode = (JsonObject) JsonNode.Parse(jsonSchema)!;
            if (tempJsonSchemaNode.Remove("$schema"))
            {
                jsonSchema = JsonSerializer.Serialize(tempJsonSchemaNode);
            }
        }

        return jsonSchema;
    }

    private sealed class OptionsMappingResponse
    {
        public const string SchemaName = "Response";
        public const string ResultsJsonPropertyName = "results";

        [Required]
        [Description(
            "The list of mapping results, each representing a raw value mapped to an allowed option for a specific attribute.")]
        [JsonPropertyName(ResultsJsonPropertyName)]
        public required IReadOnlyCollection<OptionsMappingResult> Results { get; set; }
    }

    [DebuggerDisplay("Code: {Code}, Value: {Value}, Option: {Option}")]
    private sealed class OptionsMappingResult
    {
        public const string SchemaName = "Result";
        public const string CodeJsonPropertyName = "code";
        public const string ValueJsonPropertyName = "value";
        public const string OptionJsonPropertyName = "option";
        public const string ConfidenceJsonPropertyName = "confidence";
        public const string ReasonJsonPropertyName = "reason";

        [Required]
        [Description("The attribute code this mapping applies to.")]
        [JsonPropertyName(CodeJsonPropertyName)]
        public required string Code { get; set; }

        [Required]
        [Description("The raw input value that was mapped.")]
        [JsonPropertyName(ValueJsonPropertyName)]
        public required string Value { get; set; }

        [Required]
        [Description("The best-matching allowed option selected for the raw value.")]
        [JsonPropertyName(OptionJsonPropertyName)]
        public required string Option { get; set; }

#if DEBUG
        [Required]
        [Range(0.0, 1.0)]
        [Description(
            "A confidence score between 0.0 and 1.0 indicating the model's certainty in the correctness of the mapped option.")]
        [JsonPropertyName(ConfidenceJsonPropertyName)]
        public required float Confidence { get; set; }

        [Required]
        [Description(
            "A brief explanation of the reasoning behind the chosen value, providing insight into how the model interpreted the input.")]
        [JsonPropertyName(ReasonJsonPropertyName)]
        public required string Reason { get; set; }
#endif
    }

    private sealed class CustomSchemaNameGenerator : ISchemaNameGenerator
    {
        public string Generate(Type type)
        {
            if (type == typeof(OptionsMappingResponse))
                return OptionsMappingResponse.SchemaName;
            if (type == typeof(OptionsMappingResult))
                return OptionsMappingResult.SchemaName;
            return type.Name;
        }
    }

    private sealed class OptionsMappingOutput
    {
        public required IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Results { get; init; }
    }
}

internal record SyncOptionsMappingProcessingConfig : OptionsMappingProcessingConfig
{
    public int MaxParallelRequests { get; set; } = 25;
    public int RequestTimeoutInMilliseconds { get; set; } = 10000;
    public int RetryCount { get; set; } = 2;
    public int DelayBeforeRetryInMilliseconds { get; set; } = 0;
}

internal sealed class SyncOptionsMappingProcessor(
    IOptions<SyncOptionsMappingProcessingConfig> optionsAccessor,
    GetOptionsMappingSettingsAsync getOptionsMappingSettingsAsync,
    ITemplateEngine templateEngine,
    ICpuBoundBatchProcessor cpuBoundBatchProcessor,
    IAiTextGenerationInputProcessor aiTextGenerationInputProcessor,
    ILogger<SyncOptionsMappingProcessor> logger) :
    OptionsMappingProcessorBase<SyncOptionsMappingProcessingConfig>(
        optionsAccessor,
        getOptionsMappingSettingsAsync,
        templateEngine,
        logger), IOptionsMappingProcessor
{
    private readonly ICpuBoundBatchProcessor _cpuBoundBatchProcessor =
        cpuBoundBatchProcessor ?? throw new ArgumentNullException(nameof(cpuBoundBatchProcessor));

    private readonly IAiTextGenerationInputProcessor _aiTextGenerationInputProcessor =
        aiTextGenerationInputProcessor ?? throw new ArgumentNullException(nameof(aiTextGenerationInputProcessor));

    protected override string Tag => "SYNC_OPTIONS_MAPPING_PROCESSOR";

    protected override async Task<AiProcessorBatchResponse<AiTextGenerationOutput>> ProcessTextGenerationInputs(
        IEnumerable<AiTextGenerationInput> inputs,
        ChatCompletionConfig config,
        CancellationToken cancellationToken = default)
    {
        AsyncRetryPolicy retryPolicy = CreateRetryPolicy();
        var requestTimeout = TimeSpan.FromMilliseconds(ProcessingConfig.RequestTimeoutInMilliseconds);

        AiProcessorBatchResponse<AiTextGenerationOutput> batchResponse = await _cpuBoundBatchProcessor.ProcessAsync(
            inputs.Select((x, i) => new AiProcessorBatchInput<AiTextGenerationInput> { Id = i.ToString(), Body = x }),
            process: async input =>
            {
                AiTextGenerationOutput textGenerationOutput =
                    await retryPolicy.ExecuteAsync(async () =>
                    {
                        using var timeoutCts = new CancellationTokenSource(requestTimeout);
                        using var linkedCts =
                            CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);

                        AiTextGenerationOutput result =
                            await _aiTextGenerationInputProcessor.ProcessAsync(input.Body, config, linkedCts.Token);

                        return result;
                    });

                return new AiProcessorBatchOutput<AiTextGenerationOutput>
                {
                    Id = input.Id,
                    Body = textGenerationOutput
                };
            },
            processingOptions: new CpuBoundBatchProcessingOptions
            {
                MaxDegreeOfParallelism = ProcessingConfig.MaxParallelRequests
            },
            cancellationToken);

        return batchResponse;
    }

    private AsyncRetryPolicy CreateRetryPolicy()
    {
        AsyncRetryPolicy? retryPolicy = Policy
            .Handle<Exception>()
            .WaitAndRetryAsync(
                retryCount: ProcessingConfig.RetryCount,
                sleepDurationProvider: (_, _, _) =>
                    TimeSpan.FromMilliseconds(ProcessingConfig.DelayBeforeRetryInMilliseconds),
                onRetryAsync: (outcome, timespan, retryAttempt, _) =>
                {
                    if (ProcessingConfig.DelayBeforeRetryInMilliseconds == 0)
                    {
                        return Task.CompletedTask;
                    }

                    Logger.LogWarning(
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
}

internal sealed class AsyncOptionsMappingProcessor(
    IOptions<OptionsMappingProcessingConfig> optionsAccessor,
    GetOptionsMappingSettingsAsync getOptionsMappingSettingsAsync,
    ITemplateEngine templateEngine,
    IAiTextGenerationInputBatchProcessor textGenerationBatchProcessor,
    ILogger<AsyncOptionsMappingProcessor> logger) :
    OptionsMappingProcessorBase<OptionsMappingProcessingConfig>(
        optionsAccessor,
        getOptionsMappingSettingsAsync,
        templateEngine,
        logger), IOptionsMappingProcessor
{
    private readonly IAiTextGenerationInputBatchProcessor _textGenerationBatchProcessor =
        textGenerationBatchProcessor ?? throw new ArgumentNullException(nameof(textGenerationBatchProcessor));

    protected override string Tag => "ASYNC_OPTIONS_MAPPING_PROCESSOR";

    protected override async Task<AiProcessorBatchResponse<AiTextGenerationOutput>> ProcessTextGenerationInputs(
        IEnumerable<AiTextGenerationInput> inputs,
        ChatCompletionConfig config,
        CancellationToken cancellationToken = default)
    {
        AiProcessorBatchResponse<AiTextGenerationOutput> batchResponse =
            await _textGenerationBatchProcessor.ProcessAsync(
                inputs
                    .Select((x, i) => new AiProcessorBatchInput<AiTextGenerationInput> { Id = i.ToString(), Body = x }),
                config,
                cancellationToken);

        return batchResponse;
    }
}
