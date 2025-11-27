using HC.AiProcessor.Application.Services;
using HC.AiProcessor.Infrastructure.Repositories.Ai;
using HC.Packages.AiProcessor.V1.Models;
using HC.Packages.Persistent.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Pgvector;

namespace HC.AiProcessor.Application.v1.Commands;

public record DetermineProductFamilyCommand(
    AiProcessorDetermineProductFamilyRequest Request
) : IRequest<DetermineProductFamilyCommandResult>;

public record DetermineProductFamilyCommandResult(AiProcessorDetermineProductFamilyResponse? Response);

public class DetermineProductFamiliesCommandHandler(
    IServiceProvider serviceProvider,
    IAiEmbeddingService embeddingService)
    : IRequestHandler<DetermineProductFamilyCommand, DetermineProductFamilyCommandResult>
{
    private readonly IServiceProvider _serviceProvider =
        serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

    private readonly IAiEmbeddingService _embeddingService =
        embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));

    public async Task<DetermineProductFamilyCommandResult> Handle(
        DetermineProductFamilyCommand command,
        CancellationToken ct)
    {
        AiProcessorDetermineProductFamilyRequest request = command.Request;

        List<string> texts = request.Criteria.Values.Select(x => x.Value).ToList();
        IList<Vector> embeddings = await _embeddingService.EmbeddingsAsync(request.ClientId, texts, ct);

        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        using var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var repository = scope.ServiceProvider.GetRequiredService<IAiProductAttributeEmbeddingRepository>();

        AttributeToFamilySimilarityDto[][] matrix =
            await repository.GetAttributeToFamilySimilarityMatrix(
                request.ClientId,
                request.TargetProductId,
                queries: request.Criteria.Keys.Select((attributeId, index) =>
                {
                    AiProcessorDetermineProductFamilyRequest.AttributeSimilarityCriterion criterion =
                        request.Criteria[attributeId];

                    return new AttributeToFamilySimilarityQuery(
                        attributeId,
                        embeddings[index],
                        criterion.MinSimilarity,
                        criterion.MaxSimilarity);
                }),
                request.Locale,
                request.Channel,
                ct);

        if (matrix.Length == 0)
            return new DetermineProductFamilyCommandResult(Response: null);

        if (matrix.Length == 1)
        {
            AttributeToFamilySimilarityDto bestResult = matrix[0][0];

            return new DetermineProductFamilyCommandResult(
                Response: new AiProcessorDetermineProductFamilyResponse
                {
                    FamilyId = bestResult.FamilyId,
                    Similarities = new Dictionary<long, double>
                        { { bestResult.AttributeId, bestResult.CosineSimilarity } }
                });
        }

        List<AttributeToFamilySimilarityDto> rows = matrix.SelectMany(x => x.ToList()).ToList();

        List<HashSet<long>> familySetsPerAttribute = rows
            .GroupBy(row => row.AttributeId)
            .Select(g => new HashSet<long>(g.Select(row => row.FamilyId)))
            .ToList();

        if (familySetsPerAttribute.Count == 0)
            return new DetermineProductFamilyCommandResult(Response: null);

        HashSet<long> intersection = familySetsPerAttribute
            .Skip(1)
            .Aggregate(
                new HashSet<long>(familySetsPerAttribute.First()),
                (currentIntersection, nextSet) =>
                {
                    currentIntersection.IntersectWith(nextSet);
                    return currentIntersection;
                }
            );

        IEnumerable<IGrouping<long, AttributeToFamilySimilarityDto>> groups = matrix
            .SelectMany(x => x.Where(i => intersection.Contains(i.FamilyId)))
            .GroupBy(x => x.FamilyId);

        IGrouping<long, AttributeToFamilySimilarityDto> bestResults =
            groups.MaxBy(x => x.Average(i => i.CosineSimilarity))!;

        return new DetermineProductFamilyCommandResult(
            Response: new AiProcessorDetermineProductFamilyResponse
            {
                FamilyId = bestResults.Key,
                Similarities = bestResults.ToDictionary(x => x.AttributeId, x => x.CosineSimilarity)
            });
    }
}
