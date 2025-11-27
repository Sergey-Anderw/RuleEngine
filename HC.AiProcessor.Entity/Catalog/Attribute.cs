using HC.Packages.Catalog.Contracts.V1.Enums;
using HC.Packages.Persistent.Entities;

namespace HC.AiProcessor.Entity.Catalog;

public class Attribute : EntityBase
{
    public long ClientId { get; set; }
    public long? BrandId { get; set; }

    public string Code { get; set; }
    public JsonValueStructure Name { get; set; }
    public AttributeValueTypeEnum ValueType { get; set; }

    public AttributeStatusEnum Status { get; set; }

    public bool IsCategoryFilter { get; set; }
    public JsonAttributeSettingsStructure Settings { get; set; }

    public long? AttributeGroupId { get; set; }
    public int AttributeGroupOrdinal { get; set; }

    public bool ValuePerChannel { get; set; }
    public bool ValuePerLocale { get; set; }
    public bool IsReadOnly { get; set; }

    public JsonValueStructure Guidelines { get; set; }

    public Attribute()
    {
    }

    public Attribute(
        long clientId,
        long? brandId,
        string code,
        JsonValueStructure name,
        bool isCategoryFilter,
        long? attributeGroupId,
        int attributeGroupOrdinal,
        AttributeValueTypeEnum valueType,
        AttributeStatusEnum status,
        JsonAttributeSettingsStructure settings,
        bool valuePerChannel,
        bool valuePerLocale,
        bool isReadOnly,
        JsonValueStructure guidelines
    )
    {
        ClientId = clientId;
        BrandId = brandId;
        Code = code;
        Name = name;
        IsCategoryFilter = isCategoryFilter;
        AttributeGroupId = attributeGroupId;
        AttributeGroupOrdinal = attributeGroupOrdinal;
        ValueType = valueType;
        Status = status;
        Settings = settings;
        ValuePerChannel = valuePerChannel;
        ValuePerLocale = valuePerLocale;
        IsReadOnly = isReadOnly;
        Guidelines = guidelines;
    }

    public ICollection<ProductAttribute> ProductAttributes { get; } = new HashSet<ProductAttribute>();
    public ICollection<FamilyAttribute> FamilyAttributes { get; } = new HashSet<FamilyAttribute>();
}
