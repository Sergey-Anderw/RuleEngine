using HC.AiProcessor.Entity.Catalog;
using HC.Packages.Persistent.EntityMaps;

namespace HC.AiProcessor.Infrastructure.EntityTypeConfigurations.Catalog;

public class FamilyConfiguration : EntityMapBase<Family>
{
    public override void Configure(EntityTypeBuilder<Family> builder)
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

        builder
            .HasOne(x => x.LabelAttribute)
            .WithOne(x => x.LabelForFamily)
            .HasForeignKey<Family>(x => x.LabelAttributeId)
            .OnDelete(DeleteBehavior.SetNull);

        builder
            .HasOne(x => x.ImageAttribute)
            .WithOne(x => x.ImageForFamily)
            .HasForeignKey<Family>(x => x.ImageAttributeId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.Code)
            .IsUnique();
    }
}
