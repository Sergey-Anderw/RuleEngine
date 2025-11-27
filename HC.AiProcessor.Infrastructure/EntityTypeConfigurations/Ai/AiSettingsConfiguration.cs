using HC.AiProcessor.Entity.Ai;
using HC.AiProcessor.Entity.Ai.Enums;
using HC.Packages.Persistent.EntityMaps;

namespace HC.AiProcessor.Infrastructure.EntityTypeConfigurations.Ai;

public class AiSettingsConfiguration : EntityMapBase<AiSettings>
{
    public override void Configure(EntityTypeBuilder<AiSettings> builder)
    {
        base.Configure(builder);

        builder.Property(t => t.ClientId)
            .IsRequired();

        builder.Property(t => t.Type)
            .IsRequired();

        builder.Property(t => t.Status)
            .IsRequired();

        builder.Property(u => u.Settings)
            .IsRequired()
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'{}'::jsonb");

        builder.Property(u => u.Config)
            .IsRequired()
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'{}'::jsonb");

        builder.Property(e => e.Type)
            .HasMaxLength(64)
            .HasConversion(t => t.ToString(), t => Enum.Parse<AiSettingsType>(t));

        builder.Property(e => e.Status)
            .HasMaxLength(64)
            .HasConversion(t => t.ToString(), t => Enum.Parse<AiSettingsStatusType>(t));
    }
}
