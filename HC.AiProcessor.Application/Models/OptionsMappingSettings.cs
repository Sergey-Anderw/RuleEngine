namespace HC.AiProcessor.Application.Models;

public record OptionsMappingSettings
{
    public required string SystemPrompt { get; init; }
    public required string UserPrompt { get; set; }
}
