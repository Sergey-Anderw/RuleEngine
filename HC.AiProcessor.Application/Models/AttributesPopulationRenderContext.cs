namespace HC.AiProcessor.Application.Models;

public record AttributesPopulationRenderContext
{
    public required string Language { get; init; }
    public required string Label { get; init; }

    public required string AttributesJson { get; init; }
}
