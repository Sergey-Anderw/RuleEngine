using System.Text.Json.Nodes;

namespace HC.AiProcessor.Application.Services;

internal sealed partial class AiTextGenerationInputBatchProcessor
{
    #region Create batch

    // https://platform.openai.com/docs/api-reference/batch/create
    private sealed class CreateBatch
    {
        public required string CompletionWindow { get; set; }
        public required string Endpoint { get; set; }
        public required string InputFileId { get; set; }
        public Dictionary<string, string>? Metadata { get; set; }
    }

    #endregion

    #region The batch object

    // https://platform.openai.com/docs/api-reference/batch/object
    private sealed class Batch
    {
        public string Id { get; set; }
        public string Object { get; set; }
        public string Endpoint { get; set; }
        public BatchErrors? Errors { get; set; }
        public string InputFileId { get; set; }
        public string CompletionWindow { get; set; }
        public string Status { get; set; }
        public string OutputFileId { get; set; }
        public string ErrorFileId { get; set; }
        public int? CreatedAt { get; set; }
        public int? InProgressAt { get; set; }
        public int? ExpiresAt { get; set; }
        public int? FinalizingAt { get; set; }
        public int? CompletedAt { get; set; }
        public int? FailedAt { get; set; }
        public int? ExpiredAt { get; set; }
        public int? CancellingAt { get; set; }
        public int? CancelledAt { get; set; }
        public BatchRequestCounts RequestCounts { get; set; }
        public IReadOnlyDictionary<string, string> Metadata { get; set; }
    }

    private sealed class BatchErrors
    {
        public string Object { get; set; }
        public IReadOnlyCollection<BatchErrorData> Data { get; set; }
    }

    public sealed class BatchErrorData
    {
        public string Code { get; set; }
        public int? Line { get; set; }
        public string Message { get; set; }
        public string? Param { get; set; }
    }

    private sealed class BatchRequestCounts
    {
        public int Total { get; set; }
        public int Completed { get; set; }
        public int Failed { get; set; }
    }

    #endregion

    #region The request input object

    // https://platform.openai.com/docs/api-reference/batch/request-input
    private sealed class BatchRequestInput<TBody> where TBody : IBatchRequestInputBody
    {
        public required string CustomId { get; init; }
        public string Method { get; init; } = "POST";
        public required string Url { get; init; }
        public required TBody Body { get; init; }
    }

    [JsonDerivedType(typeof(LightweightChatCompletionsRequest))]
    private interface IBatchRequestInputBody
    {
    }

    // https://platform.openai.com/docs/api-reference/chat/create
    private sealed class LightweightChatCompletionsRequest : IBatchRequestInputBody
    {
        public required string Model { get; set; }
        public List<Message> Messages { get; init; } = [];
        public ResponseFormatOptions? ResponseFormat { get; set; }
        public float? Temperature { get; set; }
        public int? MaxCompletionTokens { get; set; }

        public sealed class Message
        {
            public required string Role { get; init; }
            public required string Content { get; init; }
        }

        public sealed class ResponseFormatOptions
        {
            public required string Type { get; init; }

            // https://platform.openai.com/docs/guides/structured-outputs?api-mode=chat&example=structured-data#supported-schemas
            public JsonSchema? JsonSchema { get; init; }
        }

        public sealed class JsonSchema
        {
            public required string Name { get; init; }
            public string? Description { get; init; }
            public required bool Strict { get; init; }
            public required JsonObject Schema { get; init; }
        }
    }

    #endregion

    #region The request output object

    // https://platform.openai.com/docs/api-reference/batch/request-output
    private sealed class BatchRequestOutput<TResponseBody> where TResponseBody : IBatchResponseBody
    {
        public string Id { get; set; }
        public string CustomId { get; set; }
        public BatchResponse<TResponseBody>? Response { get; set; }
        public BatchRequestOutputError? Error { get; set; }
    }

    private sealed class BatchResponse<TBody> where TBody : IBatchResponseBody
    {
        public string RequestId { get; set; }
        public int StatusCode { get; set; }
        public TBody Body { get; set; }
    }

    private sealed class BatchRequestOutputError
    {
        public string Code { get; set; }
        public string Message { get; set; }
    }

    [JsonDerivedType(typeof(ChatCompletionsResponse))]
    [JsonDerivedType(typeof(ChatCompletionsErrorResponse))]
    private interface IBatchResponseBody
    {
    }

    // https://platform.openai.com/docs/api-reference/chat/object
    private sealed class ChatCompletionsResponse : IBatchResponseBody
    {
        public string Id { get; set; }
        public string Object { get; set; }
        public int? Created { get; set; }
        public string Model { get; set; }
        public List<Choice> Choices { get; set; }

        public sealed class Choice
        {
            public int Index { get; set; }
            public ChoiceMessage Message { get; set; }
            public string FinishReason { get; set; }
        }

        public sealed class ChoiceMessage
        {
            public string Role { get; set; }
            public string Content { get; set; }
        }
    }

    private sealed class ChatCompletionsErrorResponse : IBatchResponseBody
    {
        public ErrorData Error { get; set; }
    }

    private sealed class ErrorData
    {
        public string Message { get; set; }
        public string Type { get; set; }
        public string Param { get; set; }
        public string Code { get; set; }
    }

    #endregion
}
