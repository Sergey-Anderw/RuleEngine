using HC.Packages.Persistent.Entities;

namespace HC.AiProcessor.Entity.Catalog;

public class FamilyAttribute : EntityBase
{
    public long FamilyId { get; set; }
    public long AttributeId { get; set; }
    public int Ordinal { get; set; }

    public FamilyAttribute()
    {
    }

    public FamilyAttribute(
        long familyId,
        long attributeId)
    {
        FamilyId = familyId;
        AttributeId = attributeId;
    }

    public FamilyAttribute(
        long familyId,
        long attributeId,
        int ordinal)
    {
        FamilyId = familyId;
        AttributeId = attributeId;
        Ordinal = ordinal;
    }

    private Family _family;

    public Family Family
    {
        get => _family ?? throw new InvalidOperationException($"Uninitialized property: {nameof(Family)}");
        set => _family = value;
    }

    private Attribute _attribute;

    public Attribute Attribute
    {
        get => _attribute ?? throw new InvalidOperationException($"Uninitialized property: {nameof(Attribute)}");
        set => _attribute = value;
    }

    public Family? LabelForFamily { get; set; }
    public Family? ImageForFamily { get; set; }
}
