using HC.AiProcessor.Application.Services;
using HC.Packages.AiProcessor.V1.Models;

namespace HC.AiProcessor.Application.v1.Commands;

public record BatchRephraseCommand(
    AiProcessorBatchRequest<AiProcessorRephraseRequest> Request
) : IRequest<BatchRephraseCommandResult>;

public record BatchRephraseCommandResult(
    AiProcessorBatchResponse<AiProcessorRephraseResponse> Response);

public class BatchRephraseCommandHandler(IAiRephrasingService aiRephrasingService)
    : IRequestHandler<BatchRephraseCommand, BatchRephraseCommandResult>
{
    public async Task<BatchRephraseCommandResult> Handle(
        BatchRephraseCommand request,
        CancellationToken cancellationToken)
    {
        AiProcessorBatchResponse<AiProcessorRephraseResponse> response =
            await aiRephrasingService.BatchRephraseAsync(request.Request, cancellationToken);

        return new BatchRephraseCommandResult(response);
    }
}
