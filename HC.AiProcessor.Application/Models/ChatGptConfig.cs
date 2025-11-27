namespace HC.AiProcessor.Application.Models;

public record ChatGptConfig
{
    public required string Model { get; set; }
    public required string ApiKey { get; set; }
    public required string ChatCompletionEndpoint { get; set; }
    public long RequestTimeout { get; set; } = 60 * 10; //sec
}
