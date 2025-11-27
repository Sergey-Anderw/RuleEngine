using HC.Packages.Catalog.Contracts.V1.Enums;
using HC.Packages.Persistent.EntityMaps;
using Attribute = HC.AiProcessor.Entity.Catalog.Attribute;

namespace HC.AiProcessor.Infrastructure.EntityTypeConfigurations.Catalog;

public class AttributeConfiguration : EntityMapBase<Attribute>
{
    public override void Configure(EntityTypeBuilder<Attribute> builder)
    {
        base.Configure(builder);

        builder.Property(t => t.ClientId)
            .IsRequired();

        builder.Property(t => t.Code)
            .IsRequired();

        builder.Property(t => t.Name)
            .IsRequired()
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'{}'::jsonb");

        builder.Property(t => t.IsCategoryFilter)
            .IsRequired();

        builder.Property(t => t.AttributeGroupOrdinal)
            .IsRequired();

        builder.Property(t => t.ValueType)
            .HasConversion(x => x.ToString(), x => (AttributeValueTypeEnum)Enum.Parse(typeof(AttributeValueTypeEnum), x, true))
            .HasMaxLength(200);

        builder.Property(t => t.Status)
            .HasConversion(x => x.ToString(), x => (AttributeStatusEnum)Enum.Parse(typeof(AttributeStatusEnum), x, true))
            .HasMaxLength(200);

        builder.Property(t => t.Settings)
            .IsRequired()
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'{}'::jsonb");

        builder.Property(t => t.AttributeGroupId)
            .IsRequired(false);

        builder.HasIndex(x => x.Code)
            .IsUnique();

        builder.Property(t => t.Guidelines)
            .IsRequired()
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'{}'::jsonb");
    }
}
