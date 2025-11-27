using HC.AiProcessor.Entity.Catalog.Enums;
using HC.Packages.Persistent.Entities;

namespace HC.AiProcessor.Entity.Catalog;

public class Product : EntityBase
{
    public long ClientId { get; set; }
    public long? FamilyId { get; set; }

    public ProductStatusEnum Status { get; set; }

    public string Code { get; set; }
    public string ExternalId { get; set; }

    public long? DataSourceId { get; set; }
    public DateTimeOffset? LastSyncTime { get; set; }
    public JsonObject Metadata { get; set; }
    public long? MerchantId { get; set; }

    public Product(
        long clientId,
        long? dataSourceId,
        string externalId,
        long? familyId,
        string code,
        ProductStatusEnum status,
        DateTimeOffset? lastSyncTime,
        JsonObject metadata,
        long? merchantId
    )
    {
        ClientId = clientId;
        DataSourceId = dataSourceId;
        ExternalId = externalId;
        FamilyId = familyId;
        Code = code;
        Status = status;
        LastSyncTime = lastSyncTime;
        Metadata = metadata;
        MerchantId = merchantId;
    }

    public Product() { }

    public Family? Family { get; set; }

    public ICollection<ProductAttribute> ProductAttributes { get; init; } = new HashSet<ProductAttribute>();
}
