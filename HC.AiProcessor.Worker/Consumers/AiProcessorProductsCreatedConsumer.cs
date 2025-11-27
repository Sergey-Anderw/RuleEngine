using HC.AiProcessor.Worker.Queue;
using HC.AiProcessor.Worker.Queue.Handlers;
using HC.AiProcessor.Worker.Services;
using HC.Packages.AiProcessor.V1.Models;
using HC.Packages.Contracts.V1.Commands;
using MassTransit;

namespace HC.AiProcessor.Worker.Consumers;

internal sealed class AiProcessorProductsCreatedConsumer(
    IServiceProvider serviceProvider,
    IAiProcessorQueue queue)
    : IConsumer<IProductsCreatedCommand>
{
    private readonly IServiceProvider _serviceProvider =
        serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

    private readonly IAiProcessorQueue _queue = queue ?? throw new ArgumentNullException(nameof(queue));

    public async Task Consume(ConsumeContext<IProductsCreatedCommand> context)
    {
        var productIds = context.Message.ProductIds;
        if (productIds.Count == 0)
            return;

        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        var productRequestsCreator = scope.ServiceProvider.GetRequiredService<IProductRequestsCreator>();
        var productAttributeEmbeddingRequestsCreator =
            scope.ServiceProvider.GetRequiredService<IProductAttributeEmbeddingRequestsCreator>();

        var messages = new List<IAiProcessorQueueMessage>();

        #region Refresh AI products

        AiProcessorRefreshProductRequest[] refreshAiProductRequests =
            await productRequestsCreator.CreateRefreshRequestsAsync(productIds, context.CancellationToken);

        if (refreshAiProductRequests.Length != 0)
        {
            messages.Add(new RefreshAiProductsMessage
            {
                Requests = refreshAiProductRequests
            });
        }

        #endregion

        #region Generate AI product attribute embeddings

        AiProcessorGenerateProductAttributeEmbeddingRequest[] generateAiProductAttributeEmbeddingRequests =
            await productAttributeEmbeddingRequestsCreator
                .CreateGenerateRequestsAsync(productIds, context.CancellationToken);

        if (generateAiProductAttributeEmbeddingRequests.Length != 0)
        {
            messages.Add(new GenerateAiProductAttributeEmbeddingsMessage
            {
                Requests = generateAiProductAttributeEmbeddingRequests
            });
        }

        #endregion

        if (messages.Count == 0)
            return;

        await _queue.EnqueueMessagesAsync(messages, context.CancellationToken);
    }
}
