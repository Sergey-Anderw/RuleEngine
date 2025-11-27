using HC.AiProcessor.Entity.Ai.Enums;
using HC.Packages.Persistent.Entities;

namespace HC.AiProcessor.Entity.Ai;

public class AiProduct : EntityBase, IHardDeletableEntity
{
    public long OriginalId { get; set; }
    public long ClientId { get; set; }
    public string Code { get; set; }
    public string ExternalId { get; set; }
    public AiProductStatusEnum Status { get; set; }
    public long? FamilyId { get; set; }
}
