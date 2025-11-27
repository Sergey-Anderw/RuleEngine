using HC.AiProcessor.Application.Services;
using HC.Packages.AiProcessor.V1.Models;

namespace HC.AiProcessor.Application.v1.Commands;

public record BatchGenerateCommand(
    AiProcessorBatchRequest<AiProcessorGenerateRequest> Request
) : IRequest<BatchGenerateCommandResult>;

public record BatchGenerateCommandResult(
    AiProcessorBatchResponse<AiProcessorGenerateResponse> Response);

public class BatchGenerateCommandHandler(IAiGenerationService aiGenerationService)
    : IRequestHandler<BatchGenerateCommand, BatchGenerateCommandResult>
{
    public async Task<BatchGenerateCommandResult> Handle(
        BatchGenerateCommand request,
        CancellationToken cancellationToken)
    {
        AiProcessorBatchResponse<AiProcessorGenerateResponse> responses =
            await aiGenerationService.BatchGenerateAsync(request.Request, cancellationToken);

        return new BatchGenerateCommandResult(responses);
    }
}
