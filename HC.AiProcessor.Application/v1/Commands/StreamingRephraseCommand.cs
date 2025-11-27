using HC.AiProcessor.Application.Services;
using HC.Packages.AiProcessor.V1.Models;

namespace HC.AiProcessor.Application.v1.Commands;

public record StreamingRephraseCommand(
    AiProcessorStreamingRephraseRequest Request) : IRequest<IAsyncEnumerable<AiProcessorStreamingRephraseResponse>>;

public class StreamingRephraseCommandHandler(IAiStreamingRephrasingService aiStreamingRephrasingService) :
    IRequestHandler<StreamingRephraseCommand, IAsyncEnumerable<AiProcessorStreamingRephraseResponse>>
{
    public async Task<IAsyncEnumerable<AiProcessorStreamingRephraseResponse>> Handle(
        StreamingRephraseCommand request,
        CancellationToken cancellationToken)
    {
        IAsyncEnumerable<AiProcessorStreamingRephraseResponse> response =
            await aiStreamingRephrasingService.RephraseAsync(request.Request, cancellationToken);

        return response;
    }
}
