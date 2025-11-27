using HC.AiProcessor.Application.Services;
using HC.Packages.AiProcessor.V1.Models;

namespace HC.AiProcessor.Application.v1.Commands;

public record StreamingTranslateCommand(
    AiProcessorStreamingTranslateRequest Request) : IRequest<IAsyncEnumerable<AiProcessorStreamingTranslateResponse>>;

public class StreamingTranslateCommandHandler(IAiStreamingTranslationService aiStreamingTranslationService)
    : IRequestHandler<StreamingTranslateCommand, IAsyncEnumerable<AiProcessorStreamingTranslateResponse>>
{
    public async Task<IAsyncEnumerable<AiProcessorStreamingTranslateResponse>> Handle(
        StreamingTranslateCommand request,
        CancellationToken cancellationToken)
    {
        IAsyncEnumerable<AiProcessorStreamingTranslateResponse> response =
            await aiStreamingTranslationService.TranslateAsync(request.Request, cancellationToken);

        return response;
    }
}
