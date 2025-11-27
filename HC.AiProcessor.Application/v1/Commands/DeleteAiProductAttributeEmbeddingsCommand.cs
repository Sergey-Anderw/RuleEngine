using HC.AiProcessor.Infrastructure.Repositories.Ai;
using HC.Packages.AiProcessor.V1.Models;
using HC.Packages.Persistent.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace HC.AiProcessor.Application.v1.Commands;

public record DeleteAiProductAttributeEmbeddingsCommand(
    IReadOnlyCollection<AiProcessorDeleteProductAttributeEmbeddingRequest> Requests
) : IRequest<DeleteAiProductAttributeEmbeddingsCommandResult>;

public record DeleteAiProductAttributeEmbeddingsCommandResult(
    AiProcessorDeleteProductAttributeEmbeddingsResponse Response
);

public class DeleteAiProductAttributeEmbeddingsCommandHandler(
    IServiceProvider serviceProvider) :
    IRequestHandler<DeleteAiProductAttributeEmbeddingsCommand, DeleteAiProductAttributeEmbeddingsCommandResult>
{
    private const int ChunkSize = 100;

    private readonly IServiceProvider _serviceProvider =
        serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

    public async Task<DeleteAiProductAttributeEmbeddingsCommandResult> Handle(
        DeleteAiProductAttributeEmbeddingsCommand command,
        CancellationToken ct)
    {
        if ((command.Requests?.Count ?? 0) == 0)
            throw new ArgumentException("At least one element is required", nameof(command));

        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        using var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var repository = scope.ServiceProvider.GetRequiredService<IAiProductAttributeEmbeddingRepository>();

        var totalDeletedCount = 0;

        IEnumerable<AiProcessorDeleteProductAttributeEmbeddingRequest[]> requestsChunks =
            command.Requests!.Chunk(ChunkSize);

        foreach (AiProcessorDeleteProductAttributeEmbeddingRequest[] requests in requestsChunks)
        {
            IReadOnlyCollection<long> ids = await repository.GetIds(
                queries: requests
                    .Select(x => new AiProductAttributeEmbeddingQuery(x.ProductId, x.AttributeId, x.Locale, x.Channel)),
                ct);

            if (ids.Count == 0)
                continue;

            int deletedCount = await repository.DeleteImmediately(ids, ct);

            totalDeletedCount += deletedCount;
        }

        return new DeleteAiProductAttributeEmbeddingsCommandResult(
            Response: new AiProcessorDeleteProductAttributeEmbeddingsResponse
            {
                TotalCount = totalDeletedCount
            });
    }
}
