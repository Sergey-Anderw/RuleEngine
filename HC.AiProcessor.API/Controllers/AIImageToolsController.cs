using Asp.Versioning;
using HC.AiProcessor.Application.v1.Commands.AiImageTools;
using HC.AiProcessor.Application.v1.Queries.AiImageTools;
using HC.Packages.AiProcessor.V1.Models;
using HC.Packages.Common.Contracts.V1;
using Microsoft.AspNetCore.Mvc;

namespace HC.AiProcessor.API.Controllers;

[ApiController]
[Route("v{version:apiVersion}/[controller]")]
[ApiVersion("1.0")]
public class AIImageToolsController : ApiControllerBase
{
    [Route("search")]
    [HttpPost]
    public async Task<IActionResult> SearchProductImages(
        [FromBody] AiProcessorSearchProductImagesRequest request,
        CancellationToken ct)
    {
        var result = await Mediator.Send(new SearchProductImagesCommand(request), ct);
        return Success(result.Responses);
    }

    [HttpPost]
    [Route("transform")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ApiResponse<AiProcessorImageTransformationResponse>))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Transform(
        [FromBody] AiProcessorImageTransformationRequest request,
        CancellationToken ct)
    {
        var result = await Mediator.Send(new TransformImageCommand(request), ct);
        return Success(result);
    }
    
    [Route("remove-watermark")]
    [HttpPost]
    public async Task<IActionResult> RemoveWatermark(
        [FromBody] AiProcessorRemoveWatermarkRequest request,
        CancellationToken ct)
    {
        var result = await Mediator.Send(new CleanUpWatermarkCommand(request), ct);
        return Success(result.Response);
    }

    [HttpPost]
    [Route("image-quality")]
    public async Task<IActionResult> GetImageQuality(
        [FromBody] AiProcessorImageQualityRequest request,
        CancellationToken ct)
    {
        var result = await Mediator.Send(new ImageQualityQuery(request), ct);
        return Success(result);
    }
}
