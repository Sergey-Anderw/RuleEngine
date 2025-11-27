using HC.AiProcessor.Application.Services;
using HC.Packages.AiProcessor.V1.Models;

namespace HC.AiProcessor.Application.v1.Commands;

public record StreamingGenerateCommand(
    AiProcessorStreamingGenerateRequest Request
) : IRequest<IAsyncEnumerable<AiProcessorStreamingGenerateResponse>>;

public class StreamingGenerateCommandHandler(IAiGenerationService aiGenerationService)
    : IRequestHandler<StreamingGenerateCommand, IAsyncEnumerable<AiProcessorStreamingGenerateResponse>>
{
    public async Task<IAsyncEnumerable<AiProcessorStreamingGenerateResponse>> Handle(
        StreamingGenerateCommand request,
        CancellationToken cancellationToken)
    {
        IAsyncEnumerable<AiProcessorStreamingGenerateResponse> response =
            await aiGenerationService.StreamingGenerateAsync(request.Request, cancellationToken);

        return response;
    }
}
