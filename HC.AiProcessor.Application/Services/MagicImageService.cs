using System.Collections.Frozen;
using System.Net.Http.Headers;
using System.Threading.Tasks.Dataflow;
using HC.AiProcessor.Application.Clients;
using HC.AiProcessor.Application.Extensions;
using Microsoft.Extensions.Logging;

namespace HC.AiProcessor.Application.Services;

public interface ISearchImagesService
{
    Task<IReadOnlyDictionary<string, IEnumerable<ImageItem>>> SearchImagesAsync(
        IReadOnlyDictionary<string, string> products,
        bool validateImages,
        int imagesAmount,
        CancellationToken cancellationToken = default);
}

public class MagicImageService(
    SerperClient client,
    IHttpClientFactory httpClientFactory,
    ILogger<MagicImageService> logger) : ISearchImagesService
{
    private readonly HttpClient _httpClient = httpClientFactory.CreateClient("ImagesVerificationClient");
    private readonly int _maxParallelism = Math.Clamp(Environment.ProcessorCount * 8, 16, 128);

    public async Task<IReadOnlyDictionary<string, IEnumerable<ImageItem>>> SearchImagesAsync(
        IReadOnlyDictionary<string, string> products,
        bool validateImages,
        int imagesAmount,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(products);

        var distinctProducts = products
            .GroupBy(kvp => kvp.Value, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);

        if (distinctProducts.Count == 0)
            return FrozenDictionary<string, IEnumerable<ImageItem>>.Empty;

        var tcs = new TaskCompletionSource<HttpRequestException>();

        var searchBlock = new TransformBlock<string[], IReadOnlyDictionary<string, IEnumerable<ImageItem>>>(
            async titles =>
            {
                try
                {
                    return await client.GetImagesAsync(titles, cancellationToken);
                }
                catch (HttpRequestException ex)
                {
                    tcs.TrySetResult(ex);
                    return FrozenDictionary<string, IEnumerable<ImageItem>>.Empty;
                }
            },
            new ExecutionDataflowBlockOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = 1
            });
        var flattenBlock =
            new TransformManyBlock<IReadOnlyDictionary<string, IEnumerable<ImageItem>>, (string Key, ImageItem Image)>(
                dict => dict.SelectMany(kvp => kvp.Value.Select(img => (kvp.Key, img))),
                new ExecutionDataflowBlockOptions
                {
                    CancellationToken = cancellationToken,
                    MaxDegreeOfParallelism = 1
                });

        var verifyBlock =
            new TransformBlock<(string Key, ImageItem Image), (string Key, ImageItem Image, bool IsValid)>(
                async tuple =>
                {
                    var isValid = await CanDownloadImageAsync(tuple.Image.Url, cancellationToken);
                    return (tuple.Key, tuple.Image, isValid);
                },
                new ExecutionDataflowBlockOptions
                {
                    CancellationToken = cancellationToken,
                    MaxDegreeOfParallelism = _maxParallelism,
                    BoundedCapacity = _maxParallelism * 2
                });

        var filterBlock =
            new TransformManyBlock<(string Key, ImageItem Image, bool IsValid), (string Key, ImageItem Image)>(
                tuple => tuple.IsValid ? new[] { (tuple.Key, tuple.Image) } : Array.Empty<(string, ImageItem)>(),
                new ExecutionDataflowBlockOptions
                {
                    CancellationToken = cancellationToken,
                    MaxDegreeOfParallelism = 1
                });
        var groupBlock = new BatchBlock<(string Key, ImageItem Image)>(
            int.MaxValue,
            new GroupingDataflowBlockOptions
            {
                Greedy = true,
                CancellationToken = cancellationToken
            });
        var dictBlock =
            new TransformBlock<(string Key, ImageItem Image)[], IReadOnlyDictionary<string, IEnumerable<ImageItem>>>(
                items =>
                {
                    if (items.Length == 0)
                        return FrozenDictionary<string, IEnumerable<ImageItem>>.Empty;

                    return items
                        .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(
                            g => g.Key, IEnumerable<ImageItem> (g) => g.Select(i => i.Image).ToArray(),
                            StringComparer.OrdinalIgnoreCase)
                        .ToFrozenDictionary();
                },
                new ExecutionDataflowBlockOptions
                {
                    CancellationToken = cancellationToken,
                    MaxDegreeOfParallelism = 1
                });
        var rangeBlock = GetRangeBlock(imagesAmount, cancellationToken);
        var resultBlock = new BufferBlock<IReadOnlyDictionary<string, IEnumerable<ImageItem>>>(
            new DataflowBlockOptions
            {
                CancellationToken = cancellationToken
            });

        searchBlock.LinkTo(rangeBlock,
            new DataflowLinkOptions { PropagateCompletion = true },
            dict => dict.Count > 0);
        rangeBlock.LinkTo(flattenBlock, new DataflowLinkOptions { PropagateCompletion = true });
        flattenBlock.LinkTo(verifyBlock, new DataflowLinkOptions { PropagateCompletion = true });
        verifyBlock.LinkTo(filterBlock, new DataflowLinkOptions { PropagateCompletion = true });
        filterBlock.LinkTo(groupBlock, new DataflowLinkOptions { PropagateCompletion = true });
        groupBlock.LinkTo(dictBlock, new DataflowLinkOptions { PropagateCompletion = true });
        dictBlock.LinkTo(resultBlock,
            new DataflowLinkOptions { PropagateCompletion = true },
            dict => dict.Count > 0);
        searchBlock.Post(distinctProducts.Values.ToArray());
        searchBlock.Complete();

        try
        {
            var receiveTask = resultBlock.ReceiveAsync(cancellationToken);
            var completedTask = await Task.WhenAny(receiveTask, tcs.Task);

            if (completedTask == tcs.Task)
            {
                throw await tcs.Task;
            }

            var result = await receiveTask;
            if (result.Count == 0)
                return FrozenDictionary<string, IEnumerable<ImageItem>>.Empty;

            return distinctProducts
                .Where(kvp => result.ContainsKey(kvp.Value))
                .ToFrozenDictionary(
                    kvp => kvp.Key,
                    kvp => result[kvp.Value],
                    StringComparer.OrdinalIgnoreCase);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "HTTP request exception occurred during image processing");
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Error processing images through dataflow pipeline");
            return FrozenDictionary<string, IEnumerable<ImageItem>>.Empty;
        }
    }

    private static TransformBlock<IReadOnlyDictionary<string, IEnumerable<ImageItem>>,
        IReadOnlyDictionary<string, IEnumerable<ImageItem>>> GetRangeBlock(int imagesAmount,
        CancellationToken cancellationToken)
    {
        string[] allowedImages = ["png", "jpg", "jpeg", "jpeg", "tif", "tiff", "wepb"];
        return new TransformBlock<IReadOnlyDictionary<string, IEnumerable<ImageItem>>,
            IReadOnlyDictionary<string, IEnumerable<ImageItem>>>(
            images =>
            {
                var ranged = new Dictionary<string, IEnumerable<ImageItem>>(StringComparer.OrdinalIgnoreCase);
                foreach (var (key, list) in images)
                {
                    var top = list
                        .Where(img => allowedImages.Contains(img.Url.Split('.').Last().ToLowerInvariant()))
                        .Where(img => img.Size >= 202500) //more than 450*450 px
                        .Where(img => img.AspectRatio <= 2)
                        .OrderBy(img => img.AspectRatio - 1)
                        .ThenByDescending(img => img.Size)
                        .Take(imagesAmount)
                        .ToArray();
                    ranged[key] = top;
                }

                return ranged.ToFrozenDictionary();
            },
            new ExecutionDataflowBlockOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = 1
            });
    }

    private async Task<bool> CanDownloadImageAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                return false;
            using var req = new HttpRequestMessage(HttpMethod.Get, uri);
            req.Headers.Range = new RangeHeaderValue(0, 0);

            using var resp = await _httpClient.SendAsync(
                req,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            return resp.IsImageOk();
        }
        catch (OperationCanceledException ex)
        {
            logger.LogWarning(ex, "CanDownloadImageAsync: operation canceled");
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "CanDownloadImageAsync: error");
            return false;
        }
    }
}
