using HC.AiProcessor.Entity.Ai;
using HC.AiProcessor.Entity.Ai.Enums;
using HC.Packages.Persistent.EntityMaps;

namespace HC.AiProcessor.Infrastructure.EntityTypeConfigurations.Ai;

public class AiProductConfiguration : EntityMapBase<AiProduct>
{
    public override void Configure(EntityTypeBuilder<AiProduct> builder)
    {
        base.Configure(builder);

        builder
            .Property(t => t.OriginalId)
            .IsRequired();

        builder
            .Property(t => t.ClientId)
            .IsRequired();

        builder
            .Property(t => t.Code)
            .IsRequired();

        builder
            .Property(t => t.ExternalId)
            .IsRequired();

        // should be synchronized with catalog.products.status column
        builder
            .Property(t => t.Status)
            .HasConversion(
                x => x.ToString(),
                x => (AiProductStatusEnum) Enum.Parse(typeof(AiProductStatusEnum), x, true))
            .HasMaxLength(200);

        builder
            .HasIndex(t => t.OriginalId)
            .IsUnique();

        builder
            .HasIndex(x => x.Code)
            .IsUnique();
    }
}
