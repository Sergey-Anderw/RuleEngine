using HC.AiProcessor.Entity.Ai;
using HC.AiProcessor.Entity.Ai.Enums;
using HC.Packages.Persistent.Infrastructure;

namespace HC.AiProcessor.Infrastructure.Repositories.Ai;

public interface IAiSettingsRepository : IRepositoryEntityBase<AiSettings, long>
{
    Task<AiSettings?> GetLatestOrNull(
        long clientId,
        AiSettingsType type,
        DateTimeOffset updatedAt,
        CancellationToken ct = default);
}

internal sealed class AiSettingsRepository(DataContextProvider context) :
    RepositoryEntityBase<AiSettings, long>(context), IAiSettingsRepository
{
    public async Task<AiSettings?> GetLatestOrNull(
        long clientId,
        AiSettingsType type,
        DateTimeOffset updatedAt,
        CancellationToken ct = default)
    {
        return await EntitySet
            .AsNoTracking()
            .Where(x =>
                x.ClientId == clientId &&
                x.Status == AiSettingsStatusType.Enabled &&
                x.Type == type &&
                x.UpdatedAt > updatedAt)
            .FirstOrDefaultAsync(ct);
    }
}
