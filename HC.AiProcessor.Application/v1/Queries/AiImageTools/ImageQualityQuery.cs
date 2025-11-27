using HC.AiProcessor.Application.Services;
using HC.Packages.AiProcessor.V1.Models;

namespace HC.AiProcessor.Application.v1.Queries.AiImageTools;

public record ImageQualityQuery(AiProcessorImageQualityRequest Request) : IRequest<AiProcessorImageQualityResponse>;

public class ImageQualityQueryHandler(IImageEditService service) 
    : IRequestHandler<ImageQualityQuery, AiProcessorImageQualityResponse>
{
    public async Task<AiProcessorImageQualityResponse> Handle(ImageQualityQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(query.Request.ImageUrl);
        var overallQuality = await service.GetImageQualityAsync(query.Request.ImageUrl, cancellationToken);
        return new AiProcessorImageQualityResponse(overallQuality);
    }
}
