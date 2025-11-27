using HC.AiProcessor.Infrastructure.Repositories.Catalog;
using HC.Packages.AiProcessor.V1.Models;
using HC.Packages.Persistent.Infrastructure;

namespace HC.AiProcessor.Worker.Services;

public interface IProductRequestsCreator
{
    Task<AiProcessorRefreshProductRequest[]> CreateRefreshRequestsAsync(
        IReadOnlyCollection<long> productIds,
        CancellationToken ct = default);

    Task<AiProcessorDeleteProductRequest[]> CreateDeleteRequestsAsync(
        IReadOnlyCollection<long> productIds,
        CancellationToken ct = default);
}

internal sealed class ProductRequestsCreator(
    IServiceProvider serviceProvider
) : IProductRequestsCreator
{
    private readonly IServiceProvider _serviceProvider =
        serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

    public async Task<AiProcessorRefreshProductRequest[]> CreateRefreshRequestsAsync(
        IReadOnlyCollection<long> productIds,
        CancellationToken ct = default)
    {
        if (productIds.Count == 0)
            return [];

        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        using var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var repository = scope.ServiceProvider.GetRequiredService<IProductRepository>();

        IReadOnlyCollection<EnrichedProductDto> products = await repository.GetEnrichedProducts(productIds, ct);

        AiProcessorRefreshProductRequest[] requests = products
            .Select(x => new AiProcessorRefreshProductRequest
            {
                ClientId = x.ClientId,
                ProductId = x.Id,
                ProductCode = x.Code,
                ProductExternalId = x.ExternalId,
                ProductStatus = (AiProcessorRefreshProductRequest.ProductStatusEnum) x.Status,
                ProductFamilyId = x.FamilyId
            })
            .ToArray();

        return requests;
    }

    public Task<AiProcessorDeleteProductRequest[]> CreateDeleteRequestsAsync(
        IReadOnlyCollection<long> productIds,
        CancellationToken ct = default)
    {
        if (productIds.Count == 0)
            return Task.FromResult<AiProcessorDeleteProductRequest[]>([]);

        return Task.FromResult<AiProcessorDeleteProductRequest[]>([
            new AiProcessorDeleteProductRequest { ProductIds = productIds }
        ]);
    }
}
