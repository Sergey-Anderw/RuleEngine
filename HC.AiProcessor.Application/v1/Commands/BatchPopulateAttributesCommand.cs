using HC.AiProcessor.Application.Services;
using HC.Packages.AiProcessor.V1.Models;

namespace HC.AiProcessor.Application.v1.Commands;

public record BatchPopulateAttributesCommand(
    AiProcessorBatchRequest<AiProcessorPopulateAttributesRequest> Request
) : IRequest<BatchPopulateAttributesCommandResult>;

public record BatchPopulateAttributesCommandResult(
    AiProcessorBatchResponse<AiProcessorPopulateAttributesResponse> Response);

public class BatchPopulateAttributesHandler(IAiAttributesPopulationService aiAttributesPopulationService)
    : IRequestHandler<BatchPopulateAttributesCommand, BatchPopulateAttributesCommandResult>
{
    public async Task<BatchPopulateAttributesCommandResult> Handle(
        BatchPopulateAttributesCommand command,
        CancellationToken cancellationToken)
    {
        AiProcessorBatchResponse<AiProcessorPopulateAttributesResponse> response =
            await aiAttributesPopulationService.BatchPopulateAttributesAsync(command.Request, cancellationToken);

        return new BatchPopulateAttributesCommandResult(response);
    }
}
