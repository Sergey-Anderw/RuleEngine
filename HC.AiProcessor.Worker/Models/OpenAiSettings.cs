namespace HC.AiProcessor.Worker.Models;

internal sealed record OpenAiSettings
{
    public required string ApiKey { get; set; }
}
