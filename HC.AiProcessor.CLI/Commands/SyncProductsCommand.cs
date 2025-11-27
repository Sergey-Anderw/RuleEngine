using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using Dapper;
using HC.AiProcessor.Application.v1.Commands;
using HC.AiProcessor.Entity.Catalog;
using HC.AiProcessor.Entity.Catalog.Enums;
using HC.AiProcessor.Infrastructure;
using HC.Packages.AiProcessor.V1.Models;
using McMaster.Extensions.CommandLineUtils;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using ShellProgressBar;

namespace HC.AiProcessor.CLI.Commands;

[Command(CommandName)]
internal sealed class SyncProductsCommand(
    IServiceProvider serviceProvider,
    IMediator mediator,
    ILogger<SyncProductsCommand> logger)
{
    public const string CommandName = "sync";

    private readonly IServiceProvider _serviceProvider =
        serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

    private readonly IMediator _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));

    private readonly ILogger<SyncProductsCommand> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    #region Parameters

    [Option(
        template: "-c|--client-id",
        optionType: CommandOptionType.SingleValue,
        Description = "")]
    [Required]
    [Range(1, long.MaxValue)]
    // ReSharper disable once UnassignedGetOnlyAutoProperty
    public long ClientId { get; }

    [Option(
        template: "-f|--family",
        optionType: CommandOptionType.MultipleValue,
        Description = "")]
    [Range(1, long.MaxValue)]
    // ReSharper disable once UnassignedGetOnlyAutoProperty
    public IReadOnlyCollection<long?> FamilyIds { get; } = [];

    [Option(
        template: "-s|--batch-size",
        optionType: CommandOptionType.SingleValue,
        Description = "")]
    [Range(1, 1000)]
    public int BatchSize { get; } = 100;

    #endregion

    private async Task<int> OnExecuteAsync(CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();

        Console.WriteLine("Loading...");

        (int totalProductsCount, int totalDeletedProductsCount) = await DoScopedWork(async scope =>
        {
            await using var dbContext = scope.ServiceProvider.GetRequiredService<AiProcessorDbContext>();
            return (
                await GetProductsCountAsync(dbContext, ClientId, FamilyIds, ct),
                await GetDeletedProductsCountAsync(dbContext, ClientId, FamilyIds, ct)
            );
        });

        int take = Math.Max(1, BatchSize);

        var totalCreatedAiProductsCount = 0;
        var totalUpdatedAiProductsCount = 0;
        var totalSkippedProductsCount = 0;
        var totalDeletedAiProductsCount = 0;

        Console.Clear();

        using (var progressBar = new ProgressBar(
                   maxTicks: totalProductsCount + totalDeletedProductsCount,
                   message: "Synchronizing products...",
                   options: new ProgressBarOptions
                   {
                       ProgressBarOnBottom = true
                   }))
        {
            #region Refresh

            var skip = 0;

            while (!ct.IsCancellationRequested)
            {
                await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
                await using var dbContext = scope.ServiceProvider.GetRequiredService<AiProcessorDbContext>();

                List<ProductDto> products = await GetProductsAsync(dbContext, ClientId, FamilyIds, skip, take, ct);

                if (products.Count == 0)
                    break;

                var requests = new List<AiProcessorRefreshProductRequest>();

                foreach (ProductDto product in products)
                {
                    var request = new AiProcessorRefreshProductRequest
                    {
                        ProductId = product.Id,
                        ClientId = product.ClientId,
                        ProductCode = product.Code,
                        ProductExternalId = product.ExternalId,
                        ProductStatus = (AiProcessorRefreshProductRequest.ProductStatusEnum) product.Status,
                        ProductFamilyId = product.FamilyId
                    };

                    requests.Add(request);

                    progressBar.Tick();
                }

                RefreshAiProductCommandResult result = await _mediator.Send(new RefreshAiProductCommand(requests), ct);

                totalCreatedAiProductsCount += result.Response.TotalCreatedCount;
                totalUpdatedAiProductsCount += result.Response.TotalUpdatedCount;
                totalSkippedProductsCount += result.Response.TotalSkippedCount;

                skip += products.Count;
            }

            #endregion

            #region Delete

            var deletionSkip = 0;

            while (!ct.IsCancellationRequested)
            {
                await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
                await using var dbContext = scope.ServiceProvider.GetRequiredService<AiProcessorDbContext>();

                List<long> deletedProductIds =
                    await GetDeletedProductIdsAsync(dbContext, ClientId, FamilyIds, deletionSkip, take, ct);

                if (deletedProductIds.Count == 0)
                    break;

                var request = new AiProcessorDeleteProductRequest
                {
                    ProductIds = deletedProductIds
                };

                foreach (long _ in deletedProductIds)
                {
                    progressBar.Tick();
                }

                DeleteAiProductCommandResult result =
                    await _mediator.Send(new DeleteAiProductCommand([request]), ct);

                totalDeletedAiProductsCount += result.Response.TotalCount;

                deletionSkip += deletedProductIds.Count;
            }

            #endregion
        }

        Console.Clear();

        foreach (string message in new[]
                 {
                     $"Products have been synchronized in {stopwatch.ElapsedMilliseconds} ms",
                     $"Total created AI products count: {totalCreatedAiProductsCount}",
                     $"Total updated AI products count: {totalUpdatedAiProductsCount}",
                     $"Total deleted AI products count: {totalDeletedAiProductsCount}",
                     $"Total skipped products count: {totalSkippedProductsCount}",
                     $"Total products count: {totalProductsCount}",
                     $"Total deleted products count: {totalDeletedProductsCount}"
                 })
        {
            Console.WriteLine(message);
            _logger.LogInformation(message);
        }

        return AppConstants.SuccessCode;
    }

    private async Task<T> DoScopedWork<T>(Func<IServiceScope, Task<T>> action)
    {
        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();

        T result = await action(scope);
        return result;
    }

    private static IQueryable<Product> GetProductsQuery(
        AiProcessorDbContext dbContext,
        long clientId,
        IReadOnlyCollection<long?> familyIds)
    {
        IQueryable<Product> query = dbContext.Products
            .AsNoTracking()
            .Where(x => x.ClientId == clientId);

        if (familyIds.Count != 0)
            query = query.Where(x => x.FamilyId != null && familyIds.Contains(x.FamilyId));

        query = query.OrderBy(x => x.Id);

        return query;
    }

    private static async Task<int> GetProductsCountAsync(
        AiProcessorDbContext dbContext,
        long clientId,
        IReadOnlyCollection<long?> familyIds,
        CancellationToken ct = default)
    {
        IQueryable<Product> query = GetProductsQuery(dbContext, clientId, familyIds);

        int result = await query.CountAsync(ct);
        return result;
    }

    private static async Task<List<ProductDto>> GetProductsAsync(
        AiProcessorDbContext dbContext,
        long clientId,
        IReadOnlyCollection<long?> familyIds,
        int skip,
        int take,
        CancellationToken ct = default)
    {
        IQueryable<ProductDto> query = GetProductsQuery(dbContext, clientId, familyIds)
            .Select(x => new ProductDto
            {
                Id = x.Id,
                ClientId = x.ClientId,
                Code = x.Code,
                ExternalId = x.ExternalId,
                Status = x.Status,
                FamilyId = x.FamilyId
            });

        if (skip > 0)
            query = query.Skip(skip);

        if (take > 0)
            query = query.Take(take);

        List<ProductDto> result = await query.ToListAsync(ct);
        return result;
    }

    private static async Task<int> GetDeletedProductsCountAsync(
        AiProcessorDbContext dbContext,
        long clientId,
        IReadOnlyCollection<long?> familyIds,
        CancellationToken ct = default)
    {
        const string sql =
            """
            SELECT count(*)
            FROM catalog.products p
            WHERE p.deleted_at IS NOT NULL
              AND p.client_id = @clientId
              AND (array_length(@familyIds, 1) IS NULL OR p.family_id = ANY(@familyIds));
            """;

        await using var connection = new NpgsqlConnection(dbContext.Database.GetConnectionString());

        var count = await connection.QueryFirstAsync<int>(
            command: new CommandDefinition(sql, new { clientId, familyIds }, cancellationToken: ct));

        return count;
    }

    private static async Task<List<long>> GetDeletedProductIdsAsync(
        AiProcessorDbContext dbContext,
        long clientId,
        IReadOnlyCollection<long?> familyIds,
        int skip,
        int take,
        CancellationToken ct = default)
    {
        const string sql =
            """
            SELECT p.id
            FROM catalog.products p
            WHERE p.deleted_at IS NOT NULL
              AND p.client_id = @clientId
              AND (array_length(@familyIds, 1) IS NULL OR p.family_id = ANY(@familyIds))
            ORDER BY p.id
            LIMIT @take
            OFFSET @skip;
            """;

        await using var connection = new NpgsqlConnection(dbContext.Database.GetConnectionString());

        IEnumerable<long> results = await connection.QueryAsync<long>(
            command: new CommandDefinition(sql, new { clientId, familyIds, take, skip }, cancellationToken: ct));

        return results.ToList();
    }

    private sealed class ProductDto
    {
        public required long Id { get; init; }
        public required long ClientId { get; init; }
        public required string Code { get; init; }
        public required string ExternalId { get; init; }
        public required ProductStatusEnum Status { get; init; }
        public required long? FamilyId { get; init; }
    }
}
