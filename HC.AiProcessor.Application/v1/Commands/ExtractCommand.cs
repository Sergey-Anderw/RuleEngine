using HC.AiProcessor.Application.Models.Requests;
using HC.AiProcessor.Application.Models.Responses;
using HC.AiProcessor.Application.Services;

namespace HC.AiProcessor.Application.v1.Commands;

public record ExtractCommand(
    AiProcessorFilesExtractItemsRequest Request
) : IRequest<ExtractCommandResult>;

public record ExtractCommandResult(
    AiProcessorFilesExtractItemsResponse? Response,
    TimeSpan? Timeout);

public class ExtractCommandHandler : IRequestHandler<ExtractCommand, ExtractCommandResult>
{
    private readonly IAiProcessorFilesExtractItemsService _aiProcessorFilesExtractItemsService;

    public ExtractCommandHandler(IAiProcessorFilesExtractItemsService aiProcessorFilesExtractItemsService)
    {
        _aiProcessorFilesExtractItemsService =
            aiProcessorFilesExtractItemsService ??
            throw new ArgumentNullException(nameof(aiProcessorFilesExtractItemsService));
    }

    public async Task<ExtractCommandResult> Handle(
        ExtractCommand request,
        CancellationToken cancellationToken)
    {
        AiProcessorFilesExtractItemsResponse? response = null;
        var timeout = await _aiProcessorFilesExtractItemsService.Extract(
            request.Request,
            (res, ct) =>
            {
                response = res;
                return Task.CompletedTask;
            },
            cancellationToken);

        return new ExtractCommandResult(response, timeout);
    }
}
