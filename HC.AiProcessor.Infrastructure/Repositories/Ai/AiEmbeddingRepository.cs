using HC.AiProcessor.Entity.Ai;
using HC.Packages.Persistent.Infrastructure;
using Pgvector;

namespace HC.AiProcessor.Infrastructure.Repositories.Ai;

public interface IAiEmbeddingRepository : IRepositoryEntityBase<AiEmbedding, long>
{
    Task<AiEmbedding?> GetOrNullByHash(string hash, CancellationToken ct);
    Task<Vector?> GetValueOrNullByHash(string hash, CancellationToken ct);
    Task Create(AiEmbedding entity, CancellationToken ct);
    Task Update(AiEmbedding entity, CancellationToken ct);
    Task Delete(AiEmbedding entity, CancellationToken ct);
    Task<bool> IsExist(string hash, CancellationToken ct);
}

internal sealed class AiEmbeddingRepository(DataContextProvider context)
    : RepositoryEntityBase<AiEmbedding, long>(context), IAiEmbeddingRepository
{
    public async Task<AiEmbedding?> GetOrNullByHash(string hash, CancellationToken ct)
    {
        return await EntitySet
            .Where(x => x.Hash == hash)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<Vector?> GetValueOrNullByHash(string hash, CancellationToken ct)
    {
        return await EntitySet
            .AsNoTracking()
            .Where(x => x.Hash == hash)
            .Select(x => x.Value)
            .FirstOrDefaultAsync(ct);
    }

    public async Task Create(AiEmbedding entity, CancellationToken ct)
    {
        await MarkForInsert(entity, ct);
    }

    public async Task Update(AiEmbedding entity, CancellationToken ct)
    {
        await MarkForUpdate(entity, ct);
    }

    public async Task Delete(AiEmbedding entity, CancellationToken ct)
    {
        await HardDelete(entity, ct);
    }

    public async Task<bool> IsExist(string hash, CancellationToken ct)
    {
        return await EntitySet
            .AsNoTracking()
            .Where(x => x.Hash == hash)
            .AnyAsync(ct);
    }
}
