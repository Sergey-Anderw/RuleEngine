using HC.AiProcessor.Entity.Catalog;
using HC.Packages.Persistent.EntityMaps;

namespace HC.AiProcessor.Infrastructure.EntityTypeConfigurations.Catalog;

public class FamilyAttributeConfiguration : EntityMapBase<FamilyAttribute>
{
    public override void Configure(EntityTypeBuilder<FamilyAttribute> builder)
    {
        base.Configure(builder);

        builder
            .HasIndex(fa => new { fa.FamilyId, fa.AttributeId })
            .IsUnique();

        builder
            .HasOne(t => t.Family)
            .WithMany(t => t.FamilyAttributes)
            .HasForeignKey(t => t.FamilyId);

        builder
            .HasOne(t => t.Attribute)
            .WithMany(t => t.FamilyAttributes)
            .HasForeignKey(t => t.AttributeId);

        builder
            .Property(t => t.Ordinal)
            .IsRequired();
    }
}
