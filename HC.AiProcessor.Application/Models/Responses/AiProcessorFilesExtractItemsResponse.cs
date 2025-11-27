namespace HC.AiProcessor.Application.Models.Responses;

public class AiProcessorFilesExtractItemsResponse
{
    public List<ExtractedItem> ExtractedItems { get; set; }  = new List<ExtractedItem>();

    public Dictionary<string, object> Context { get; set; } = new();
}

public class ExtractedItem
{
    public string Key { get; set; }
    public string? Value { get; set; } = null!;
    public string? Confidence { get; set; } = null!;
}
