using HC.AiProcessor.Application.Services;
using HC.Packages.AiProcessor.V1.Models;

namespace HC.AiProcessor.Application.v1.Commands;

public record PopulateAttributesCommand(
    AiProcessorPopulateAttributesRequest Request
) : IRequest<PopulateAttributesCommandResult>;

public record PopulateAttributesCommandResult(
    AiProcessorPopulateAttributesResponse Response);

public class PopulateAttributesCommandHandler(IAiAttributesPopulationService aiAttributesPopulationService)
    : IRequestHandler<PopulateAttributesCommand, PopulateAttributesCommandResult>
{
    public async Task<PopulateAttributesCommandResult> Handle(
        PopulateAttributesCommand request,
        CancellationToken cancellationToken)
    {
        AiProcessorPopulateAttributesResponse response =
            await aiAttributesPopulationService.PopulateAttributesAsync(request.Request, cancellationToken);

        return new PopulateAttributesCommandResult(response);
    }
}
