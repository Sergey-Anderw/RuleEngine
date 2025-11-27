using HC.AiProcessor.Application.Services;
using HC.Packages.AiProcessor.V1.Models;

namespace HC.AiProcessor.Application.v1.Commands;

public record RephraseCommand(
    AiProcessorRephraseRequest Request
) : IRequest<RephraseCommandResult>;

public record RephraseCommandResult(
    AiProcessorRephraseResponse Response);

public class RephraseCommandHandler(IAiRephrasingService aiRephrasingService)
    : IRequestHandler<RephraseCommand, RephraseCommandResult>
{
    public async Task<RephraseCommandResult> Handle(
        RephraseCommand request,
        CancellationToken cancellationToken)
    {
        AiProcessorRephraseResponse response =
            await aiRephrasingService.RephraseAsync(request.Request, cancellationToken);

        return new RephraseCommandResult(response);
    }
}
