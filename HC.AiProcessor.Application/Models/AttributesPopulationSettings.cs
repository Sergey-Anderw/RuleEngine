namespace HC.AiProcessor.Application.Models;

public record AttributesPopulationSettings
{
    public required string SetupRequest { get; init; }
    public required string Prompt { get; init; }

    public required string OptionsMappingSetupPrompt { get; init; }
    public required string OptionsMappingPrompt { get; init; }
}
