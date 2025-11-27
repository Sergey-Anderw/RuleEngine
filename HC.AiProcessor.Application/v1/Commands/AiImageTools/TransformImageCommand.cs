using FluentValidation;
using HC.AiProcessor.Application.Services;
using HC.Packages.AiProcessor.V1.Models;

namespace HC.AiProcessor.Application.v1.Commands.AiImageTools;

public record TransformImageCommand(AiProcessorImageTransformationRequest Request)
    : IRequest<AiProcessorImageTransformationResponse>;

public class TransformImageCommandValidator : AbstractValidator<TransformImageCommand>
{
    public TransformImageCommandValidator()
    {
        RuleFor(x => x.Request)
            .NotNull();
        RuleFor(x => x.Request.ImageUrl)
            .NotNull()
            .NotEmpty();
        RuleFor(x => x.Request)
            .Must(request => request.Background != null ||
                             request.ChangeSize != null ||
                             request.Restoration != null
            )
            .WithMessage("At least one transformation must be specified: RemoveBackground, ChangeSize or Upscale");
    }
}

public class TransformImageCommandHandler(IImageEditService service)
    : IRequestHandler<TransformImageCommand, AiProcessorImageTransformationResponse>
{
    public async Task<AiProcessorImageTransformationResponse> Handle(
        TransformImageCommand command, 
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Request);
        
        return await service.Transform(command.Request, cancellationToken);
    }
}
