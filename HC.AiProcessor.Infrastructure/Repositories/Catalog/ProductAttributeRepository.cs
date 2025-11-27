using Dapper;
using HC.AiProcessor.Entity.Catalog;
using HC.Packages.Persistent.Entities;
using HC.Packages.Persistent.Infrastructure;
using Npgsql;

namespace HC.AiProcessor.Infrastructure.Repositories.Catalog;

public interface IProductAttributeRepository : IRepositoryEntityBase<ProductAttribute, long>
{
    Task<IReadOnlyCollection<EnrichedProductAttributeDto>> GetEnrichedProductAttributes(
        IReadOnlyCollection<long> productIds,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<ProductAttributeReferenceDto>> GetProductAttributeReferencesByDeletedProducts(
        IReadOnlyCollection<long> productIds,
        DateTimeOffset deletedAfter = default,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<ProductAttributeReferenceDto>> GetDeletedProductAttributeReferences(
        IReadOnlyCollection<long> productIds,
        DateTimeOffset deletedAfter = default,
        CancellationToken cancellationToken = default);
}

public record EnrichedProductAttributeDto(
    long ProductId,
    string ProductCode,
    long AttributeId,
    string AttributeCode,
    JsonValueStructure Value);

public record ProductAttributeReferenceDto(
    long ProductId,
    long AttributeId);

internal sealed class ProductAttributeRepository(DataContextProvider context)
    : RepositoryEntityBase<ProductAttribute, long>(context), IProductAttributeRepository
{
    public async Task<IReadOnlyCollection<EnrichedProductAttributeDto>> GetEnrichedProductAttributes(
        IReadOnlyCollection<long> productIds,
        CancellationToken cancellationToken = default)
    {
        List<EnrichedProductAttributeDto> productAttributes = await EntitySet
            .AsNoTracking()
            .Where(x => productIds.Contains(x.ProductId))
            .Select(x =>
                new EnrichedProductAttributeDto(x.ProductId, x.Product.Code, x.AttributeId, x.Attribute.Code, x.Value))
            .ToListAsync(cancellationToken);

        return productAttributes;
    }

    public async Task<IReadOnlyCollection<ProductAttributeReferenceDto>> GetProductAttributeReferencesByDeletedProducts(
        IReadOnlyCollection<long> productIds,
        DateTimeOffset deletedAfter = default,
        CancellationToken cancellationToken = default)
    {
        const string sql =
            """
            SELECT pa.product_id as productId, pa.attribute_id as attributeId
            FROM catalog.product_attributes pa
            JOIN catalog.products p ON pa.product_id = p.id
            WHERE p.deleted_at IS NOT NULL
              AND p.deleted_at > @deletedAfter
              AND pa.product_id = ANY(@productIds)
            ORDER BY pa.deleted_at;
            """;

        await using var connection = new NpgsqlConnection(Context.Database.GetConnectionString());

        IEnumerable<ProductAttributeReferenceDto> results = await connection.QueryAsync<ProductAttributeReferenceDto>(
            command: new CommandDefinition(
                sql,
                new { productIds, deletedAfter },
                cancellationToken: cancellationToken));

        return results.ToList();
    }

    public async Task<IReadOnlyCollection<ProductAttributeReferenceDto>> GetDeletedProductAttributeReferences(
        IReadOnlyCollection<long> productIds,
        DateTimeOffset deletedAfter = default,
        CancellationToken cancellationToken = default)
    {
        const string sql =
            """
            SELECT pa.product_id as productId, pa.attribute_id as attributeId
            FROM catalog.product_attributes pa
            WHERE pa.deleted_at IS NOT NULL
              AND pa.deleted_at > @deletedAfter
              AND pa.product_id = ANY(@productIds)
              AND NOT EXISTS (
                SELECT 1
                FROM catalog.product_attributes pa_active
                WHERE pa_active.product_id = pa.product_id
                  AND pa_active.attribute_id = pa.attribute_id
                  AND pa_active.deleted_at IS NULL
              )
            ORDER BY pa.deleted_at;
            """;

        await using var connection = new NpgsqlConnection(Context.Database.GetConnectionString());

        IEnumerable<ProductAttributeReferenceDto> results = await connection.QueryAsync<ProductAttributeReferenceDto>(
            command: new CommandDefinition(
                sql,
                new { productIds, deletedAfter },
                cancellationToken: cancellationToken));

        return results.ToList();
    }
}
