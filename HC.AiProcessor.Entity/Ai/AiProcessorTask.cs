using HC.AiProcessor.Entity.Ai.Enums;
using HC.Packages.Persistent.Entities;

namespace HC.AiProcessor.Entity.Ai;

public class AiProcessorTask : EntityBase
{
    public long ClientId { get; set; }
    public JsonObject Data { get; set; }
    public AiProcessorTaskStatusType Status { get; set; }
    public DateTimeOffset? ExecutionStart { get; set; }
    public DateTimeOffset? ExecutionEnd { get; set; }
    public JsonObject? Result { get; set; }
    public string? Log { get; set; }
}
