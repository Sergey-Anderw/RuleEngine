// TODO: move to HC.Packages.Contracts project (Roman Yefimchuk)

namespace HC.Packages.Contracts.V1.Commands;

public record ExternalActionRequestCommand<T>
{
    public required string Type { get; set; }
    public required string Id { get; set; }
    public T? Data { get; set; }
    public string Version { get; init; } = "1.0";
}

public record ExternalActionResponseCommand<T>
{
    public required string RequestType { get; set; }
    public required string RequestId { get; set; }
    public T? Data { get; set; }
    public string Version { get; init; } = "1.0";
}
