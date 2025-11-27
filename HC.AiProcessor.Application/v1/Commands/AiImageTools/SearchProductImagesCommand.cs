using System.Collections.Frozen;
using FluentValidation;
using HC.AiProcessor.Application.Services;
using HC.Packages.AiProcessor.V1.Models;

namespace HC.AiProcessor.Application.v1.Commands.AiImageTools;

public record SearchProductImagesCommand(
    AiProcessorSearchProductImagesRequest Request)
:IRequest<SearchProductImagesCommandResult>;

public record SearchProductImagesCommandResult(
    IReadOnlyList<AiProcessorSearchProductImagesResponse> Responses
);

public class SearchProductImagesCommandValidator : AbstractValidator<SearchProductImagesCommand>
{
    public SearchProductImagesCommandValidator()
    {
        RuleFor(x => x.Request)
            .NotNull();
        RuleFor(x => x.Request.ImagesAmount)
            .GreaterThanOrEqualTo(1)
            .LessThanOrEqualTo(10);
    }
}

public class SearchProductImagesCommandHandler(ISearchImagesService service) 
    : IRequestHandler<SearchProductImagesCommand, SearchProductImagesCommandResult>
{
    public async Task<SearchProductImagesCommandResult> Handle(SearchProductImagesCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Request.Products);

        var products = command.Request.Products
            .DistinctBy(x => x.Code)
            .ToFrozenDictionary(x => x.Code, x => x.Title);

        var images = await service.SearchImagesAsync(
            products,
            command.Request.ValidateUrls,
            command.Request.ImagesAmount,
            cancellationToken);

        var results = images.Select(x =>
                new AiProcessorSearchProductImagesResponse
                {
                    Code = x.Key,
                    ImageUrls = x.Value
                        .Select(img => img.Url)
                        .ToArray()
                })
            .ToArray();
        
        return new SearchProductImagesCommandResult(results);
    }
}
