namespace HC.AiProcessor.Application.Models;

public record ChatGptGenerationSettings
{
    public string? SetupRequest { get; init; }
    public string Prompt { get; set; } = null!;

    [Obsolete("Use " + nameof(Prompt) + " instead.")]
    public string Promt
    {
        get => Prompt;
        set => Prompt = value;
    }
}
