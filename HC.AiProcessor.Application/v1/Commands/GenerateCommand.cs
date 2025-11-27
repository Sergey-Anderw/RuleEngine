using HC.AiProcessor.Application.Services;
using HC.Packages.AiProcessor.V1.Models;

namespace HC.AiProcessor.Application.v1.Commands;

public record GenerateCommand(
    AiProcessorGenerateRequest Request
) : IRequest<GenerateCommandResult>;

public record GenerateCommandResult(
    AiProcessorGenerateResponse Response);

public class GenerateCommandHandler(IAiGenerationService aiGenerationService)
    : IRequestHandler<GenerateCommand, GenerateCommandResult>
{
    public async Task<GenerateCommandResult> Handle(
        GenerateCommand request,
        CancellationToken cancellationToken)
    {
        AiProcessorGenerateResponse response =
            await aiGenerationService.GenerateAsync(request.Request, cancellationToken);

        return new GenerateCommandResult(response);
    }
}
