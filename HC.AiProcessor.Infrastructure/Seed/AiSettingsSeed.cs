using HC.AiProcessor.Entity.Ai.Enums;

namespace HC.AiProcessor.Infrastructure.Seed;

internal sealed class AiSettingsItem
{
    public required AiSettingsType Type { get; set; }
    public required JsonObject Settings { get; set; }
    public required JsonObject Config { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
}
