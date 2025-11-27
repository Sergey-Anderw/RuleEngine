using Asp.Versioning;
using HC.AiProcessor.Application.v1.Commands;
using HC.Packages.AiProcessor.V1.Models;
using HC.Packages.Common.Contracts.V1;
using Microsoft.AspNetCore.Mvc;

namespace HC.AiProcessor.API.Controllers;

[ApiController]
[Route("v{version:apiVersion}/[controller]")]
[ApiVersion("1.0")]
public class AIProductController : ApiControllerBase
{
    [Route("refresh")]
    [HttpPost]
    public async Task<IResult> RefreshProducts(
        [FromBody] AiProcessorRefreshProductRequest[] requests,
        CancellationToken ct)
    {
        RefreshAiProductCommandResult result =
            await Mediator.Send(new RefreshAiProductCommand(requests), ct);
        return Results.Ok(result.Response);
    }

    [Route("delete")]
    [HttpPost]
    public async Task<IResult> DeleteProducts(
        [FromBody] AiProcessorDeleteProductRequest[] requests,
        CancellationToken ct)
    {
        DeleteAiProductCommandResult result =
            await Mediator.Send(new DeleteAiProductCommand(requests), ct);
        return Results.Ok(result.Response);
    }

    [Route("embeddings/generate")]
    [HttpPost]
    public async Task<IResult> GenerateProductAttributeEmbeddings(
        [FromBody] AiProcessorGenerateProductAttributeEmbeddingRequest[] requests,
        CancellationToken ct)
    {
        GenerateAiProductAttributeEmbeddingsCommandResult result =
            await Mediator.Send(new GenerateAiProductAttributeEmbeddingsCommand(requests), ct);
        return Results.Ok(result.Response);
    }

    [Route("embeddings/delete")]
    [HttpPost]
    public async Task<IResult> DeleteProductAttributeEmbeddings(
        [FromBody] AiProcessorDeleteProductAttributeEmbeddingRequest[] requests,
        CancellationToken ct)
    {
        DeleteAiProductAttributeEmbeddingsCommandResult result =
            await Mediator.Send(new DeleteAiProductAttributeEmbeddingsCommand(requests), ct);
        return Results.Ok(result.Response);
    }

    [Route("family/determine")]
    [HttpPost]
    public async Task<IResult> DetermineProductFamily(
        [FromBody] AiProcessorDetermineProductFamilyRequest request,
        CancellationToken ct)
    {
        DetermineProductFamilyCommandResult result =
            await Mediator.Send(new DetermineProductFamilyCommand(request), ct);
        return Results.Ok(result.Response);
    }
}
