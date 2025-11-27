using System.Text.Json;
using HC.AiProcessor.Application.Common;
using HC.AiProcessor.Application.Extensions;
using HC.AiProcessor.Infrastructure.Repositories.Catalog;
using HC.Packages.AiProcessor.V1.Models;
using HC.Packages.Persistent.Entities;
using Attribute = HC.AiProcessor.Entity.Catalog.Attribute;

namespace HC.AiProcessor.Worker.Services;

internal interface IProductAttributeEmbeddingRequestsCreator
{
    Task<AiProcessorGenerateProductAttributeEmbeddingRequest[]> CreateGenerateRequestsAsync(
        IReadOnlyCollection<long> productIds,
        CancellationToken ct = default);

    Task<AiProcessorDeleteProductAttributeEmbeddingRequest[]> CreateDeleteRequestsAsync(
        IReadOnlyCollection<long> productIds,
        CancellationToken ct = default);
}

internal sealed class ProductAttributeEmbeddingRequestsCreator(
    IAttributeRepository attributeRepository,
    IProductAttributeRepository productAttributeRepository,
    ILogger<ProductAttributeEmbeddingRequestsCreator> logger
) : IProductAttributeEmbeddingRequestsCreator
{
    private readonly IAttributeRepository _attributeRepository =
        attributeRepository ?? throw new ArgumentNullException(nameof(attributeRepository));

    private readonly IProductAttributeRepository _productAttributeRepository = productAttributeRepository ??
                                                                               throw new ArgumentNullException(
                                                                                   nameof(productAttributeRepository));

    private readonly ILogger<ProductAttributeEmbeddingRequestsCreator> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<AiProcessorGenerateProductAttributeEmbeddingRequest[]> CreateGenerateRequestsAsync(
        IReadOnlyCollection<long> productIds,
        CancellationToken ct = default)
    {
        if (productIds.Count == 0)
            return [];

        IReadOnlyCollection<EnrichedProductAttributeDto> productAttributes =
            await _productAttributeRepository.GetEnrichedProductAttributes(productIds, ct);

        if (productAttributes.Count == 0)
            return [];

        List<long> attributeIds = productAttributes
            .Select(x => x.AttributeId)
            .Distinct()
            .ToList();

        IReadOnlyDictionary<long, Attribute> attributesDict =
            await _attributeRepository.GetAttributeDictionary(attributeIds, ct);

        var requests = new List<AiProcessorGenerateProductAttributeEmbeddingRequest>();

        foreach (var productAttribute in productAttributes)
        {
            Attribute attribute = attributesDict[productAttribute.AttributeId];

            if ((productAttribute.Value?.Data?.Count ?? 0) == 0)
            {
                _logger.LogWarning(
                    $"Product attribute value is empty, " +
                    $"Product id: {productAttribute.ProductId}, " +
                    $"Attribute id: {productAttribute.AttributeId}");
                continue;
            }

            foreach (JsonValueDataItemStructure jsonValueDataItemStructure in productAttribute.Value!.Data!)
            {
                string value = attribute.GetValue(
                    productAttribute.Value,
                    jsonValueDataItemStructure.Locale,
                    jsonValueDataItemStructure.Channel);

                if (string.IsNullOrWhiteSpace(value))
                {
                    if (_logger.IsEnabled(LogLevel.Debug))
                    {
                        _logger.LogDebug($"Product attribute does not contain value, " +
                                         $"Product id: {productAttribute.ProductId}" +
                                         $"Attribute id: {productAttribute.AttributeId}" +
                                         $"Locale: '{jsonValueDataItemStructure.Locale}', " +
                                         $"Channel: '{jsonValueDataItemStructure.Channel}', " +
                                         $"JsonValueStructure: {JsonSerializer.Serialize(productAttribute.Value, JsonSettingsExtensions.Default)}.");
                    }

                    _logger.LogWarning(
                        $"Skipping attribute {productAttribute.AttributeId} for product {productAttribute.ProductId} due to empty value.");

                    continue;
                }

                var request = new AiProcessorGenerateProductAttributeEmbeddingRequest
                {
                    ProductId = productAttribute.ProductId,
                    ProductCode = productAttribute.ProductCode,
                    AttributeId = productAttribute.AttributeId,
                    AttributeCode = productAttribute.AttributeCode,
                    Value = value,
                    OriginalValue = productAttribute.Value,
                    Locale = jsonValueDataItemStructure.Locale,
                    Channel = jsonValueDataItemStructure.Channel
                };

                requests.Add(request);
            }
        }

        return requests.ToArray();
    }

    public async Task<AiProcessorDeleteProductAttributeEmbeddingRequest[]> CreateDeleteRequestsAsync(
        IReadOnlyCollection<long> productIds,
        CancellationToken ct = default)
    {
        if (productIds.Count == 0)
            return [];

        IReadOnlyCollection<ProductAttributeReferenceDto> productAttributes = await _productAttributeRepository
            .GetProductAttributeReferencesByDeletedProducts(productIds, cancellationToken: ct);

        IReadOnlyCollection<ProductAttributeReferenceDto> deletedProductAttributes =
            await _productAttributeRepository.GetDeletedProductAttributeReferences(productIds, cancellationToken: ct);

        if (productAttributes.Count == 0 && deletedProductAttributes.Count == 0)
            return [];

        List<ProductAttributeReferenceDto> uniqueProductAttributes = new List<ProductAttributeReferenceDto>()
            .Concat(productAttributes)
            .Concat(deletedProductAttributes)
            .DistinctBy(x => new { x.ProductId, x.AttributeId })
            .ToList();

        if (uniqueProductAttributes.Count == 0)
            return [];

        AiProcessorDeleteProductAttributeEmbeddingRequest[] requests = uniqueProductAttributes
            .Select(productAttribute => new AiProcessorDeleteProductAttributeEmbeddingRequest
            {
                ProductId = productAttribute.ProductId,
                AttributeId = productAttribute.AttributeId
            })
            .ToArray();

        return requests;
    }
}
