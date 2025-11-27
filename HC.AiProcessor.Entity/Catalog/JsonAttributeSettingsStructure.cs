using HC.Packages.Persistent.Entities;

namespace HC.AiProcessor.Entity.Catalog;

public enum ValidationRule
{
    Url,
    Email,
    Phone
}

public record JsonAttributeSettingsStructure
{
    public JsonAttributeSettingsStructure(JsonAttributeSettingsDataStructure data)
    {
        Data = data;
    }

    public JsonAttributeSettingsStructure()
    {
    }

    public JsonAttributeSettingsDataStructure Data { get; init; } = new();
    public string Version { get; init; } = "1.0";
}

public record JsonAttributeSettingsDataStructure(
    double? Minimum = null,
    double? Maximum = null,
    bool? AllowNegative = null,
    int? FractionDigits = null,
    ValidationRule? ValidationRule = null,
    bool? AllowHtml = null,
    DateTimeOffset? MinimumDate = null,
    DateTimeOffset? MaximumDate = null,
    List<JsonAttributeSettingsOptionItemStructure>? Options = null,
    string? DimensionFamilyCode = null,
    string? DimensionDefaultUnitCode = null,
    List<string>? AssetFamilyCodes = null
);

public record JsonAttributeSettingsOptionItemStructure(
    string Code,
    List<JsonValueDataItemStructure> Value,
    int Ordinal
);
