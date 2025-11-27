using HC.AiProcessor.Worker.Queue;
using HC.AiProcessor.Worker.Queue.Handlers;
using HC.AiProcessor.Worker.Services;
using HC.Packages.AiProcessor.V1.Models;
using HC.Packages.Contracts.V1.Commands;
using MassTransit;

namespace HC.AiProcessor.Worker.Consumers;

internal sealed class AiProcessorProductsDeletedConsumer(
    IServiceProvider serviceProvider,
    IAiProcessorQueue queue)
    : IConsumer<IProductsDeletedCommand>
{
    private readonly IServiceProvider _serviceProvider =
        serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

    private readonly IAiProcessorQueue _queue = queue ?? throw new ArgumentNullException(nameof(queue));

    public async Task Consume(ConsumeContext<IProductsDeletedCommand> context)
    {
        var productIds = context.Message.ProductIds;
        if (productIds.Count == 0)
            return;

        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        var productRequestsCreator = scope.ServiceProvider.GetRequiredService<IProductRequestsCreator>();
        var productAttributeEmbeddingRequestsCreator =
            scope.ServiceProvider.GetRequiredService<IProductAttributeEmbeddingRequestsCreator>();

        var messages = new List<IAiProcessorQueueMessage>();

        #region Delete AI products

        AiProcessorDeleteProductRequest[] deleteAiProductRequests =
            await productRequestsCreator.CreateDeleteRequestsAsync(productIds, context.CancellationToken);

        if (deleteAiProductRequests.Length != 0)
        {
            messages.Add(new DeleteAiProductsMessage
            {
                Requests = deleteAiProductRequests
            });
        }

        #endregion

        #region Delete AI product attribute embeddings

        AiProcessorDeleteProductAttributeEmbeddingRequest[] deleteAiProductAttributeEmbeddingRequests =
            await productAttributeEmbeddingRequestsCreator
                .CreateDeleteRequestsAsync(productIds, context.CancellationToken);

        if (deleteAiProductAttributeEmbeddingRequests.Length != 0)
        {
            messages.Add(new DeleteAiProductAttributeEmbeddingsMessage
            {
                Requests = deleteAiProductAttributeEmbeddingRequests
            });
        }

        #endregion

        if (messages.Count == 0)
            return;

        await _queue.EnqueueMessagesAsync(messages, context.CancellationToken);
    }
}
