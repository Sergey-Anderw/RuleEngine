using System.Diagnostics;
using HC.AiProcessor.Application.Models;
using HC.Packages.AiProcessor.V1.Models;
using HC.Packages.Catalog.Contracts.V1.Enums;

namespace HC.AiProcessor.Application.Services;

internal sealed partial class ChatGptAttributesPopulationService
{
    private sealed class AttributesPopulationContext
    {
        public required AttributesPopulationConfig Config { get; init; }
        public required AttributesPopulationSettings Settings { get; init; }

        public required Dictionary<string, List<AiProcessorPopulateAttributesRequest.AttributeOption>> OptionsPool
        {
            get;
            init;
        }
    }

    private sealed class PromptAttribute
    {
        public required long Id { get; init; }
        public required string Code { get; init; }
        public required string Label { get; init; }
        public string? Description { get; init; }
        public PromptAttributeValueTypeEnum? ValueType { get; init; }
        public PromptAttributeSettings? Settings { get; set; }
    }

    private enum PromptAttributeValueTypeEnum
    {
        Boolean,
        Integer,
        Decimal,
        Text,
        FormattedText,
        Date,
        DateRange,
        SingleChoice,
        MultiChoice
    }

    private sealed class PromptAttributeSettings
    {
        public double? Minimum { get; init; }
        public double? Maximum { get; init; }
        public bool? AllowNegative { get; init; }
        public int? FractionDigits { get; init; }
        public AiProcessorPopulateAttributesRequest.AttributeValidationRule? ValidationRule { get; init; }
        public bool? AllowHtml { get; init; }
        public DateTimeOffset? MinimumDate { get; init; }
        public DateTimeOffset? MaximumDate { get; init; }
        public List<AiProcessorPopulateAttributesRequest.AttributeOption>? Options { get; init; }
        public List<string>? OptionExamples { get; init; }
    }

    private sealed class AttributesPopulationInput
    {
        public required AiProcessorPopulateAttributesRequest Request { get; init; }
        public required AiTextGenerationInput TextGenerationInput { get; init; }
    }

    private sealed class AttributesPopulationOutput
    {
        public required AttributesPopulationInput Input { get; init; }
        public required AiTextGenerationOutput TextGenerationOutput { get; init; }
    }

    private sealed class ProcessedAttributesPopulationOutput
    {
        public required AttributesPopulationResponse Response { get; init; }
        public required List<UnrecognizedSelectableAttributePopulationResult> UnrecognizedResults { get; init; }
        public required AttributesPopulationInput Input { get; init; }
    }

    [DebuggerDisplay("Code: {Code}, Label: {Label}")]
    private sealed class UnrecognizedSelectableAttributePopulationResult
    {
        public required string Code { get; init; }
        public required string Label { get; init; }
        public required string Context { get; init; }
        public required List<string> Values { get; init; }
        public required List<string> Options { get; init; }
        public required bool SingleSelectionOnly { get; init; }
    }
}
