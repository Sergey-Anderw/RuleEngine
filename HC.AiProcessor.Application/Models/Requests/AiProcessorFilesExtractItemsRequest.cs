using Microsoft.AspNetCore.Http;

namespace HC.AiProcessor.Application.Models.Requests;

public interface IAiProcessorFilesExtractItemsRequest
{
    List<ExtractItem> ExtractItems { get; }
    List<IFormFile> Content { get; }
    string Flow { get; }
    Dictionary<string, object> Context { get; }
}

public class AiProcessorFilesExtractItemsRequest : IAiProcessorFilesExtractItemsRequest
{
    public string Product { get; set; }
    public List<ExtractItem> ExtractItems { get; set; }
    public List<IFormFile> Content { get; set; }
    public string Flow { get; set; }
    public Dictionary<string, object>? Context { get; set; }
}

public class ExtractItem
{
    public string Key { get; set; }
    public string? Description { get; set; }
    public string? Type { get; set; }
}
