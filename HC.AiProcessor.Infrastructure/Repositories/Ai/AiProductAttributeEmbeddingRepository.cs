using System.Diagnostics;
using Dapper;
using HC.AiProcessor.Entity.Ai;
using HC.Packages.Persistent.Infrastructure;
using Npgsql;
using Pgvector;

namespace HC.AiProcessor.Infrastructure.Repositories.Ai;

public interface IAiProductAttributeEmbeddingRepository : IRepositoryEntityBase<AiProductAttributeEmbedding, long>
{
    Task Create(AiProductAttributeEmbedding entity, CancellationToken ct);
    Task Update(AiProductAttributeEmbedding entity, CancellationToken ct);

    Task<IReadOnlyCollection<long>> GetIds(
        IEnumerable<AiProductAttributeEmbeddingQuery> queries,
        CancellationToken ct);

    Task<IReadOnlyDictionary<string, AiProductAttributeEmbedding>> GetDictionary(
        IEnumerable<AiProductAttributeEmbeddingQuery> queries,
        Func<AiProductAttributeEmbedding, string> getKey,
        CancellationToken ct);

    Task<int> DeleteImmediately(IEnumerable<long> ids, CancellationToken ct);

    Task<AttributeToFamilySimilarityDto[][]> GetAttributeToFamilySimilarityMatrix(
        long clientId,
        long targetProductId,
        IEnumerable<AttributeToFamilySimilarityQuery> queries,
        string? locale = null,
        string? channel = null,
        CancellationToken ct = default);
}

public record AiProductAttributeEmbeddingQuery(
    long ProductId,
    long AttributeId,
    string? Locale,
    string? Channel);

public record AttributeToFamilySimilarityQuery(
    long AttributeId,
    Vector Embedding,
    double MinSimilarity,
    double MaxSimilarity);

[DebuggerDisplay("AttributeId={AttributeId}, FamilyId={FamilyId}, CosineSimilarity={CosineSimilarity}")]
public record AttributeToFamilySimilarityDto
{
    public required long AttributeId { get; init; }
    public required long FamilyId { get; init; }
    public required double CosineSimilarity { get; init; }
}

internal sealed class AiProductAttributeEmbeddingRepository(DataContextProvider context)
    : RepositoryEntityBase<AiProductAttributeEmbedding, long>(context), IAiProductAttributeEmbeddingRepository
{
    public async Task Create(AiProductAttributeEmbedding entity, CancellationToken ct)
    {
        await MarkForInsert(entity, ct);
    }

    public async Task Update(AiProductAttributeEmbedding entity, CancellationToken ct)
    {
        await MarkForUpdate(entity, ct);
    }

    public async Task<IReadOnlyCollection<long>> GetIds(
        IEnumerable<AiProductAttributeEmbeddingQuery> queries,
        CancellationToken ct)
    {
        IQueryable<long> query = null!;

        foreach (AiProductAttributeEmbeddingQuery request in queries)
        {
            IQueryable<long> subQuery = EntitySet
                .AsNoTracking()
                .Where(x => x.ProductId == request.ProductId)
                .Where(x => x.AttributeId == request.AttributeId)
                .Where(x => request.Locale == null || x.Locale == request.Locale)
                .Where(x => request.Channel == null || x.Channel == request.Channel)
                .Select(x => x.Id)
                .Distinct();

            query = query == null ? subQuery : query.Union(subQuery);
        }

        List<long> ids = await query.ToListAsync(ct);

        return ids;
    }

    public async Task<IReadOnlyDictionary<string, AiProductAttributeEmbedding>> GetDictionary(
        IEnumerable<AiProductAttributeEmbeddingQuery> queries,
        Func<AiProductAttributeEmbedding, string> getKey,
        CancellationToken ct)
    {
        IQueryable<AiProductAttributeEmbedding> queryable = null!;

        foreach (var entityQuery in queries)
        {
            IQueryable<AiProductAttributeEmbedding> subQuery = EntitySet
                .Where(x => x.ProductId == entityQuery.ProductId)
                .Where(x => x.AttributeId == entityQuery.AttributeId)
                .Where(x => entityQuery.Locale == null || x.Locale == entityQuery.Locale)
                .Where(x => entityQuery.Channel == null || x.Channel == entityQuery.Channel);

            queryable = queryable == null ? subQuery : queryable.Union(subQuery);
        }

        Dictionary<string, AiProductAttributeEmbedding> entityDictionary =
            await queryable.ToDictionaryAsync(getKey, ct);

        return entityDictionary;
    }

    public async Task<int> DeleteImmediately(IEnumerable<long> ids, CancellationToken ct)
    {
        int deletedCount = await EntitySet
            .Where(x => ids.Contains(x.Id))
            .ExecuteDeleteAsync(ct);

        return deletedCount;
    }

    public async Task<AttributeToFamilySimilarityDto[][]> GetAttributeToFamilySimilarityMatrix(
        long clientId,
        long targetProductId,
        IEnumerable<AttributeToFamilySimilarityQuery> queries,
        string? locale = null,
        string? channel = null,
        CancellationToken ct = default)
    {
        var matrix = new List<AttributeToFamilySimilarityDto[]>();

        await using var connection = new NpgsqlConnection(Context.Database.GetConnectionString());

        foreach (AttributeToFamilySimilarityQuery query in queries)
        {
            const string sql =
                """
                SELECT
                    @attributeId as attributeId,
                    family_id as familyId,
                    AVG(cosine_similarity) AS cosineSimilarity
                FROM (
                  SELECT p.family_id, (1 - (pa.embedding <=> @embedding)) AS cosine_similarity
                  FROM ai.ai_product_attribute_embeddings pa
                  JOIN ai.ai_products p ON pa.product_id = p.original_id
                  WHERE pa.client_id = @clientId
                    AND pa.product_id <> @targetProductId
                    AND pa.attribute_id = @attributeId
                    AND (1 - (pa.embedding <=> @embedding)) BETWEEN @min AND @max
                    AND (@locale is NULL OR pa.locale = @locale)
                    AND (@channel is NULL OR pa.channel = @channel)
                ) sub
                GROUP BY familyId
                ORDER BY cosineSimilarity DESC;
                """;

            AttributeToFamilySimilarityDto[] subResults = (
                    await connection.QueryAsync<AttributeToFamilySimilarityDto>(
                        command: new CommandDefinition(
                            sql,
                            new
                            {
                                clientId,
                                targetProductId,
                                attributeId = query.AttributeId,
                                embedding = query.Embedding,
                                min = query.MinSimilarity,
                                max = query.MaxSimilarity,
                                locale,
                                channel
                            },
                            cancellationToken: ct)))
                .ToArray();

            if (subResults.Length == 0)
                return [];

            matrix.Add(subResults);
        }

        return matrix.ToArray();
    }
}
