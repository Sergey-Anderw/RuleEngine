using Dapper;
using HC.AiProcessor.Entity.Catalog;
using HC.AiProcessor.Entity.Catalog.Enums;
using HC.Packages.Persistent.Infrastructure;
using Npgsql;

namespace HC.AiProcessor.Infrastructure.Repositories.Catalog;

public interface IProductRepository : IRepositoryEntityBase<Product, long>
{
    Task<IReadOnlyCollection<EnrichedProductDto>> GetEnrichedProducts(
        IReadOnlyCollection<long> productIds,
        CancellationToken ct);

    Task<IReadOnlyCollection<ClientProductDto>> GetClientProducts(
        IReadOnlyCollection<long> productIds,
        CancellationToken ct);

    Task<IReadOnlyCollection<long>> GetDeletedProductIds(
        long? clientId = null,
        DateTimeOffset deletedAfter = default,
        CancellationToken ct = default);
}

public record EnrichedProductDto(
    long Id,
    long ClientId,
    string Code,
    string ExternalId,
    ProductStatusEnum Status,
    long? FamilyId);

public record ClientProductDto(
    long Id,
    long ClientId);

internal sealed class ProductRepository(DataContextProvider context)
    : RepositoryEntityBase<Product, long>(context), IProductRepository
{
    public async Task<IReadOnlyCollection<EnrichedProductDto>> GetEnrichedProducts(
        IReadOnlyCollection<long> productIds,
        CancellationToken ct)
    {
        List<EnrichedProductDto> products = await EntitySet
            .AsNoTracking()
            .Where(x => productIds.Contains(x.Id))
            .Select(x =>
                new EnrichedProductDto(x.Id, x.ClientId, x.Code, x.ExternalId, x.Status, x.FamilyId))
            .ToListAsync(ct);

        return products;
    }

    public async Task<IReadOnlyCollection<ClientProductDto>> GetClientProducts(
        IReadOnlyCollection<long> productIds,
        CancellationToken ct)
    {
        List<ClientProductDto> products = await EntitySet
            .AsNoTracking()
            .Where(x => productIds.Contains(x.Id))
            .Select(x => new ClientProductDto(x.Id, x.ClientId))
            .ToListAsync(ct);

        return products;
    }

    public async Task<IReadOnlyCollection<long>> GetDeletedProductIds(
        long? clientId = null,
        DateTimeOffset deletedAfter = default,
        CancellationToken ct = default)
    {
        const string sql =
            """
            SELECT p.id
            FROM catalog.products p
            WHERE p.deleted_at IS NOT NULL
              AND p.deleted_at > @deletedAfter
              AND (@clientId IS NULL OR p.client_id = @clientId)
            ORDER BY p.deleted_at
            """;

        await using var connection = new NpgsqlConnection(Context.Database.GetConnectionString());

        IEnumerable<long> results = await connection.QueryAsync<long>(
            command: new CommandDefinition(sql, new { clientId, deletedAfter }, cancellationToken: ct));

        return results.ToList();
    }
}
