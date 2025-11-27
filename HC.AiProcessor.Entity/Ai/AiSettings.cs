using HC.AiProcessor.Entity.Ai.Enums;
using HC.Packages.Persistent.Entities;

namespace HC.AiProcessor.Entity.Ai
{
    public class AiSettings : EntityBase
    {
        public long ClientId { get; set; }
        public AiSettingsType Type { get; set; }
        public AiSettingsStatusType Status { get; set; }

        public JsonObject Settings { get; set; } = null!;
        public JsonObject Config { get; set; } = null!;
    }
}
