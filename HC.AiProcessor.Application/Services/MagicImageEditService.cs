using HC.AiProcessor.Application.Clients;
using HC.AiProcessor.Application.Clients.ClaidAi;
using HC.Packages.AiProcessor.V1.Models;

namespace HC.AiProcessor.Application.Services;

public interface IImageEditService
{
    Task<double> GetImageQualityAsync(string imageUrl, CancellationToken cancellationToken);
    
    Task<AiProcessorImageTransformationResponse> Transform(AiProcessorImageTransformationRequest request, CancellationToken cancellationToken);
    
    Task<byte[]> RemoveWaterMarkByUrlAsync(string imageUrl, string fileName, string mimeType, CancellationToken cancellationToken);
}

public class MagicImageEditService(
    DeWatermarkAiClient watermarkClient,
    ClaidAiClient editClient,
    IHttpClientFactory clientFactory) : IImageEditService
{
    public async Task<double> GetImageQualityAsync(string imageUrl, CancellationToken cancellationToken)
    {
        var result = await editClient.Estimate(imageUrl, cancellationToken);
        return result.Data.ImageQuality.OverallQuality;
    }

    public async Task<AiProcessorImageTransformationResponse> Transform(
        AiProcessorImageTransformationRequest request, 
        CancellationToken cancellationToken)
    {
        var payload = ImageProcessingPayload.Create(request);
        var result = await editClient.Transform(payload, cancellationToken);
        return result.ToTransformResponse();
    }

    public async Task<byte[]> RemoveWaterMarkByUrlAsync(string imageUrl, string fileName, string mimeType, CancellationToken cancellationToken)
    {
        var bytes = await DownloadImageAsync(imageUrl, cancellationToken);
        var result = await watermarkClient.CleanUpWatermark(bytes, fileName, mimeType, cancellationToken);
        return result;
    }

    private async Task<byte[]> DownloadImageAsync(string url, CancellationToken cancellationToken)
    {
        var httpClient = clientFactory.CreateClient();
        var response = await httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (bytes.Length == 0)
            throw new InvalidOperationException($"File from {url} is empty");

        return bytes;
    }
}
