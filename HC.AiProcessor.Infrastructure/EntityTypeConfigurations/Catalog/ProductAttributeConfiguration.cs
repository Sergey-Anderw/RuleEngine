using HC.AiProcessor.Entity.Catalog;
using HC.Packages.Persistent.EntityMaps;

namespace HC.AiProcessor.Infrastructure.EntityTypeConfigurations.Catalog;

public class ProductAttributeConfiguration : EntityMapBase<ProductAttribute>
{
    public override void Configure(EntityTypeBuilder<ProductAttribute> builder)
    {
        base.Configure(builder);

        builder.Property(t => t.Value)
            .IsRequired()
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'{}'::jsonb");

        builder.HasOne(t => t.Product)
            .WithMany(t => t.ProductAttributes)
            .HasForeignKey(t => t.ProductId);

        builder.HasOne(t => t.Attribute)
            .WithMany(t => t.ProductAttributes)
            .HasForeignKey(t => t.AttributeId);
    }
}
