using HC.Packages.Persistent.Entities;

namespace HC.AiProcessor.Entity.Catalog;

public class Family : EntityBase
{
    public long ClientId { get; set; }
    public string Code { get; set; }
    public JsonObject Name { get; set; }

    public long? LabelAttributeId { get; set; }
    public long? ImageAttributeId { get; set; }

    public Family(
        long clientId,
        string code,
        JsonObject name,
        long? labelAttributeId = null,
        long? imageAttributeId = null
    )
    {
        ClientId = clientId;
        Code = code;
        Name = name;
        LabelAttributeId = labelAttributeId;
        ImageAttributeId = imageAttributeId;
    }

    public Family()
    {
    }

    public FamilyAttribute? LabelAttribute { get; set; }
    public FamilyAttribute? ImageAttribute { get; set; }

    public ICollection<FamilyAttribute> FamilyAttributes { get; } = new HashSet<FamilyAttribute>();
    public ICollection<Product> Products { get; } = new HashSet<Product>();
}
