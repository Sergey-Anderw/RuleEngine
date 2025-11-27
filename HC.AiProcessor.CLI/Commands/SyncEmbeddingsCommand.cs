using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Dapper;
using HC.AiProcessor.Application.Extensions;
using HC.AiProcessor.Application.v1.Commands;
using HC.AiProcessor.Entity.Catalog;
using HC.AiProcessor.Infrastructure;
using HC.Packages.AiProcessor.V1.Models;
using HC.Packages.Persistent.Entities;
using McMaster.Extensions.CommandLineUtils;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using ShellProgressBar;
using Attribute = HC.AiProcessor.Entity.Catalog.Attribute;

namespace HC.AiProcessor.CLI.Commands;

[Command(CommandName)]
internal sealed class SyncEmbeddingsCommand(
    IServiceProvider serviceProvider,
    IMediator mediator,
    ILogger<SyncEmbeddingsCommand> logger)
{
    public const string CommandName = "sync";

    private readonly IServiceProvider _serviceProvider =
        serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

    private readonly IMediator _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));

    private readonly ILogger<SyncEmbeddingsCommand> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    #region Parameters

    [Option(
        template: "-c|--client-id",
        optionType: CommandOptionType.SingleValue,
        Description = "Specifies the client identifier for which embeddings should be generated.")]
    [Required]
    [Range(1, long.MaxValue)]
    // ReSharper disable once UnassignedGetOnlyAutoProperty
    public long ClientId { get; }

    [Option(
        template: "-f|--family",
        optionType: CommandOptionType.MultipleValue,
        Description =
            "Optional. Filters products by family id to generate embeddings for specific product families.")]
    [Range(1, long.MaxValue)]
    // ReSharper disable once UnassignedGetOnlyAutoProperty
    public IReadOnlyCollection<long?> FamilyIds { get; } = [];

    [Option(
        template: "-l|--locale",
        optionType: CommandOptionType.SingleValue,
        Description = "Optional. Defines the locale to be used when retrieving attribute values.")]
    // ReSharper disable once UnassignedGetOnlyAutoProperty
    public string? Locale { get; }

    [Option(
        template: "-C|--channel",
        optionType: CommandOptionType.SingleValue,
        Description = "Optional. Specifies the channel to be used when retrieving attribute values.")]
    // ReSharper disable once UnassignedGetOnlyAutoProperty
    public string? Channel { get; }

    [Option(
        template: "-s|--batch-size",
        optionType: CommandOptionType.SingleValue,
        Description = "Defines the number of product attributes to process per batch.")]
    [Range(1, 1000)]
    public int BatchSize { get; } = 100;

    #endregion

    [Experimental("SKEXP0001")]
    private async Task<int> OnExecuteAsync(CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();

        Console.WriteLine("Loading...");

        (int totalProductAttributesCount, int totalDeletedProductAttributesCount) =
            await DoScopedWork(async scope =>
            {
                await using var dbContext = scope.ServiceProvider.GetRequiredService<AiProcessorDbContext>();
                return (
                    await GetProductAttributesCountAsync(dbContext, ClientId, FamilyIds, ct),
                    await GetDeletedProductAttributesCountAsync(dbContext, ClientId, FamilyIds, ct)
                );
            });

        int take = Math.Max(1, BatchSize);

        var totalCreatedEmbeddingsCount = 0;
        var totalUpdatedEmbeddingsCount = 0;
        var totalSkippedProductAttributesCount = 0;
        var totalDeletedEmbeddingsCount = 0;

        Console.Clear();

        using (var progressBar = new ProgressBar(
                   maxTicks: totalProductAttributesCount + totalDeletedProductAttributesCount,
                   message: "Synchronizing embeddings...",
                   options: new ProgressBarOptions
                   {
                       ProgressBarOnBottom = true
                   }))
        {
            #region Generate

            bool isLocaleDefined = !string.IsNullOrWhiteSpace(Locale);
            bool isChannelDefined = !string.IsNullOrWhiteSpace(Channel);
            var skip = 0;

            while (!ct.IsCancellationRequested)
            {
                await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
                await using var dbContext = scope.ServiceProvider.GetRequiredService<AiProcessorDbContext>();

                List<ProductAttributeDto> productAttributes =
                    await GetProductAttributesAsync(dbContext, ClientId, FamilyIds, skip, take, ct);

                if (productAttributes.Count == 0)
                    break;

                List<long> attributeIds = productAttributes
                    .Select(x => x.AttributeId)
                    .Distinct()
                    .ToList();

                Dictionary<long, Attribute> attributesDict =
                    await GetAttributesDictAsync(dbContext, attributeIds, ct);

                var requests = new List<AiProcessorGenerateProductAttributeEmbeddingRequest>();

                foreach (ProductAttributeDto productAttribute in productAttributes)
                {
                    Attribute attribute = attributesDict[productAttribute.AttributeId];

                    if (isLocaleDefined && !attribute.ValuePerLocale)
                    {
                        _logger.LogWarning(
                            $"Attribute {attribute.Id} does not support per-locale values, but locale '{Locale}' was provided.");
                    }

                    if (isChannelDefined && !attribute.ValuePerChannel)
                    {
                        _logger.LogWarning(
                            $"Attribute {attribute.Id} does not support per-channel values, but channel '{Channel}' was provided.");
                    }

                    string value = attribute.GetValue(productAttribute.Value, Locale, Channel);

                    var request = new AiProcessorGenerateProductAttributeEmbeddingRequest
                    {
                        ClientId = ClientId,
                        ProductId = productAttribute.ProductId,
                        ProductCode = productAttribute.ProductCode,
                        AttributeId = productAttribute.AttributeId,
                        AttributeCode = productAttribute.AttributeCode,
                        Value = value,
                        OriginalValue = productAttribute.Value,
                        Locale = Locale,
                        Channel = Channel
                    };

                    requests.Add(request);

                    progressBar.Tick();
                }

                GenerateAiProductAttributeEmbeddingsCommandResult result = await _mediator.Send(
                    new GenerateAiProductAttributeEmbeddingsCommand(requests, CachingEnabled: true),
                    ct);

                totalCreatedEmbeddingsCount += result.Response.TotalCreatedCount;
                totalUpdatedEmbeddingsCount += result.Response.TotalUpdatedCount;
                totalSkippedProductAttributesCount += result.Response.TotalSkippedCount;

                skip += productAttributes.Count;
            }

            #endregion

            #region Delete

            var deletionSkip = 0;

            while (!ct.IsCancellationRequested)
            {
                await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
                await using var dbContext = scope.ServiceProvider.GetRequiredService<AiProcessorDbContext>();

                List<DeletedProductAttributeDto> deletedProductAttributes =
                    await GetDeletedProductAttributesAsync(dbContext, ClientId, FamilyIds, deletionSkip, take, ct);

                if (deletedProductAttributes.Count == 0)
                    break;

                var requests = new List<AiProcessorDeleteProductAttributeEmbeddingRequest>();

                foreach (DeletedProductAttributeDto deletedProductAttribute in deletedProductAttributes)
                {
                    var request = new AiProcessorDeleteProductAttributeEmbeddingRequest
                    {
                        ProductId = deletedProductAttribute.ProductId,
                        AttributeId = deletedProductAttribute.AttributeId,
                        Locale = Locale,
                        Channel = Channel
                    };

                    requests.Add(request);

                    progressBar.Tick();
                }

                DeleteAiProductAttributeEmbeddingsCommandResult result =
                    await _mediator.Send(new DeleteAiProductAttributeEmbeddingsCommand(requests), ct);

                totalDeletedEmbeddingsCount += result.Response.TotalCount;

                deletionSkip += deletedProductAttributes.Count;
            }

            #endregion
        }

        Console.Clear();

        foreach (string message in new[]
                 {
                     $"Embeddings have been synchronized in {stopwatch.ElapsedMilliseconds} ms",
                     $"Total created embeddings count: {totalCreatedEmbeddingsCount}",
                     $"Total updated embeddings count: {totalUpdatedEmbeddingsCount}",
                     $"Total deleted embeddings count: {totalDeletedEmbeddingsCount}",
                     $"Total skipped product attributes count: {totalSkippedProductAttributesCount}",
                     $"Total product attributes count: {totalProductAttributesCount}",
                     $"Total deleted product attributes count: {totalDeletedProductAttributesCount}"
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

    private static async Task<List<ProductAttributeDto>> GetProductAttributesAsync(
        AiProcessorDbContext dbContext,
        long clientId,
        IReadOnlyCollection<long?> familyIds,
        int skip,
        int take,
        CancellationToken ct = default)
    {
        IQueryable<ProductAttributeDto> query = GetProductAttributesQuery(dbContext, clientId, familyIds)
            .Select(x => new ProductAttributeDto
            {
                ProductId = x.ProductId,
                ProductCode = x.Product.Code,
                AttributeId = x.Attribute.Id,
                AttributeCode = x.Attribute.Code,
                Value = x.Value
            });

        if (skip > 0)
            query = query.Skip(skip);

        if (take > 0)
            query = query.Take(take);

        List<ProductAttributeDto> result = await query.ToListAsync(ct);
        return result;
    }

    private static async Task<int> GetProductAttributesCountAsync(
        AiProcessorDbContext dbContext,
        long clientId,
        IReadOnlyCollection<long?> familyIds,
        CancellationToken ct = default)
    {
        IQueryable<ProductAttribute> query = GetProductAttributesQuery(dbContext, clientId, familyIds);

        int result = await query.CountAsync(ct);
        return result;
    }

    private static IQueryable<ProductAttribute> GetProductAttributesQuery(
        AiProcessorDbContext dbContext,
        long clientId,
        IReadOnlyCollection<long?> familyIds)
    {
        IQueryable<ProductAttribute> query = dbContext.ProductAttributes
            .AsNoTracking()
            .Where(x => x.Product.ClientId == clientId);

        if (familyIds.Count != 0)
            query = query.Where(x => x.Product.FamilyId != null && familyIds.Contains(x.Product.FamilyId));

        query = query.OrderBy(x => x.Id);

        return query;
    }

    private static async Task<Dictionary<long, Attribute>> GetAttributesDictAsync(
        AiProcessorDbContext dbContext,
        IReadOnlyCollection<long> attributeIds,
        CancellationToken ct = default)
    {
        IQueryable<Attribute> query = dbContext.Attributes
            .AsNoTracking()
            .Where(x => attributeIds.Contains(x.Id));

        Dictionary<long, Attribute> result = await query.ToDictionaryAsync(x => x.Id, x => x, ct);
        return result;
    }

    private static async Task<int> GetDeletedProductAttributesCountAsync(
        AiProcessorDbContext dbContext,
        long clientId,
        IReadOnlyCollection<long?> familyIds,
        CancellationToken ct = default)
    {
        const string sql =
            """
            SELECT count(*)
            FROM catalog.product_attributes pa
            JOIN catalog.products p ON pa.product_id = p.id
            WHERE pa.deleted_at IS NOT NULL
              AND p.client_id = @clientId
              AND (array_length(@familyIds, 1) IS NULL OR p.family_id = ANY(@familyIds))
              AND NOT EXISTS (
                SELECT 1
                FROM catalog.product_attributes pa_active
                WHERE pa_active.product_id = pa.product_id
                  AND pa_active.attribute_id = pa.attribute_id
                  AND pa_active.deleted_at IS NULL
              );
            """;

        await using var connection = new NpgsqlConnection(dbContext.Database.GetConnectionString());

        var count = await connection.QueryFirstAsync<int>(
            command: new CommandDefinition(sql, new { clientId, familyIds }, cancellationToken: ct));

        return count;
    }

    private static async Task<List<DeletedProductAttributeDto>> GetDeletedProductAttributesAsync(
        AiProcessorDbContext dbContext,
        long clientId,
        IReadOnlyCollection<long?> familyIds,
        int skip,
        int take,
        CancellationToken ct = default)
    {
        const string sql =
            """
            SELECT pa.product_id as productId, pa.attribute_id as attributeId, pa.id
            FROM catalog.product_attributes pa
            JOIN catalog.products p ON pa.product_id = p.id
            WHERE pa.deleted_at IS NOT NULL
              AND p.client_id = @clientId
              AND (array_length(@familyIds, 1) IS NULL OR p.family_id = ANY(@familyIds))
              AND NOT EXISTS (
                SELECT 1
                FROM catalog.product_attributes pa_active
                WHERE pa_active.product_id = pa.product_id
                  AND pa_active.attribute_id = pa.attribute_id
                  AND pa_active.deleted_at IS NULL
              )
            ORDER BY pa.id
            LIMIT @take
            OFFSET @skip;
            """;

        await using var connection = new NpgsqlConnection(dbContext.Database.GetConnectionString());

        IEnumerable<DeletedProductAttributeDto> results = await connection.QueryAsync<DeletedProductAttributeDto>(
            command: new CommandDefinition(sql, new { clientId, familyIds, take, skip }, cancellationToken: ct));

        return results.ToList();
    }

    private sealed class ProductAttributeDto
    {
        public required long ProductId { get; init; }
        public required string ProductCode { get; init; }
        public required long AttributeId { get; init; }
        public required string AttributeCode { get; init; }
        public required JsonValueStructure Value { get; init; }
    }

    private sealed class DeletedProductAttributeDto
    {
        public required long ProductId { get; init; }
        public required long AttributeId { get; init; }
    }
}
