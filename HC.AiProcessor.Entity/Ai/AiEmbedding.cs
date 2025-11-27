using HC.Packages.Persistent.Entities;
using Pgvector;

namespace HC.AiProcessor.Entity.Ai;

public class AiEmbedding : EntityBase, IHardDeletableEntity
{
    public string Hash { get; set; }
    public string Preview { get; set; }
    public Vector Value { get; set; }
}
