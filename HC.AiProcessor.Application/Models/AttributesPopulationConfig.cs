namespace HC.AiProcessor.Application.Models;

public record AttributesPopulationConfig : ChatCompletionConfig
{
    public string? OptionsMappingModel { get; set; }
}
