using System.Text;
using System.Text.Json;
using HC.AiProcessor.Application.Common;
using HC.AiProcessor.Application.Services;
using HC.AiProcessor.Entity.Ai;
using HC.AiProcessor.Infrastructure.Repositories.Ai;
using HC.Packages.AiProcessor.V1.Models;
using HC.Packages.Persistent.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pgvector;

namespace HC.AiProcessor.Application.v1.Commands;

public record GenerateAiProductAttributeEmbeddingsCommand(
    IReadOnlyCollection<AiProcessorGenerateProductAttributeEmbeddingRequest> Requests,
    bool CachingEnabled = false
) : IRequest<GenerateAiProductAttributeEmbeddingsCommandResult>;

public record GenerateAiProductAttributeEmbeddingsCommandResult(
    AiProcessorGenerateProductAttributeEmbeddingsResponse Response
);

public class GenerateAiProductAttributeEmbeddingsCommandHandler(
    IServiceProvider serviceProvider,
    IAiEmbeddingService embeddingService,
    IAiEmbeddingCache embeddingCache,
    ILogger<GenerateAiProductAttributeEmbeddingsCommandHandler> logger
) : IRequestHandler<GenerateAiProductAttributeEmbeddingsCommand, GenerateAiProductAttributeEmbeddingsCommandResult>
{
    private const int ChunkSize = 100;

    private readonly IServiceProvider _serviceProvider =
        serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

    private readonly IAiEmbeddingService _embeddingService =
        embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));

    private readonly IAiEmbeddingCache _embeddingCache =
        embeddingCache ?? throw new ArgumentNullException(nameof(embeddingCache));

    private readonly ILogger<GenerateAiProductAttributeEmbeddingsCommandHandler> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<GenerateAiProductAttributeEmbeddingsCommandResult> Handle(
        GenerateAiProductAttributeEmbeddingsCommand command,
        CancellationToken ct)
    {
        if ((command.Requests?.Count ?? 0) == 0)
            throw new ArgumentException("At least one element is required", nameof(command));

        Dictionary<string, List<AiProcessorGenerateProductAttributeEmbeddingRequest>> groups = command.Requests!
            .GroupBy(x => GetKey(x.ProductId, x.AttributeId, x.Locale, x.Channel))
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (KeyValuePair<string, List<AiProcessorGenerateProductAttributeEmbeddingRequest>> group in groups)
        {
            if (group.Value.Count <= 1)
                continue;

            AiProcessorGenerateProductAttributeEmbeddingRequest x = group.Value[0];
            throw new Exception($"Duplicate requests found, " +
                                $"client id: {x.ClientId}, " +
                                $"product id: {x.ProductId}, " +
                                $"attribute id: {x.AttributeId}, " +
                                $"locale: {x.Locale ?? string.Empty}, " +
                                $"channel: {x.Channel ?? string.Empty}.");
        }

        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        using var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var aiProductAttributeEmbeddingRepository =
            scope.ServiceProvider.GetRequiredService<IAiProductAttributeEmbeddingRepository>();

        var totalCreatedCount = 0;
        var totalUpdatedCount = 0;
        var totalSkippedCount = 0;

        IEnumerable<AiProcessorGenerateProductAttributeEmbeddingRequest[]> requestsChunks =
            command.Requests!.Chunk(ChunkSize);

        foreach (AiProcessorGenerateProductAttributeEmbeddingRequest[] requests in requestsChunks)
        {
            IReadOnlyDictionary<string, AiProductAttributeEmbedding> entityDict =
                await aiProductAttributeEmbeddingRepository.GetDictionary(
                    queries: requests.Select(
                        x => new AiProductAttributeEmbeddingQuery(
                            x.ProductId,
                            x.AttributeId,
                            x.Locale,
                            x.Channel)),
                    getKey: x => GetKey(x.ProductId, x.AttributeId, x.Locale, x.Channel),
                    ct);

            foreach (AiProcessorGenerateProductAttributeEmbeddingRequest request in requests)
            {
                string value = request.Value;

                if (string.IsNullOrWhiteSpace(request.Value))
                {
                    if (_logger.IsEnabled(LogLevel.Debug))
                    {
                        _logger.LogDebug($"Product attribute does not contain value, " +
                                         $"Product id: {request.ProductId}" +
                                         $"Attribute id: {request.AttributeId}" +
                                         $"Locale: '{request.Locale}', " +
                                         $"Channel: '{request.Channel}', " +
                                         $"JsonValueStructure: {JsonSerializer.Serialize(request.OriginalValue, JsonSettingsExtensions.Default)}.");
                    }

                    _logger.LogWarning(
                        $"Skipping attribute {request.AttributeId} for product {request.ProductId} due to empty value.");

                    totalSkippedCount++;
                    continue;
                }

                string key = GetKey(request.ProductId, request.AttributeId, request.Locale, request.Channel);

                if (entityDict.TryGetValue(key, out AiProductAttributeEmbedding? entity) && entity.Value == value)
                {
                    _logger.LogDebug(
                        $"Skipping attribute {request.AttributeId} for product {request.ProductId} because the existing value is identical.");

                    totalSkippedCount++;
                    continue;
                }

                Vector? embedding = await _embeddingCache.GetValueAsync(value, ct);
                if (embedding is null)
                {
                    embedding = await _embeddingService.EmbeddingAsync(request.ClientId, value, ct);

                    if (command.CachingEnabled)
                    {
                        await _embeddingCache.SetValueAsync(value, embedding, ct);
                    }
                }

                if (entity is null)
                {
                    entity = new AiProductAttributeEmbedding
                    {
                        ClientId = request.ClientId,
                        ProductId = request.ProductId,
                        ProductCode = request.ProductCode,
                        AttributeId = request.AttributeId,
                        AttributeCode = request.AttributeCode,
                        Value = request.Value,
                        Locale = request.Locale,
                        Channel = request.Channel,
                        Embedding = embedding,
                        OriginalValue = request.OriginalValue
                    };

                    await aiProductAttributeEmbeddingRepository.Create(entity, ct);

                    totalCreatedCount++;
                }
                else
                {
                    entity.Embedding = embedding;

                    await aiProductAttributeEmbeddingRepository.Update(entity, ct);

                    totalUpdatedCount++;
                }
            }
        }

        if (totalCreatedCount + totalUpdatedCount != 0)
        {
            await uow.BulkSaveChangesAsync(ct);
        }

        return new GenerateAiProductAttributeEmbeddingsCommandResult(
            Response: new AiProcessorGenerateProductAttributeEmbeddingsResponse
            {
                TotalCreatedCount = totalCreatedCount,
                TotalUpdatedCount = totalUpdatedCount,
                TotalSkippedCount = totalSkippedCount
            });
    }

    private static string GetKey(long productId, long attributeId, string? locale, string? channel)
    {
        var sb = new StringBuilder($"{productId}_{attributeId}");

        if (!string.IsNullOrWhiteSpace(locale))
            sb.Append($"_{locale}");

        if (!string.IsNullOrWhiteSpace(channel))
            sb.Append($"_{channel}");

        return sb.ToString();
    }
}
