namespace HC.AiProcessor.Application.Models;

public record ImageToolClientSettings
{
    public ClaidAiOptions ClaidAi { get; set; }
    
    public SerperClientOptions Serper { get; set; }
    
    public DeWatermarkAiOptions DeWatermarkAi { get; set; }
}

public record ClaidAiOptions
{
    public required string Url     { get; init; }
    public required string ApiKey  { get; init; }
}

public record SerperClientOptions
{
    public required string Url     { get; init; }
    public required string ApiKey  { get; init; }
    public required string Country { get; init; }
}

public class DeWatermarkAiOptions
{
    public required string Url     { get; init; }
    public required string ApiKey  { get; init; }
}
