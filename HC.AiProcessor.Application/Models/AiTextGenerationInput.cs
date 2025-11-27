namespace HC.AiProcessor.Application.Models;

public sealed record AiTextGenerationInput
{
    public string? SystemPrompt { get; init; }
    public string? UserPrompt { get; init; }
    public bool WebSearchEnabled { get; init; }
    public float? Temperature { get; set; }
    public int? MaxOutputTokenCount { get; init; }
    public IOutputTextFormat? OutputTextFormat { get; init; }

    public interface IOutputTextFormat
    {
    }

    public sealed record TextFormat : IOutputTextFormat
    {
    }

    public sealed record JsonObjectFormat : IOutputTextFormat
    {
    }

    public sealed record JsonSchemaFormat : IOutputTextFormat
    {
        public required string Schema { get; init; }
    }
}
