namespace HC.AiProcessor.Application.Models;

public record ChatGptTextEmbeddingGenerationConfig
{
    public required string ModelId { get; set; }
    public required string ApiKey { get; set; }
}
