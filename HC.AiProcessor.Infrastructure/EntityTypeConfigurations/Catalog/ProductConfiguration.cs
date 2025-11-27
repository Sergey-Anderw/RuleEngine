using HC.AiProcessor.Entity.Catalog;
using HC.AiProcessor.Entity.Catalog.Enums;
using HC.Packages.Persistent.EntityMaps;

namespace HC.AiProcessor.Infrastructure.EntityTypeConfigurations.Catalog;

public class ProductConfiguration : EntityMapBase<Product>
{
    public override void Configure(EntityTypeBuilder<Product> builder)
    {
        base.Configure(builder);

        builder.Property(t => t.ClientId)
            .IsRequired();

        builder.Property(t => t.ExternalId)
            .IsRequired();

        builder.Property(t => t.Code)
            .IsRequired();

        builder.Property(t => t.Status)
            .HasConversion(x => x.ToString(), x => (ProductStatusEnum)Enum.Parse(typeof(ProductStatusEnum), x, true))
            .HasMaxLength(200);

        builder.Property(t => t.Metadata)
            .IsRequired()
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'{}'::jsonb");

        builder.HasOne(t => t.Family)
            .WithMany(t => t.Products)
            .HasForeignKey(t => t.FamilyId);

        builder.HasIndex(x => x.Code)
            .IsUnique();

        builder.HasIndex(x => x.Status);
    }
}
