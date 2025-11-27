using HC.AiProcessor.Entity.Ai;
using HC.Packages.Persistent.EntityMaps;

namespace HC.AiProcessor.Infrastructure.EntityTypeConfigurations.Ai;

public class AiEmbeddingConfiguration : EntityMapBase<AiEmbedding>
{
    public override void Configure(EntityTypeBuilder<AiEmbedding> builder)
    {
        base.Configure(builder);

        builder
            .Property(t => t.Hash)
            .IsRequired()
            .IsUnicode(false)
            .HasMaxLength(64);

        builder
            .Property(t => t.Preview)
            .IsRequired()
            .HasMaxLength(32);

        builder
            .Property(u => u.Value)
            .IsRequired()
            .HasColumnType("vector(512)");

        builder
            .HasIndex(t => t.Hash)
            .IsUnique();
    }
}
