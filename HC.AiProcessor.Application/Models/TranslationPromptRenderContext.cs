namespace HC.AiProcessor.Application.Models;

/// <summary>
/// Represents the context required to render a translation prompt,
/// including the content to translate and the source and target languages.
/// </summary>
public record TranslationPromptRenderContext
{
    /// <summary>
    /// The original content that needs to be translated.
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// The language (e.g., "en" or "English") of the original content.
    /// </summary>
    public required string SourceLanguage { get; init; }

    /// <summary>
    /// The language (e.g., "en" or "English") into which the content should be translated.
    /// </summary>
    public required string TargetLanguage { get; init; }
}
