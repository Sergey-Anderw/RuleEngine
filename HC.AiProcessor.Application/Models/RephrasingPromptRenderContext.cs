namespace HC.AiProcessor.Application.Models;

/// <summary>
/// Represents the context required to render a rephrasing prompt,
/// including the original content and the desired tone or emotional style.
/// </summary>
public record RephrasingPromptRenderContext
{
    /// <summary>
    /// The original content that needs to be rephrased.
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// Instructions describing the desired tone or emotional style for the description.
    /// </summary>
    public required string ToneOfVoiceInstructions { get; init; }
}
