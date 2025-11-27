
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using HC.AiProcessor.Application.Clients;
using HC.AiProcessor.Infrastructure;
using HC.Packages.Common.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents.OpenAI;
using Microsoft.SemanticKernel.ChatCompletion;
using OpenAI.Files;
using System.ClientModel;
using System.Collections.ObjectModel;
using System.Text;
using HC.AiProcessor.Application.Clients.ChatGPT;
using HC.AiProcessor.Application.Common;
using HC.AiProcessor.Application.Models;
using HC.AiProcessor.Application.Models.Requests;
using HC.AiProcessor.Application.Models.Responses;
using HC.AiProcessor.Application.Extensions;
using HC.AiProcessor.Entity.Ai.Enums;
using HC.Packages.Contracts.V1;
using Microsoft.AspNetCore.Http;
using OpenAI.VectorStores;

namespace HC.AiProcessor.Application.Services;

public interface IAiProcessorFilesExtractItemsService
{
    Task<TimeSpan?> Extract(AiProcessorFilesExtractItemsRequest request, Func<AiProcessorFilesExtractItemsResponse, CancellationToken, Task> cb, CancellationToken ct);
}

public class AiProcessorFilesExtractItemsService(
    IServiceProvider serviceProvider,
    IEndClientContext clientContext,
    ILogger<AiProcessorFilesExtractItemsService> logger)
    : IAiProcessorFilesExtractItemsService
{
    private DateTimeOffset _settingsLastUpdate = new();
    private ChatGptConfig _config = null!;
    private Dictionary<string, ChatGPTExtarctionSettings?> _settings = null!;
    private ChatGPTClient _gptClient = null!;

    /// <summary>
    /// Metadata key to indicate the assistant as created for a sample.
    /// </summary>
    protected const string AssistantSampleMetadataKey = "sksample";

    /// <summary>
    /// Metadata to indicate the assistant as created for a sample.
    /// </summary>
    /// <remarks>
    /// While the samples do attempt delete the assistants it creates, it is possible
    /// that some assistants may remain.  This metadata can be used to identify and sample
    /// agents for clean-up.
    /// </remarks>
    protected static readonly ReadOnlyDictionary<string, string> AssistantSampleMetadata =
        new(new Dictionary<string, string>
        {
            { AssistantSampleMetadataKey, bool.TrueString }
        });



    private async Task TryLoadSettings(CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        await using var dbContext = scope.ServiceProvider.GetRequiredService<AiProcessorDbContext>();
        var settings = await dbContext.AiSettings
            .Where(x =>
                    x.ClientId == clientContext.ClientId
                    && x.Status == HC.AiProcessor.Entity.Ai.Enums.AiSettingsStatusType.Enabled
                    && x.Type == AiSettingsType.ExtractionChatGpt
                    && x.DeletedAt == null
                    && x.UpdatedAt > _settingsLastUpdate)
                .FirstOrDefaultAsync(ct);
        
        if (settings is not null)
        {
            _settings = JsonSerializer.Deserialize<Dictionary<string, ChatGPTExtarctionSettings?>>(settings.Settings!, JsonSettingsExtensions.Default)!;
            _config = JsonSerializer.Deserialize<ChatGptConfig>(settings.Config!, JsonSettingsExtensions.Default)!;
            _gptClient?.Dispose();
            _gptClient = serviceProvider.GetRequiredService<ChatGPTClient>();
            _gptClient.Configure(_config);
            _settingsLastUpdate = settings.UpdatedAt ?? DateTimeOffset.UtcNow;
        }
    }


    [Experimental("SKEXP0110")]
    public async Task<TimeSpan?> Extract(AiProcessorFilesExtractItemsRequest request, Func<AiProcessorFilesExtractItemsResponse, CancellationToken, Task> cb, CancellationToken ct)
    {
        throw new NotImplementedException();
        // await TryLoadSettings(ct);
        //
        // if (_settings is null)
        //     throw new Exception($"AIFilesExtractItems setting for {clientContext.ClientId} was not found");
        //
        // if (!_settings.TryGetValue(request.Flow ?? "", out var flowSettings) || flowSettings is null)
        //     throw new Exception($"AIFilesExtractItems settings for flow {request.Flow} was not found");
        //
        // var uploadedFiles = new List<string>();
        // try
        // {
        //     #pragma warning disable SKEXP0110
        //     var provider = OpenAIClientProvider.ForOpenAI(new ApiKeyCredential(_config.ApiKey));
        //     var fileClient = provider.Client.GetOpenAIFileClient();
        //     
        //     var vectorStoreCreationOptions = new VectorStoreCreationOptions
        //     {
        //         Metadata = { { AssistantSampleMetadataKey, bool.TrueString } }
        //     };
        //
        //     foreach (var fileItem in request.Content)
        //     {
        //         OpenAIFile fileInfo =
        //             await fileClient.UploadFileAsync(
        //                 fileItem.OpenReadStream(),
        //                 filename:$"{request.Product}_{fileItem.FileName.Replace(".pdf", "")}_document.pdf",
        //                 FileUploadPurpose.Assistants,
        //                 ct);
        //      
        //         uploadedFiles.Add(fileInfo.Id);
        //         vectorStoreCreationOptions.FileIds.Add(fileInfo.Id);
        //     }
        //
        //     // Create a vector-store
        //     var vectorStoreClient = provider.Client.GetVectorStoreClient();
        //     var vectorStore =
        //         await vectorStoreClient.CreateVectorStoreAsync(waitUntilCompleted: false,
        //             vectorStoreCreationOptions, 
        //             cancellationToken: ct);
        //
        //     #pragma warning restore SKEXP0110
        //
        //     var agent =
        //         await OpenAIAssistantAgent.CreateAsync(
        //             provider,
        //             definition: new OpenAIAssistantDefinition(_config.Model)
        //             {
        //                 EnableFileSearch = true,
        //                 Metadata = AssistantSampleMetadata,
        //                 Instructions = flowSettings.SetupRequest
        //             },
        //             kernel: new Kernel(), 
        //             cancellationToken: ct);
        //
        //     var gptRequest = RenderGptRequest(flowSettings, request);
        //
        //     var threadId = await agent.CreateThreadAsync(
        //         new OpenAIThreadCreationOptions
        //         {
        //             Metadata = AssistantSampleMetadata,
        //             VectorStoreId = vectorStore.VectorStoreId
        //         }, ct);
        //
        //     var assistantResponse = new StringBuilder();
        //     try
        //     {
        //         ChatMessageContent message = new(AuthorRole.User, gptRequest);
        //         await agent.AddChatMessageAsync(threadId, message, ct);
        //
        //         await foreach (var aiResponse in agent.InvokeAsync(threadId, cancellationToken: ct))
        //         {
        //             assistantResponse.AppendLine(aiResponse.Content);
        //         }
        //     }
        //     finally
        //     {
        //         await agent.DeleteThreadAsync(threadId, ct);
        //         await agent.DeleteAsync(ct);
        //         await vectorStoreClient.DeleteVectorStoreAsync(vectorStore.VectorStoreId, ct);
        //         foreach (var uploadedFile in uploadedFiles)
        //         {
        //             await fileClient.DeleteFileAsync(uploadedFile, ct);
        //         }
        //     }
        //
        //     var items = ParseGptResponse(assistantResponse.ToString());
        //
        //     var response = new AiProcessorFilesExtractItemsResponse
        //     {
        //         Context = request.Context,
        //         ExtractedItems = items.ToList()
        //     };
        //
        //     await cb(response, ct);
        //
        //     return null;
        // }
        // catch (GPTLimitException ex)
        // {
        //     logger.LogError(ex, "AiTranslationService: limit reached");
        //     throw;
        // }
        // catch (GPTErrorException ex)
        // {
        //     logger.LogError(ex, "AiTranslationService: translation failed");
        //     throw;
        // }
    }

    public async Task<byte[]> ReadAllBytesAsync(IFormFile file)
    {
        if (file == null)
            throw new ArgumentNullException(nameof(file));

        using var memoryStream = new MemoryStream();
        await file.CopyToAsync(memoryStream);
        return memoryStream.ToArray();
    }

    

    private string RenderGptRequest(ChatGPTExtarctionSettings settings, AiProcessorFilesExtractItemsRequest request)
    {
        var formatter = request.ExtractItems.Select(x => settings.EntryFormat.ProcessTemplate(x));
        string items;
        if (string.IsNullOrEmpty(settings.EntrySeparator))
            items = string.Concat(formatter);
        else
            items = string.Join(settings.EntrySeparator, formatter);

        var ctx = new
        {
            Items = items,
            Product = request.Product,
        };

        return settings!.Prompt.ProcessTemplate(ctx);
    }

    private ExtractedItem[] ParseGptResponse(string assistantResponse)
    {
        if (string.IsNullOrEmpty(assistantResponse))
            return [];

        try
        {
            var normalized = Normalize(assistantResponse);
            var extracted =
                JsonSerializer.Deserialize<Dictionary<string, GptExtractedItem>>(normalized,
                    JsonSettingsExtensions.Default); // TODO check settings
            if (extracted is not null)
                return (extracted
                    .Select(x => new ExtractedItem
                    {
                        Key = x.Key, Value = x.Value.Value, Confidence = x.Value.Confidence
                    })
                    .ToArray());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AiProcessorFilesExtractItemsService - ParseGptResponse:");
            throw new Exception($"Process Response: Cannot be processes the message:{assistantResponse}");
        }

        return (Array.Empty<ExtractedItem>());
    }

    public class GptExtractedItem
    {
        public string Value { get; set; }
        public string Text { get; set; }
        public string Confidence { get; set; }
    }

    private string Normalize(string json)
    {
        json = ExtractJsonFromRawString(json);

        var startTrim = "```json";
        var index = json.IndexOf(startTrim, StringComparison.InvariantCultureIgnoreCase);
        var normalized = index != 0 ? json : json.Remove(index, startTrim.Length);

        return normalized.Trim('`').Trim();
    }

    private string ExtractJsonFromRawString(string rawString)
    {
        // Assuming the JSON starts after a certain delimiter or pattern
        int startIndex = rawString.IndexOf("{");
        int endIndex = rawString.LastIndexOf("}");

        if (startIndex >= 0 && endIndex >= 0)
        {
            // Extract and return the JSON portion of the string
            return rawString.Substring(startIndex, endIndex - startIndex + 1);
        }
        else
        {
            // Return an empty string or handle as appropriate
            return "{}";
        }
    }

}
