using HC.AiProcessor.Entity.Ai;
using HC.AiProcessor.Entity.Ai.Enums;
using HC.AiProcessor.Infrastructure.Repositories.Ai;
using HC.Packages.AiProcessor.V1.Models;
using HC.Packages.Persistent.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HC.AiProcessor.Application.v1.Commands;

public record RefreshAiProductCommand(
    IReadOnlyCollection<AiProcessorRefreshProductRequest> Requests
) : IRequest<RefreshAiProductCommandResult>;

public record RefreshAiProductCommandResult(
    AiProcessorRefreshProductsResponse Response
);

public class RefreshAiProductCommandHandler(
    IServiceProvider serviceProvider,
    ILogger<RefreshAiProductCommandHandler> logger
) : IRequestHandler<RefreshAiProductCommand, RefreshAiProductCommandResult>
{
    private const int ChunkSize = 100;

    private readonly IServiceProvider _serviceProvider =
        serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

    private readonly ILogger<RefreshAiProductCommandHandler> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<RefreshAiProductCommandResult> Handle(RefreshAiProductCommand command, CancellationToken ct)
    {
        if ((command.Requests?.Count ?? 0) == 0)
            throw new ArgumentException("At least one element is required", nameof(command));

        Dictionary<long, List<AiProcessorRefreshProductRequest>> groups = command.Requests!
            .GroupBy(x => x.ProductId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (KeyValuePair<long, List<AiProcessorRefreshProductRequest>> group in groups)
        {
            if (group.Value.Count <= 1)
                continue;

            AiProcessorRefreshProductRequest x = group.Value[0];
            throw new Exception($"Duplicate requests found, " +
                                $"product id: {x.ProductId}.");
        }

        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        using var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var aiProductRepository = scope.ServiceProvider.GetRequiredService<IAiProductRepository>();

        var totalCreatedCount = 0;
        var totalUpdatedCount = 0;
        var totalSkippedCount = 0;

        IEnumerable<AiProcessorRefreshProductRequest[]> requestsChunks = command.Requests!.Chunk(ChunkSize);

        foreach (AiProcessorRefreshProductRequest[] requests in requestsChunks)
        {
            IReadOnlyDictionary<long, AiProduct> entityDict =
                await aiProductRepository.GetDictionary(originalIds: requests.Select(x => x.ProductId), ct);

            foreach (AiProcessorRefreshProductRequest request in requests)
            {
                if (entityDict.TryGetValue(request.ProductId, out AiProduct? entity) &&
                    entity.ClientId == request.ClientId &&
                    entity.Code == request.ProductCode &&
                    entity.ExternalId == request.ProductExternalId &&
                    entity.Status == (AiProductStatusEnum) request.ProductStatus &&
                    entity.FamilyId == request.ProductFamilyId)
                {
                    _logger.LogDebug(
                        $"Skipping product {request.ProductId} because the existing value is identical.");

                    totalSkippedCount++;
                    continue;
                }

                if (entity is null)
                {
                    entity = new AiProduct
                    {
                        OriginalId = request.ProductId,
                        ClientId = request.ClientId,
                        Code = request.ProductCode,
                        ExternalId = request.ProductExternalId,
                        Status = (AiProductStatusEnum) request.ProductStatus,
                        FamilyId = request.ProductFamilyId
                    };

                    await aiProductRepository.Create(entity, ct);

                    totalCreatedCount++;
                }
                else
                {
                    entity.OriginalId = request.ProductId;
                    entity.ClientId = request.ClientId;
                    entity.Code = request.ProductCode;
                    entity.ExternalId = request.ProductExternalId;
                    entity.Status = (AiProductStatusEnum) request.ProductStatus;
                    entity.FamilyId = request.ProductFamilyId;

                    await aiProductRepository.Update(entity, ct);

                    totalUpdatedCount++;
                }
            }
        }

        if (totalCreatedCount + totalUpdatedCount != 0)
        {
            await uow.BulkSaveChangesAsync(ct);
        }

        return new RefreshAiProductCommandResult(
            Response: new AiProcessorRefreshProductsResponse
            {
                TotalCreatedCount = totalCreatedCount,
                TotalUpdatedCount = totalUpdatedCount,
                TotalSkippedCount = totalSkippedCount
            });
    }
}
