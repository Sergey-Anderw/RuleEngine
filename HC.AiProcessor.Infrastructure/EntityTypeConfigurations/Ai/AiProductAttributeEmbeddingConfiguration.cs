using HC.AiProcessor.Entity.Ai;
using HC.Packages.Persistent.EntityMaps;

namespace HC.AiProcessor.Infrastructure.EntityTypeConfigurations.Ai;

public class AiProductAttributeEmbeddingConfiguration : EntityMapBase<AiProductAttributeEmbedding>
{
    public override void Configure(EntityTypeBuilder<AiProductAttributeEmbedding> builder)
    {
        base.Configure(builder);

        builder
            .Property(t => t.ClientId)
            .IsRequired();

        builder
            .Property(t => t.ProductId)
            .IsRequired();

        // should be synchronized with catalog.products.code column
        builder
            .Property(u => u.ProductCode)
            .IsRequired();

        builder
            .Property(u => u.AttributeId)
            .IsRequired();

        // should be synchronized with catalog.attributes.code column
        builder
            .Property(u => u.AttributeCode)
            .IsRequired();

        builder
            .Property(u => u.Value)
            .IsRequired();

        builder
            .Property(u => u.Embedding)
            .IsRequired()
            .HasColumnType("vector(512)");

        builder
            .Property(u => u.OriginalValue)
            .IsRequired()
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'{}'::jsonb");

        builder
            .HasIndex(i => i.ClientId);

        builder
            .HasIndex(i => i.ProductId);

        builder
            .HasIndex(i => i.ProductCode);

        builder
            .HasIndex(i => i.AttributeId);

        builder
            .HasIndex(i => i.AttributeCode);

        builder
            .HasIndex(i => i.Embedding)
            .HasMethod("hnsw")
            .HasOperators("vector_l2_ops")
            .HasStorageParameter("m", 48)
            .HasStorageParameter("ef_construction", 128);
    }
}
