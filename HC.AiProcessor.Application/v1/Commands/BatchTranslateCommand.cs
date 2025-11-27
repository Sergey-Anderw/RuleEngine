using HC.AiProcessor.Application.Services;
using HC.Packages.AiProcessor.V1.Models;

namespace HC.AiProcessor.Application.v1.Commands;

public record BatchTranslateCommand(
    AiProcessorBatchRequest<AiProcessorTranslateRequest> Request
) : IRequest<BatchTranslateCommandResult>;

public record BatchTranslateCommandResult(
    AiProcessorBatchResponse<AiProcessorTranslateResponse> Response);

public class BatchTranslateCommandHandler(IAiTranslationService aiTranslationService)
    : IRequestHandler<BatchTranslateCommand, BatchTranslateCommandResult>
{
    public async Task<BatchTranslateCommandResult> Handle(
        BatchTranslateCommand request,
        CancellationToken cancellationToken)
    {
        AiProcessorBatchResponse<AiProcessorTranslateResponse> response =
            await aiTranslationService.BatchTranslateAsync(request.Request, cancellationToken);

        return new BatchTranslateCommandResult(response);
    }
}
