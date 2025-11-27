namespace HC.AiProcessor.Application.Models;

/// <summary>
/// Represents contextual data used to render a product description generation prompt.
/// </summary>
public record GenerationPromptRenderContext
{
    /// <summary>
    /// Language (e.g., "en" or "English") indicating the target language for the generated description.
    /// </summary>
    public required string Language { get; init; }

    /// <summary>
    /// Instructions describing the desired tone or emotional style for the description.
    /// </summary>
    public required string ToneOfVoiceInstructions { get; init; }

    /// <summary>
    /// Minimum number of characters the generated description should contain.
    /// </summary>
    public int? MinLength { get; init; }

    /// <summary>
    /// Maximum number of characters the generated description should not exceed.
    /// </summary>
    public int? MaxLength { get; init; }

    /// <summary>
    /// Indicates whether the generated description is allowed to include HTML formatting.
    /// </summary>
    public required bool AllowHtml { get; init; }

    /// <summary>
    /// Optional custom instructions provided by the user.
    /// These override or supplement default generation logic.
    /// </summary>
    public string? AdditionalInstructions { get; init; }

    /// <summary>
    /// A set of product attribute values, where the key is the attribute code and the value is the attribute's value.
    /// </summary>
    public required IReadOnlyDictionary<string, string> AttributeValues { get; init; }

    /// <summary>
    /// A set of product attribute descriptions, where the key is the attribute code and the value is the attribute's description or explanation.
    /// </summary>
    public required IReadOnlyDictionary<string, string> AttributeDescriptions { get; init; }
}
