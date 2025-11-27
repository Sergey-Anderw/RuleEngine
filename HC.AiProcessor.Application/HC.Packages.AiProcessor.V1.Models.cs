// TODO: move to HC.Packages.AiProcessor project (Roman Yefimchuk)

using HC.Packages.Catalog.Contracts.V1.Enums;
using HC.Packages.Persistent.Entities;

namespace HC.Packages.AiProcessor.V1.Models;

public abstract record AiProcessorRequestBase
{
    // TODO: temporary predefined value for backward compatibility
    public long ClientId { get; set; } = 1;
}

#region Chat completion

public abstract record AiProcessorChatCompletionRequestBase : AiProcessorRequestBase
{
    public required string Flow { get; init; }
}

#region Text translation

public abstract record AiProcessorTranslateRequestBase : AiProcessorChatCompletionRequestBase
{
    public required string SourceLanguage { get; init; }
    public required string TargetLanguage { get; init; }
}

public record AiProcessorTranslateRequest : AiProcessorTranslateRequestBase
{
    public IReadOnlyDictionary<string, string> Items { get; init; } = new Dictionary<string, string>();
}

public record AiProcessorTranslateResponse
{
    public IReadOnlyDictionary<string, string> Items { get; init; } = new Dictionary<string, string>();
}

public record AiProcessorStreamingTranslateRequest : AiProcessorTranslateRequestBase
{
    public required string Text { get; init; }
}

public record AiProcessorStreamingTranslateResponse
{
    public string? Text { get; init; }
}

#endregion

#region Text rephrasing

public abstract record AiProcessorRephraseRequestBase : AiProcessorChatCompletionRequestBase
{
    /// <summary>
    /// Affects the mood and emotional impact.
    /// </summary>
    public required string ToneOfVoice { get; init; }
}

public record AiProcessorRephraseRequest : AiProcessorRephraseRequestBase
{
    public IReadOnlyDictionary<string, string> Items { get; init; } = new Dictionary<string, string>();
}

public record AiProcessorRephraseResponse
{
    public IReadOnlyDictionary<string, string> Items { get; init; } = new Dictionary<string, string>();
}

public record AiProcessorStreamingRephraseRequest : AiProcessorRephraseRequestBase
{
    public required string Text { get; init; }
}

public record AiProcessorStreamingRephraseResponse
{
    public string? Text { get; init; }
}

#endregion

#region Text generation

public abstract record AiProcessorGenerateRequestBase : AiProcessorChatCompletionRequestBase
{
    public required string Language { get; init; }
    public required string ToneOfVoice { get; init; }
    public required IReadOnlyCollection<Attribute> Attributes { get; init; } = [];
    public int MinLength { get; init; }
    public int MaxLength { get; init; }
    public bool AllowHtml { get; init; }
    public string? AdditionalInstructions { get; init; }

    public record Attribute
    {
        public required string Label { get; init; }
        public required string Value { get; init; }
        public string? Description { get; init; }
    }
}

public record AiProcessorPopulateAttributesRequest : AiProcessorChatCompletionRequestBase
{
    public required string Language { get; init; }
    public required string Label { get; set; }
    public required IReadOnlyCollection<Attribute> Attributes { get; set; } = [];

    public record Attribute
    {
        public required long Id { get; set; }
        public required string Code { get; set; }
        public required string Label { get; set; }
        public string? Description { get; set; }
        public AttributeValueTypeEnum? ValueType { get; set; }
        public AttributeSettings? Settings { get; set; }
    }

    public enum AttributeValidationRule
    {
        Url,
        Email,
        Phone
    }

    public record AttributeSettings
    {
        public double? Minimum { get; set; }
        public double? Maximum { get; set; }
        public bool? AllowNegative { get; set; }
        public int? FractionDigits { get; set; }
        public AttributeValidationRule? ValidationRule { get; set; }
        public bool? AllowHtml { get; set; }
        public DateTimeOffset? MinimumDate { get; set; }
        public DateTimeOffset? MaximumDate { get; set; }
        public List<AttributeOption>? Options { get; set; }
    }

    public record AttributeOption
    {
        public string Code { get; set; }
        public string Value { get; set; }
    }
}

public record AiProcessorPopulateAttributesResponse
{
    public IReadOnlyCollection<PopulatedAttribute> Results { get; init; } = [];
    public IReadOnlyCollection<string> UnpopulatedAttributeCodes { get; init; } = [];

    public AiProcessorPopulateAttributesResponse(
        IReadOnlyCollection<PopulatedAttribute> results,
        IReadOnlyCollection<string> unpopulatedAttributeCodes)
    {
        Results = results;
        UnpopulatedAttributeCodes = unpopulatedAttributeCodes;
    }

    public record PopulatedAttribute
    {
        public required long Id { get; init; }
        public required string Code { get; init; }
        public required object Value { get; init; }
        public required float Confidence { get; init; }
        public required string Reason { get; init; }
        public required IReadOnlyCollection<string> SourceUrls { get; init; }
    }
}

public record AiProcessorGenerateRequest : AiProcessorGenerateRequestBase
{
}

public record AiProcessorGenerateResponse
{
    public string? Text { get; init; }
}

public record AiProcessorStreamingGenerateRequest : AiProcessorGenerateRequestBase
{
}

public record AiProcessorStreamingGenerateResponse
{
    public string? Text { get; init; }
}

#endregion

#endregion

public record AiProcessorRefreshProductRequest : AiProcessorRequestBase
{
    public required long ProductId { get; set; }
    public required string ProductCode { get; set; }
    public required string ProductExternalId { get; set; }
    public ProductStatusEnum ProductStatus { get; set; }
    public long? ProductFamilyId { get; set; }

    public enum ProductStatusEnum
    {
        Published = 0,
        Draft = 1
    }
}

public record AiProcessorRefreshProductsResponse
{
    public required int TotalCreatedCount { get; set; }
    public required int TotalUpdatedCount { get; set; }
    public required int TotalSkippedCount { get; set; }
}

public record AiProcessorDeleteProductRequest
{
    public required IReadOnlyCollection<long> ProductIds { get; set; }
}

public record AiProcessorDeleteProductsResponse
{
    public required int TotalCount { get; set; }
}

public record AiProcessorGenerateProductAttributeEmbeddingRequest : AiProcessorRequestBase
{
    public required long ProductId { get; set; }
    public required string ProductCode { get; set; }
    public required long AttributeId { get; set; }
    public required string AttributeCode { get; set; }
    public required string Value { get; set; }
    public required JsonValueStructure OriginalValue { get; set; }
    public string? Locale { get; set; }
    public string? Channel { get; set; }
}

public record AiProcessorGenerateProductAttributeEmbeddingsResponse
{
    public required int TotalCreatedCount { get; set; }
    public required int TotalUpdatedCount { get; set; }
    public required int TotalSkippedCount { get; set; }
}

public record AiProcessorDeleteProductAttributeEmbeddingRequest
{
    public required long ProductId { get; set; }
    public required long AttributeId { get; set; }
    public string? Locale { get; set; }
    public string? Channel { get; set; }
}

public record AiProcessorDeleteProductAttributeEmbeddingsResponse
{
    public required int TotalCount { get; set; }
}

public record AiProcessorDetermineProductFamilyRequest : AiProcessorRequestBase
{
    public required long TargetProductId { get; set; }
    public required IReadOnlyDictionary<long, AttributeSimilarityCriterion> Criteria { get; set; }
    public string? Locale { get; set; }
    public string? Channel { get; set; }

    public record AttributeSimilarityCriterion(
        string Value,
        double MinSimilarity,
        double MaxSimilarity);
}

public record AiProcessorDetermineProductFamilyResponse
{
    public required long FamilyId { get; set; }
    public required IReadOnlyDictionary<long, double> Similarities { get; set; }
}

public record AiProcessorSearchProductImagesRequest : AiProcessorRequestBase
{
    public required bool ValidateUrls { get; set; }

    public int ImagesAmount { get; set; } = 3;
    public required IReadOnlyCollection<Product> Products { get; set; }

    public record Product(string Code, string Title);
}

public record AiProcessorSearchProductImagesResponse
{
    public required string Code { get; set; }
    public required IEnumerable<string> ImageUrls { get; set; }
}

public record AiProcessorRemoveWatermarkRequest(string ImageUrl) : AiProcessorRequestBase;

public record AiProcessorRemoveWatermarkResponse
{
    public required string ImagePath { get; set; }

    public required string FileName { get; set; }

    public required string GenFileName { get; set; }

    public required long FileSize { get; set; }

    public required string ContentType { get; set; }
}

public record AiProcessorImageQualityRequest(string ImageUrl) : AiProcessorRequestBase;

public record AiProcessorImageQualityResponse(double OverallQuality);

public record AiProcessorImageTransformationRequest(string ImageUrl) : AiProcessorRequestBase
{
    public BackgroundOperation? Background { get; set; }
    public ChangeSizeOperation? ChangeSize { get; set; }
    public bool? Restoration { get; set; }

    public string? Padding { get; set; }

    public record BackgroundOperation(string Colour = "transparent", bool Clipping = false);

    public record ChangeSizeOperation(int? Width, int? Height, string? Crop, string? Fit);
}

public record AiProcessorImageTransformationResponse(
    string ImageUrl,
    string FileName,
    string Extension,
    double Mps,
    string MimeType,
    int Width,
    int Height,
    string Format);

public record AiProcessorBatchInput<TBody>
{
    public required string Id { get; set; }
    public required TBody Body { get; set; }
}

public record AiProcessorBatchOutput<TBody>
{
    public required string Id { get; set; }
    public TBody? Body { get; set; }
    public AiProcessorBatchError? Error { get; set; }
}

public record AiProcessorBatchRequest<TBody>
{
    public required IReadOnlyCollection<AiProcessorBatchInput<TBody>> Inputs { get; set; }
}

public record AiProcessorBatchResponse<TBody>
{
    public IReadOnlyCollection<AiProcessorBatchOutput<TBody>>? Outputs { get; set; }
    public AiProcessorBatchError? Error { get; set; }
}

public record AiProcessorBatchError
{
    public required string Code { get; set; }
    public required string Message { get; set; }
}
