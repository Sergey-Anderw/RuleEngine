using HC.AiProcessor.Entity.Ai;
using HC.Packages.Persistent.Infrastructure;

namespace HC.AiProcessor.Infrastructure.Repositories.Ai;

public interface IAiProductRepository : IRepositoryEntityBase<AiProduct, long>
{
    Task Create(AiProduct entity, CancellationToken ct);

    Task Update(AiProduct entity, CancellationToken ct);

    Task<IReadOnlyDictionary<long, AiProduct>> GetDictionary(
        IEnumerable<long> originalIds,
        CancellationToken ct);

    Task<int> DeleteImmediately(IEnumerable<long> originalIds, CancellationToken ct);
}

internal sealed class AiProductRepository(DataContextProvider context) :
    RepositoryEntityBase<AiProduct, long>(context), IAiProductRepository
{
    public async Task Create(AiProduct entity, CancellationToken ct)
    {
        await MarkForInsert(entity, ct);
    }

    public async Task Update(AiProduct entity, CancellationToken ct)
    {
        await MarkForUpdate(entity, ct);
    }

    public async Task<IReadOnlyDictionary<long, AiProduct>> GetDictionary(
        IEnumerable<long> originalIds,
        CancellationToken ct)
    {
        return await EntitySet
            .Where(x => originalIds.Contains(x.OriginalId))
            .ToDictionaryAsync(x => x.OriginalId, x => x, ct);
    }

    public async Task<int> DeleteImmediately(IEnumerable<long> originalIds, CancellationToken ct)
    {
        return await EntitySet
            .Where(x => originalIds.Contains(x.OriginalId))
            .ExecuteDeleteAsync(ct);
    }
}
