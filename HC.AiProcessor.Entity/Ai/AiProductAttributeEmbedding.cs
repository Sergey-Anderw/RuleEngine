using HC.Packages.Persistent.Entities;
using Pgvector;

namespace HC.AiProcessor.Entity.Ai;

public class AiProductAttributeEmbedding : EntityBase, IHardDeletableEntity
{
    public long ClientId { get; set; }
    public long ProductId { get; set; }
    public string ProductCode { get; set; }
    public long AttributeId { get; set; }
    public string AttributeCode { get; set; }
    public string Value { get; set; }
    public string? Locale { get; set; }
    public string? Channel { get; set; }
    public Vector Embedding { get; set; }
    public JsonValueStructure OriginalValue { get; set; }
}
