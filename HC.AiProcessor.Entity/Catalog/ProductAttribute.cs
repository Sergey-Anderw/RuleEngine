using HC.Packages.Persistent.Entities;

namespace HC.AiProcessor.Entity.Catalog;

public class ProductAttribute : EntityBase
{
    public long ProductId { get; set; }
    public long AttributeId { get; set; }
    public JsonValueStructure Value { get; set; }
    public long? DimensionUnitId { get; set; }

    public ProductAttribute(
        long productId,
        long attributeId,
        JsonValueStructure value,
        long? dimensionUnitId
    )
    {
        ProductId = productId;
        AttributeId = attributeId;
        Value = value;
        DimensionUnitId = dimensionUnitId;
    }

    public ProductAttribute()
    {
    }

    private Product? _product;

    public Product Product
    {
        get => _product ?? throw new InvalidOperationException($"Uninitialized property: {nameof(Product)}");
        set => _product = value;
    }

    private Attribute _attribute;

    public Attribute Attribute
    {
        get => _attribute ?? throw new InvalidOperationException($"Uninitialized property: {nameof(Attribute)}");
        set => _attribute = value;
    }
}
