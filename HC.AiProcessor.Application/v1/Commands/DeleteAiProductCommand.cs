using HC.AiProcessor.Infrastructure.Repositories.Ai;
using HC.Packages.AiProcessor.V1.Models;
using HC.Packages.Persistent.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace HC.AiProcessor.Application.v1.Commands;

public record DeleteAiProductCommand(
    IReadOnlyCollection<AiProcessorDeleteProductRequest> Requests
) : IRequest<DeleteAiProductCommandResult>;

public record DeleteAiProductCommandResult(AiProcessorDeleteProductsResponse Response);

public class DeleteAiProductCommandHandler(
    IServiceProvider serviceProvider
) : IRequestHandler<DeleteAiProductCommand, DeleteAiProductCommandResult>
{
    private readonly IServiceProvider _serviceProvider =
        serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

    public async Task<DeleteAiProductCommandResult> Handle(DeleteAiProductCommand command, CancellationToken ct)
    {
        if ((command.Requests?.Count ?? 0) == 0)
            throw new ArgumentException("At least one element is required", nameof(command));

        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        using var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var repository = scope.ServiceProvider.GetRequiredService<IAiProductRepository>();

        IEnumerable<long> originalIds = command.Requests!
            .SelectMany(x => x.ProductIds)
            .Distinct();

        int totalDeletedCount = await repository.DeleteImmediately(originalIds, ct);

        return new DeleteAiProductCommandResult(
            Response: new AiProcessorDeleteProductsResponse
            {
                TotalCount = totalDeletedCount
            });
    }
}
