using HC.AiProcessor.Entity.Ai;
using HC.AiProcessor.Entity.Ai.Enums;
using HC.Packages.Persistent.EntityMaps;

namespace HC.AiProcessor.Infrastructure.EntityTypeConfigurations.Ai;

public class AiProcessorTaskConfiguration : EntityMapBase<AiProcessorTask>
{
    public override void Configure(EntityTypeBuilder<AiProcessorTask> builder)
    {
        base.Configure(builder);

        builder
            .Property(x => x.Data)
            .IsRequired()
            .HasColumnType("jsonb");

        builder
            .Property(t => t.Status)
            .HasConversion(
                x => x.ToString(),
                x => (AiProcessorTaskStatusType) Enum.Parse(typeof(AiProcessorTaskStatusType), x, true))
            .HasMaxLength(32);

        builder
            .Property(x => x.Result)
            .HasColumnType("jsonb");
    }
}
