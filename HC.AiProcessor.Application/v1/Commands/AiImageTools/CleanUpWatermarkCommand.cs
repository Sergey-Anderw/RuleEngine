using FluentValidation;
using HC.AiProcessor.Application.Helpers;
using HC.AiProcessor.Application.Services;
using HC.Packages.AiProcessor.V1.Models;
using Microsoft.AspNetCore.StaticFiles;

namespace HC.AiProcessor.Application.v1.Commands.AiImageTools;

public record CleanUpWatermarkCommand(AiProcessorRemoveWatermarkRequest Request)
    : IRequest<CleanUpWatermarkCommandResult>;

public record CleanUpWatermarkCommandResult(AiProcessorRemoveWatermarkResponse Response);

public class CleanUpWatermarkCommandValidator : AbstractValidator<CleanUpWatermarkCommand>
{
    public CleanUpWatermarkCommandValidator()
    {
        RuleFor(x => x.Request)
            .NotNull();
        RuleFor(x => x.Request.ImageUrl)
            .NotNull()
            .NotEmpty();
    }
}

public class CleanUpWatermarkCommandHandler(
    IImageEditService service,
    IStorageService storageService) 
    : IRequestHandler<CleanUpWatermarkCommand, CleanUpWatermarkCommandResult>
{
    public async Task<CleanUpWatermarkCommandResult> Handle(CleanUpWatermarkCommand commad, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(commad);
        ArgumentNullException.ThrowIfNull(commad.Request.ImageUrl);
        var uri = new Uri(commad.Request.ImageUrl);
        var fileName = Path.GetFileName(uri.LocalPath);
        var provider = new FileExtensionContentTypeProvider();
        if (!provider.TryGetContentType(fileName, out var mimeType))
        {
            mimeType = "application/octet-stream";
        }

        var result = await service.RemoveWaterMarkByUrlAsync(commad.Request.ImageUrl, fileName, mimeType, cancellationToken);
        using var stream = new MemoryStream(result);
        var genFileName = FileHelper.GenerateUniqueFileName(fileName);
        var filePath = await storageService.SaveTempFileAsync(stream, genFileName, mimeType, cancellationToken);
        return new CleanUpWatermarkCommandResult(new AiProcessorRemoveWatermarkResponse
        {
            ImagePath = filePath,
            FileName = fileName,
            GenFileName = genFileName,
            ContentType = mimeType,
            FileSize = result.Length
        });
    }
}
