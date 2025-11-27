using HC.AiProcessor.Application.Services;
using HC.Packages.AiProcessor.V1.Models;

namespace HC.AiProcessor.Application.v1.Commands;

public record TranslateCommand(
    AiProcessorTranslateRequest Request
) : IRequest<TranslateCommandResult>;

public record TranslateCommandResult(
    AiProcessorTranslateResponse Response);

public class TranslateCommandHandler(IAiTranslationService aiTranslationService)
    : IRequestHandler<TranslateCommand, TranslateCommandResult>
{
    public async Task<TranslateCommandResult> Handle(
        TranslateCommand request,
        CancellationToken cancellationToken)
    {
        AiProcessorTranslateResponse response =
            await aiTranslationService.TranslateAsync(request.Request, cancellationToken);

        return new TranslateCommandResult(response);
    }
}
