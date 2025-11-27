using HC.AiProcessor.Entity.Ai;
using HC.AiProcessor.Entity.Ai.Enums;
using HC.Packages.Persistent.Infrastructure;

namespace HC.AiProcessor.Infrastructure.Repositories.Ai;

public interface IAiProcessorTaskRepository : IRepositoryEntityBase<AiProcessorTask, long>
{
    Task<IReadOnlyCollection<AiProcessorTask>> GetActive(CancellationToken ct);
}

internal sealed class AiProcessorTaskRepository(DataContextProvider context)
    : RepositoryEntityBase<AiProcessorTask, long>(context), IAiProcessorTaskRepository
{
    public async Task<IReadOnlyCollection<AiProcessorTask>> GetActive(CancellationToken ct)
    {
        return await EntitySet
            .Where(x =>
                x.Status == AiProcessorTaskStatusType.Queued ||
                x.Status == AiProcessorTaskStatusType.InProgress)
            .ToListAsync(ct);
    }
}
