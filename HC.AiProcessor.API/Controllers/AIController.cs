using System.Text;
using Asp.Versioning;
using HC.AiProcessor.Application.Models.Requests;
using HC.AiProcessor.Application.Services;
using HC.AiProcessor.Application.v1.Commands;
using HC.Packages.AiProcessor.V1.Models;
using HC.Packages.Auth.Attributes;
using HC.Packages.Common.Contracts.V1;
using Microsoft.AspNetCore.Mvc;

namespace HC.AiProcessor.API.Controllers
{
    [ApiController]
    [Route("v{version:apiVersion}/[controller]")]
    [ApiVersion("1.0")]
    public class AIController : ApiControllerBase
    {
        #region Generate

        [Route("generate")]
        [HttpPost]
        public async Task<IResult> Generate([FromBody] AiProcessorGenerateRequest request, CancellationToken ct)
        {
            GenerateCommandResult result = await Mediator.Send(new GenerateCommand(request), ct);
            return Results.Ok(result.Response);
        }

        [Route("generate/batch")]
        [HttpPost]
        public async Task<IResult> BatchGenerate(
            [FromBody] AiProcessorBatchRequest<AiProcessorGenerateRequest> request,
            CancellationToken ct)
        {
            BatchGenerateCommandResult result = await Mediator.Send(new BatchGenerateCommand(request), ct);
            return Results.Ok(result.Response);
        }

        [Route("generate/streaming")]
        [HttpPost]
        [ServiceAuthorize]
        public async Task<IResult> StreamingGenerate(
            [FromBody] AiProcessorStreamingGenerateRequest request,
            CancellationToken ct)
        {
            IAsyncEnumerable<AiProcessorStreamingGenerateResponse> response =
                await Mediator.Send(new StreamingGenerateCommand(request), ct);

            return TypedResults.Stream(async stream =>
            {
                await using var writer = new StreamWriter(stream, encoding: Encoding.UTF8);

                await foreach (AiProcessorStreamingGenerateResponse chunk in response.WithCancellation(ct))
                {
                    await writer.WriteAsync(chunk.Text);
                    await writer.FlushAsync(ct);
                }
            }, contentType: "text/plain");
        }

        #endregion

        #region Rephrase

        [Route("rephrase")]
        [HttpPost]
        public async Task<IResult> Rephrase([FromBody] AiProcessorRephraseRequest request, CancellationToken ct)
        {
            RephraseCommandResult result = await Mediator.Send(new RephraseCommand(request), ct);
            return Results.Ok(result.Response);
        }

        [Route("rephrase/batch")]
        [HttpPost]
        public async Task<IResult> BatchRephrase(
            [FromBody] AiProcessorBatchRequest<AiProcessorRephraseRequest> request,
            CancellationToken ct)
        {
            BatchRephraseCommandResult result = await Mediator.Send(new BatchRephraseCommand(request), ct);
            return Results.Ok(result.Response);
        }

        [Route("rephrase/streaming")]
        [HttpPost]
        [ServiceAuthorize]
        public async Task<IResult> StreamingRephrase(
            [FromBody] AiProcessorStreamingRephraseRequest request,
            CancellationToken ct)
        {
            IAsyncEnumerable<AiProcessorStreamingRephraseResponse> response =
                await Mediator.Send(new StreamingRephraseCommand(request), ct);

            return TypedResults.Stream(async stream =>
            {
                await using var writer = new StreamWriter(stream, encoding: Encoding.UTF8);

                await foreach (AiProcessorStreamingRephraseResponse chunk in response.WithCancellation(ct))
                {
                    await writer.WriteAsync(chunk.Text);
                    await writer.FlushAsync(ct);
                }
            }, contentType: "text/plain");
        }

        #endregion

        #region Translate

        [Route("translate")]
        [HttpPost]
        public async Task<IResult> Translate([FromBody] AiProcessorTranslateRequest request, CancellationToken ct)
        {
            TranslateCommandResult result = await Mediator.Send(new TranslateCommand(request), ct);
            return Results.Ok(result.Response);
        }

        [Route("translate/batch")]
        [HttpPost]
        public async Task<IResult> BatchTranslate(
            [FromBody] AiProcessorBatchRequest<AiProcessorTranslateRequest> request,
            CancellationToken ct)
        {
            BatchTranslateCommandResult result = await Mediator.Send(new BatchTranslateCommand(request), ct);
            return Results.Ok(result.Response);
        }

        [Route("translate/streaming")]
        [HttpPost]
        [ServiceAuthorize]
        public async Task<IResult> StreamingTranslate(
            [FromBody] AiProcessorStreamingTranslateRequest request,
            CancellationToken ct)
        {
            IAsyncEnumerable<AiProcessorStreamingTranslateResponse> response =
                await Mediator.Send(new StreamingTranslateCommand(request), ct);

            return TypedResults.Stream(async stream =>
            {
                await using var writer = new StreamWriter(stream, encoding: Encoding.UTF8);

                await foreach (AiProcessorStreamingTranslateResponse chunk in response.WithCancellation(ct))
                {
                    await writer.WriteAsync(chunk.Text);
                    await writer.FlushAsync(ct);
                }
            }, contentType: "text/plain");
        }

        #endregion

        #region Populate

        [Route("populate")]
        [HttpPost]
        [ServiceAuthorize]
        public async Task<IResult> Populate(
            [FromBody] AiProcessorPopulateAttributesRequest request,
            CancellationToken ct)
        {
            PopulateAttributesCommandResult result = await Mediator.Send(new PopulateAttributesCommand(request), ct);
            return Results.Ok(result.Response);
        }

        [Route("populate/batch")]
        [HttpPost]
        public async Task<IResult> BatchPopulate(
            [FromBody] AiProcessorBatchRequest<AiProcessorPopulateAttributesRequest> request,
            CancellationToken ct)
        {
            BatchPopulateAttributesCommandResult result =
                await Mediator.Send(new BatchPopulateAttributesCommand(request), ct);
            return Results.Ok(result.Response);
        }

        #endregion

#if DEBUG

        #region Options mapping

        [Route("options/map")]
        [HttpPost]
        public async Task<IResult> MapOptions(
            [FromBody] IReadOnlyCollection<OptionsMappingInput> request,
            CancellationToken ct)
        {
            MapOptionsCommandResult result = await Mediator.Send(new MapOptionsCommand(request), ct);
            return Results.Ok(result);
        }

        #endregion

#endif

        [Route("extract")]
        [Consumes("multipart/form-data")]
        [HttpPost]
        public async Task<IResult> Extract([FromForm] AiProcessorFilesExtractItemsRequest request, CancellationToken ct)
        {
            ExtractCommandResult result = await Mediator.Send(new ExtractCommand(request), ct);
            return Results.Ok(result.Response);
        }
    }
}
