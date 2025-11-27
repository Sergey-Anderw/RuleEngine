using System.ClientModel;
using System.ClientModel.Primitives;
using System.Diagnostics.CodeAnalysis;
using System.IO.Abstractions;
using System.Text.Json;
using System.Text.Json.Nodes;
using HC.AiProcessor.Application.Constants;
using HC.AiProcessor.Application.Models;
using HC.Packages.AiProcessor.V1.Models;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.Agents.OpenAI;
using OpenAI;
using OpenAI.Batch;
using OpenAI.Files;

namespace HC.AiProcessor.Application.Services;

public interface IAiTextGenerationInputBatchProcessor
{
    Task<AiProcessorBatchResponse<AiTextGenerationOutput>> ProcessAsync(
        IEnumerable<AiProcessorBatchInput<AiTextGenerationInput>> inputs,
        ChatCompletionConfig config,
        CancellationToken cancellationToken = default);
}

internal sealed partial class AiTextGenerationInputBatchProcessor : IAiTextGenerationInputBatchProcessor
{
    private const string Tag = "TEXT_GENERATION_INPUT_BATCH_PROCESSOR";
    private const string BatchRequestsDirName = "open_ai_batches";
    private const string BatchFileExtension = ".jsonl";
    private const string DefaultEndpoint = "/v1/chat/completions";

    private static readonly JsonSerializerOptions JsonSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly IFileSystem _fileSystem;
    private readonly IRandomIdGenerator _randomIdGenerator;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AiTextGenerationInputBatchProcessor> _logger;

    public AiTextGenerationInputBatchProcessor(
        IFileSystem fileSystem,
        IRandomIdGenerator randomIdGenerator,
        IHttpClientFactory httpClientFactory,
        ILogger<AiTextGenerationInputBatchProcessor> logger)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(randomIdGenerator);
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _fileSystem = fileSystem;
        _randomIdGenerator = randomIdGenerator;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    [Experimental("OPENAI001")]
    public async Task<AiProcessorBatchResponse<AiTextGenerationOutput>> ProcessAsync(
        IEnumerable<AiProcessorBatchInput<AiTextGenerationInput>> inputs,
        ChatCompletionConfig config,
        CancellationToken cancellationToken = default)
    {
        IPath path = _fileSystem.Path;

        string tempDirPath = path.Combine(path.GetTempPath(), BatchRequestsDirName);
        string batchFilePath = path.Combine(tempDirPath, _randomIdGenerator.GenerateId() + BatchFileExtension);

        try
        {
            _fileSystem.Directory.CreateDirectory(tempDirPath);

            AiProcessorBatchResponse<AiTextGenerationOutput> result =
                await InternalProcessAsync(inputs, config, batchFilePath, cancellationToken);

            _logger.LogDebug("[{Tag}] Batch processing completed successfully.", Tag);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{Tag}] Unhandled exception while processing batch.", Tag);

            return new AiProcessorBatchResponse<AiTextGenerationOutput>
            {
                Error = new AiProcessorBatchError
                {
                    Code = ErrorCodes.BatchFailedError,
                    Message = ex.Message
                }
            };
        }
        finally
        {
            _logger.LogDebug("[{Tag}] Batch processing finished.", Tag);

            DeleteTempFile(batchFilePath);
        }
    }

    [Experimental("OPENAI001")]
    private async Task<AiProcessorBatchResponse<AiTextGenerationOutput>> InternalProcessAsync(
        IEnumerable<AiProcessorBatchInput<AiTextGenerationInput>> inputs,
        ChatCompletionConfig config,
        string batchFilePath,
        CancellationToken cancellationToken = default)
    {
        IFile file = _fileSystem.File;

        _logger.LogDebug("[{Tag}] Starting writing batch file.", Tag);

        await using (FileSystemStream stream = file.OpenWrite(batchFilePath))
        await using (var streamWriter = new StreamWriter(stream))
        {
            foreach (AiProcessorBatchInput<AiTextGenerationInput> input in inputs)
            {
                cancellationToken.ThrowIfCancellationRequested();

                BatchRequestInput<LightweightChatCompletionsRequest> request =
                    CreateChatCompletionsApiRequestInput(input, config);
                string json = JsonSerializer.Serialize(request, JsonSerializerOptions);
                await streamWriter.WriteLineAsync(json);
            }
        }

        _logger.LogDebug("[{Tag}] Finished writing batch file. Uploading to OpenAI.", Tag);

        OpenAIClient client = OpenAIAssistantAgent.CreateOpenAIClient(
            apiKey: new ApiKeyCredential(config.ApiKey),
            httpClient: _httpClientFactory.CreateClient(WellKnownHttpClients.AiClient));

        OpenAIFileClient fileClient = client.GetOpenAIFileClient();

        ClientResult<OpenAIFile> fileUploadResult = await fileClient.UploadFileAsync(
            batchFilePath,
            purpose: FileUploadPurpose.Batch);

        OpenAIFile openAIFile = fileUploadResult.Value;

        _logger.LogDebug("[{Tag}] Uploaded batch file. OpenAI file id: {FileId}.", Tag, openAIFile.Id);

        DeleteTempFile(batchFilePath);

        BatchClient batchClient = client.GetBatchClient();

        var createBatch = new CreateBatch
        {
            InputFileId = openAIFile.Id,
            Endpoint = DefaultEndpoint,
            CompletionWindow = "24h"
        };
        var binaryData = BinaryData.FromObjectAsJson(createBatch, JsonSerializerOptions);
        var content = BinaryContent.Create(binaryData);

        Batch? batch = null;

        try
        {
            _logger.LogDebug("[{Tag}] Creating batch job in OpenAI.", Tag);

            CreateBatchOperation operation = await batchClient.CreateBatchAsync(
                content,
                waitUntilCompleted: true,
                options: new RequestOptions { CancellationToken = cancellationToken });

            batch = operation.GetRawResponse().Content.ToObjectFromJson<Batch>(JsonSerializerOptions)!;

            _logger.LogDebug(
                "[{Tag}] Batch job created. Status: {Status}, OutputFileId: {OutputFileId}, ErrorFileId: {ErrorFileId}.",
                Tag, batch.Status, batch.OutputFileId, batch.ErrorFileId);

            AiProcessorBatchResponse<AiTextGenerationOutput> response =
                await GetBatchResponseAsync(batch, fileClient, cancellationToken);

            return response;
        }
        finally
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(batch?.InputFileId))
                {
                    _logger.LogDebug("[{Tag}] Deleting input file from OpenAI: {FileId}.", Tag, batch.InputFileId);

                    await fileClient.DeleteFileAsync(batch.InputFileId, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[{Tag}] Failed to delete input file: {FileId}.", Tag, batch!.InputFileId);
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(batch?.OutputFileId))
                {
                    _logger.LogDebug("[{Tag}] Deleting output file from OpenAI: {FileId}.", Tag, batch.OutputFileId);

                    await fileClient.DeleteFileAsync(batch.OutputFileId, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[{Tag}] Failed to delete output file: {FileId}.", Tag, batch!.OutputFileId);
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(batch?.ErrorFileId))
                {
                    _logger.LogDebug("[{Tag}] Deleting error file from OpenAI: {FileId}.", Tag, batch.ErrorFileId);

                    await fileClient.DeleteFileAsync(batch.ErrorFileId, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[{Tag}] Failed to delete error file: {FileId}.", Tag, batch!.ErrorFileId);
            }
        }
    }

    private BatchRequestInput<LightweightChatCompletionsRequest> CreateChatCompletionsApiRequestInput(
        AiProcessorBatchInput<AiTextGenerationInput> input,
        ChatCompletionConfig config)
    {
        _logger.LogDebug("[{Tag}] Creating chat completions API request input for id: {InputId}.", Tag, input.Id);

        var body = new LightweightChatCompletionsRequest { Model = config.Model };

        if (!string.IsNullOrWhiteSpace(input.Body.SystemPrompt))
        {
            _logger.LogDebug("[{Tag}] Adding system prompt for id: {InputId}.", Tag, input.Id);

            body.Messages.Add(new LightweightChatCompletionsRequest.Message
            {
                Role = "system",
                Content = input.Body.SystemPrompt
            });
        }

        if (!string.IsNullOrWhiteSpace(input.Body.UserPrompt))
        {
            _logger.LogDebug("[{Tag}] Adding user prompt for id: {InputId}.", Tag, input.Id);

            body.Messages.Add(new LightweightChatCompletionsRequest.Message
            {
                Role = "user",
                Content = input.Body.UserPrompt
            });
        }

        body.Temperature = input.Body.Temperature;
        body.MaxCompletionTokens = input.Body.MaxOutputTokenCount;

        switch (input.Body.OutputTextFormat)
        {
            case null:
            case AiTextGenerationInput.TextFormat:
            {
                _logger.LogDebug("[{Tag}] Using text response format for id: {InputId}.", Tag, input.Id);

                body.ResponseFormat = new LightweightChatCompletionsRequest.ResponseFormatOptions
                {
                    Type = "text"
                };
                break;
            }
            case AiTextGenerationInput.JsonObjectFormat:
            {
                _logger.LogDebug("[{Tag}] Using json_object response format for id: {InputId}.", Tag, input.Id);

                body.ResponseFormat = new LightweightChatCompletionsRequest.ResponseFormatOptions
                {
                    Type = "json_object"
                };
                break;
            }
            default:
            {
                _logger.LogDebug("[{Tag}] Using json_schema response format for id: {InputId}.", Tag, input.Id);

                string schema = ((AiTextGenerationInput.JsonSchemaFormat) input.Body.OutputTextFormat).Schema;
                var jsonSchemaObject = (JsonObject) JsonNode.Parse(schema)!;

                string jsonSchemaName;

                if (jsonSchemaObject.TryGetPropertyValue("title", out JsonNode? jsonNode))
                {
                    jsonSchemaName = jsonNode!.ToString();
                    jsonSchemaObject.Remove("title");
                }
                else
                {
                    jsonSchemaName = "user_data";
                }

                body.ResponseFormat = new LightweightChatCompletionsRequest.ResponseFormatOptions
                {
                    Type = "json_schema",
                    JsonSchema = new LightweightChatCompletionsRequest.JsonSchema
                    {
                        Name = jsonSchemaName,
                        Strict = true,
                        Schema = jsonSchemaObject
                    }
                };
                break;
            }
        }

        if (input.Body.WebSearchEnabled)
        {
            _logger.LogWarning("[{Tag}] Batch API does not support web search, input id: {Id}.", Tag, input.Id);
        }

        var request = new BatchRequestInput<LightweightChatCompletionsRequest>
        {
            CustomId = input.Id,
            Url = DefaultEndpoint,
            Body = body
        };

        return request;
    }

    private async Task<AiProcessorBatchResponse<AiTextGenerationOutput>> GetBatchResponseAsync(
        Batch batch,
        OpenAIFileClient fileClient,
        CancellationToken cancellationToken)
    {
        switch (batch.Status)
        {
            case "completed":
            {
                if (!string.IsNullOrWhiteSpace(batch.OutputFileId))
                {
                    _logger.LogDebug("[{Tag}] Batch completed. Parsing output file: {OutputFileId}.",
                        Tag,
                        batch.OutputFileId);

                    return await ParseBatchOutputFileAsync(batch.OutputFileId, fileClient, cancellationToken);
                }

                if (!string.IsNullOrWhiteSpace(batch.ErrorFileId))
                {
                    _logger.LogDebug("[{Tag}] Batch completed. Parsing error file: {ErrorFileId}.",
                        Tag,
                        batch.ErrorFileId);

                    return await ParseBatchErrorFileAsync(batch.ErrorFileId, fileClient, cancellationToken);
                }

                _logger.LogWarning("[{Tag}] Batch completed but no output or error file was provided.", Tag);

                return new AiProcessorBatchResponse<AiTextGenerationOutput>
                {
                    Error = new AiProcessorBatchError
                    {
                        Code = ErrorCodes.UnknownError,
                        Message = "Batch completed but no output or error file was provided."
                    }
                };
            }
            case "failed":
            {
                if (batch.Errors is null || batch.Errors.Data.Count == 0)
                {
                    _logger.LogWarning("[{Tag}] Batch failed but no error details were provided.", Tag);

                    return new AiProcessorBatchResponse<AiTextGenerationOutput>
                    {
                        Error = new AiProcessorBatchError
                        {
                            Code = ErrorCodes.BatchFailedError,
                            Message = "Batch failed but no error details were provided."
                        }
                    };
                }

                BatchErrorData errorData = batch.Errors.Data.First();

                _logger.LogWarning("[{Tag}] Batch failed. Error: {ErrorMessage}.", Tag, errorData.Message);

                return new AiProcessorBatchResponse<AiTextGenerationOutput>
                {
                    Error = new AiProcessorBatchError
                    {
                        Code = ErrorCodes.BatchFailedError,
                        Message = errorData.Message
                    }
                };
            }
            default:
            {
                _logger.LogError("[{Tag}] Invalid batch status: {Status}.", Tag, batch.Status);

                throw new InvalidOperationException($"Invalid batch status: {batch.Status}");
            }
        }
    }

    private async Task<AiProcessorBatchResponse<AiTextGenerationOutput>> ParseBatchOutputFileAsync(
        string outputFileId,
        OpenAIFileClient fileClient,
        CancellationToken cancellationToken)
    {
        ClientResult downloadResult = await fileClient.DownloadFileAsync(
            outputFileId,
            options: new RequestOptions { CancellationToken = cancellationToken });

        using var streamReader = new StreamReader(downloadResult.GetRawResponse().ContentStream!);

        var outputs = new List<AiProcessorBatchOutput<AiTextGenerationOutput>>();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string? line = await streamReader.ReadLineAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(line))
                break;

            var response = JsonSerializer
                .Deserialize<BatchRequestOutput<ChatCompletionsResponse>>(line, JsonSerializerOptions)!;

            ChatCompletionsResponse body = response.Response!.Body;

            string content = string.Join(
                separator: Environment.NewLine,
                values: body.Choices
                    .OrderBy(x => x.Index)
                    .Select(x => x.Message.Content));

            outputs.Add(new AiProcessorBatchOutput<AiTextGenerationOutput>
            {
                Id = response.CustomId,
                Body = new AiTextGenerationOutput { Content = content }
            });
        }

        return new AiProcessorBatchResponse<AiTextGenerationOutput> { Outputs = outputs };
    }

    private async Task<AiProcessorBatchResponse<AiTextGenerationOutput>> ParseBatchErrorFileAsync(
        string errorFileId,
        OpenAIFileClient fileClient,
        CancellationToken cancellationToken)
    {
        ClientResult downloadResult = await fileClient.DownloadFileAsync(
            errorFileId,
            options: new RequestOptions { CancellationToken = cancellationToken });

        using var streamReader = new StreamReader(downloadResult.GetRawResponse().ContentStream!);

        var outputs = new List<AiProcessorBatchOutput<AiTextGenerationOutput>>();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string? line = await streamReader.ReadLineAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(line))
                break;

            var response = JsonSerializer
                .Deserialize<BatchRequestOutput<ChatCompletionsErrorResponse>>(line, JsonSerializerOptions)!;

            ChatCompletionsErrorResponse body = response.Response!.Body;

            outputs.Add(new AiProcessorBatchOutput<AiTextGenerationOutput>
            {
                Id = response.CustomId,
                Error = new AiProcessorBatchError
                {
                    Code = ErrorCodes.RequestFailedError,
                    Message = body.Error.Message
                }
            });
        }

        return new AiProcessorBatchResponse<AiTextGenerationOutput> { Outputs = outputs };
    }

    private void DeleteTempFile(string filePath)
    {
        IFile file = _fileSystem.File;

        try
        {
            if (!file.Exists(filePath))
                return;

            file.Delete(filePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[{Tag}] Failed to delete temp file: {FilePath}.", Tag, filePath);
        }
    }
}
