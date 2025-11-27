namespace HC.AiProcessor.Application.Models;

public record ChatCompletionConfig
{
    public required string Model { get; set; }
    public required string ApiKey { get; set; }
    public int? MaxOutputTokenCount { get; set; }
}
