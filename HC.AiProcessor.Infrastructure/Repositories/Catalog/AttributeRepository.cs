using HC.Packages.Persistent.Infrastructure;
using Attribute = HC.AiProcessor.Entity.Catalog.Attribute;

namespace HC.AiProcessor.Infrastructure.Repositories.Catalog;

public interface IAttributeRepository : IRepositoryEntityBase<Attribute, long>
{
    Task<IReadOnlyDictionary<long, Attribute>> GetAttributeDictionary(IEnumerable<long> attributeIds,
        CancellationToken ct);
}

internal sealed class AttributeRepository(DataContextProvider context)
    : RepositoryEntityBase<Attribute, long>(context), IAttributeRepository
{
    public async Task<IReadOnlyDictionary<long, Attribute>> GetAttributeDictionary(
        IEnumerable<long> attributeIds,
        CancellationToken ct)
    {
        Dictionary<long, Attribute> attributesDict = await EntitySet
            .AsNoTracking()
            .Where(x => attributeIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x, ct);

        return attributesDict;
    }
}
