using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using HC.Packages.Catalog.Contracts.V1.Enums;
using NJsonSchema;
using NJsonSchema.Generation;

namespace HC.AiProcessor.Application.Services;

internal sealed partial class ChatGptAttributesPopulationService
{
    private const bool ExcludeSchemaUriFromOutputSchema = true;
    private readonly Lazy<string> _outputJsonSchemaFactory = new(CreateOutputJsonSchema);

    private static string CreateOutputJsonSchema()
    {
        var settings = new SystemTextJsonSchemaGeneratorSettings
        {
            SchemaNameGenerator = new CustomSchemaNameGenerator()
        };
        var generator = new JsonSchemaGenerator(settings);

        JsonSchema schema = generator.Generate(typeof(AttributesPopulationResponse));

        if (!schema.Definitions
                .TryGetValue(AttributePopulationResult.SchemaName, out JsonSchema? itemDefinition))
            throw new InvalidOperationException(
                $"Schema definition {AttributePopulationResult.SchemaName} not found.");

        if (!itemDefinition.Properties
                .TryGetValue(AttributePopulationResult.ValueJsonPropertyName, out JsonSchemaProperty? valueProperty))
            throw new InvalidOperationException(
                $"Property {AttributePopulationResult.ValueJsonPropertyName} not found.");

        valueProperty.Type = 0;
        valueProperty.AnyOf.Clear();
        valueProperty.AnyOf.Add(new JsonSchema
        {
            Type = JsonObjectType.Boolean,
            Description =
                $"Value for '{nameof(PromptAttributeValueTypeEnum.Boolean)}' attributes."
        });
        valueProperty.AnyOf.Add(new JsonSchema
        {
            Type = JsonObjectType.Number,
            Description =
                $"Value for '{nameof(PromptAttributeValueTypeEnum.Integer)}' or '{nameof(PromptAttributeValueTypeEnum.Decimal)}' attributes."
        });
        valueProperty.AnyOf.Add(new JsonSchema
        {
            Type = JsonObjectType.String,
            Description =
                $"Value for '{nameof(PromptAttributeValueTypeEnum.Text)}', '{nameof(PromptAttributeValueTypeEnum.FormattedText)}', '{nameof(PromptAttributeValueTypeEnum.Date)}' or '{nameof(PromptAttributeValueTypeEnum.SingleChoice)}' attributes."
        });
        valueProperty.AnyOf.Add(new JsonSchema
        {
            Type = JsonObjectType.Array,
            Item = new JsonSchema { Type = JsonObjectType.String },
            Description =
                $"Value for '{nameof(PromptAttributeValueTypeEnum.MultiChoice)}' attributes."
        });
        JsonSchema dateRangeSchema = generator.Generate(typeof(DateRange));
        valueProperty.AnyOf.Add(dateRangeSchema);

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

    private sealed class AttributesPopulationResponse
    {
        public const string SchemaName = "Response";
        public const string ResultsJsonPropertyName = "results";
        public const string SearchResultsJsonPropertyName = "searchResults";

        [Required]
        [Description(
            "An array of attribute population results, each containing the populated value, confidence score, and reasoning.")]
        [JsonPropertyName(ResultsJsonPropertyName)]
        public required IReadOnlyCollection<AttributePopulationResult> Results { get; set; }

        [Required]
        [Description(
            "An array of search results supporting the attribute values, including the related attribute codes and their sources.")]
        [JsonPropertyName(SearchResultsJsonPropertyName)]
        public required IReadOnlyCollection<SearchResult> SearchResults { get; set; }
    }

    private sealed class AttributePopulationResult
    {
        public const string SchemaName = "Result";
        public const string CodeJsonPropertyName = "code";
        public const string ValueJsonPropertyName = "value";
        public const string ConfidenceJsonPropertyName = "confidence";
        public const string ReasonJsonPropertyName = "reason";

        [Required]
        [Description(
            "The unique code of the attribute. This corresponds to the 'code' field in the attribute definition.")]
        [JsonPropertyName(CodeJsonPropertyName)]
        public required string Code { get; set; }

        [Required]
        [Description("The value assigned to the attribute.")]
        [JsonConverter(typeof(ValueConverter))]
        [JsonPropertyName(ValueJsonPropertyName)]
        public required object Value { get; set; }

        [Required]
        [Range(0.0, 1.0)]
        [Description(
            "A confidence score between 0.0 and 1.0 indicating the model's certainty in the correctness of the populated value.")]
        [JsonPropertyName(ConfidenceJsonPropertyName)]
        public required float Confidence { get; set; }

        [Required]
        [Description(
            "A brief explanation of the reasoning behind the chosen value, providing insight into how the model interpreted the input.")]
        [JsonPropertyName(ReasonJsonPropertyName)]
        public required string Reason { get; set; }
    }

    private sealed class SearchResult
    {
        public const string SchemaName = "SearchResult";
        public const string CodesJsonPropertyName = "codes";
        public const string SourcesJsonPropertyName = "sources";

        [Required]
        [Description("An array of attribute codes for which the following sources provide supporting evidence.")]
        [JsonPropertyName(CodesJsonPropertyName)]
        public required IReadOnlyCollection<string> Codes { get; set; }

        [Required]
        [Description(
            "An array of RFC 1738 compliant URLs pointing to online sources (e.g., product pages, documentation, retailer listings) used as evidence to support the populated attribute values.")]
        [JsonPropertyName(SourcesJsonPropertyName)]
        public required IReadOnlyCollection<string> Sources { get; set; }
    }

    private sealed class DateRange
    {
        public const string SchemaName = "DateRange";
        public const string FromJsonPropertyName = "from";
        public const string ToJsonPropertyName = "to";

        [Required]
        [Description("The start of the date range, formatted as an ISO 8601 date (YYYY-MM-DD).")]
        [JsonPropertyName(FromJsonPropertyName)]
        public required string From { get; set; }

        [Required]
        [Description("The end of the date range, formatted as an ISO 8601 date (YYYY-MM-DD).")]
        [JsonPropertyName(ToJsonPropertyName)]
        public required string To { get; set; }
    }

    private sealed class ValueConverter : JsonConverter<object>
    {
        public override object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.TokenType switch
            {
                JsonTokenType.True => true,
                JsonTokenType.False => false,
                JsonTokenType.Number => reader.TryGetInt64(out long int64) ? int64 : reader.GetDouble(),
                JsonTokenType.String => reader.GetString(),
                JsonTokenType.StartArray => JsonSerializer.Deserialize<List<string>>(ref reader, options),
                JsonTokenType.StartObject => JsonSerializer.Deserialize<DateRange>(ref reader, options),
                _ => throw new JsonException($"Unsupported JSON token type: {reader.TokenType}")
            };
        }

        public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value, value.GetType(), options);
        }
    }

    private sealed class CustomSchemaNameGenerator : ISchemaNameGenerator
    {
        public string Generate(Type type)
        {
            if (type == typeof(AttributesPopulationResponse))
                return AttributesPopulationResponse.SchemaName;
            if (type == typeof(AttributePopulationResult))
                return AttributePopulationResult.SchemaName;
            if (type == typeof(SearchResult))
                return SearchResult.SchemaName;
            if (type == typeof(DateRange))
                return DateRange.SchemaName;
            return type.Name;
        }
    }
}
