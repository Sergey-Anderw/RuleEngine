using Fluid;
using Microsoft.Extensions.Caching.Memory;

namespace HC.AiProcessor.Application.Services;

public interface IParsedTemplate
{
    string Render(object ctx);
}

public interface ITemplateEngine
{
    public IParsedTemplate? ParseTemplate(string template);
    public bool TryParseTemplate(string template, out IParsedTemplate? result);
}

internal sealed class ParsedFluidTemplate : IParsedTemplate
{
    private readonly IFluidTemplate _template;

    public ParsedFluidTemplate(IFluidTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        _template = template;
    }

    public string Render(object ctx)
    {
        string result = _template.Render(context: new TemplateContext(ctx));
        return result;
    }
}

internal sealed class FluidTemplateEngine : ITemplateEngine
{
    private readonly IMemoryCache _memoryCache = new MemoryCache(new MemoryCacheOptions());
    private readonly TimeSpan _cacheLifetime = TimeSpan.FromDays(1);

    public IParsedTemplate? ParseTemplate(string template)
    {
        if (string.IsNullOrWhiteSpace(template))
            return null;

        if (!_memoryCache.TryGetValue(template, out IFluidTemplate? fluidTemplate))
        {
            var parser = new FluidParser();
            fluidTemplate = parser.Parse(template);
            _memoryCache.Set(template, fluidTemplate, _cacheLifetime);
        }

        if (fluidTemplate is null)
            return null;

        var result = new ParsedFluidTemplate(fluidTemplate);
        return result;
    }

    public bool TryParseTemplate(string template, out IParsedTemplate? result)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            result = null;
            return false;
        }

        if (_memoryCache.TryGetValue(template, out IFluidTemplate? fluidTemplate))
        {
            result = new ParsedFluidTemplate(fluidTemplate!);
            return true;
        }

        var parser = new FluidParser();
        if (!parser.TryParse(template, out fluidTemplate))
        {
            result = null;
            return false;
        }

        _memoryCache.Set(template, fluidTemplate, _cacheLifetime);
        result = new ParsedFluidTemplate(fluidTemplate!);
        return true;
    }
}
