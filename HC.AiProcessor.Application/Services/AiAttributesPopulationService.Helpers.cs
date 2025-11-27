using System.Globalization;
using HC.Packages.AiProcessor.V1.Models;
using HC.Packages.Catalog.Contracts.V1.Enums;

namespace HC.AiProcessor.Application.Services;

internal sealed partial class ChatGptAttributesPopulationService
{
    private static bool IsValidDate(string? input)
    {
        return DateTime.TryParseExact(input, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
    }

    private static bool IsAttributeSettingsEmpty(AiProcessorPopulateAttributesRequest.AttributeSettings? settings)
    {
        if (settings is null)
            return true;

        return settings.Minimum is null &&
               settings.Maximum is null &&
               settings.AllowNegative is null &&
               settings.FractionDigits is null &&
               settings.ValidationRule is null &&
               settings.AllowHtml is null &&
               settings.MinimumDate is null &&
               settings.MaximumDate is null &&
               (settings.Options is null || settings.Options.Count == 0);
    }


    private static List<T> GetExamples<T>(IList<T> collection, int maxExamples = OptionExamplesCountPerAttribute)
    {
        var result = new List<T>();
        int count = collection.Count;

        if (count == 0 || maxExamples <= 0)
            return result;

        if (maxExamples >= count)
        {
            result.AddRange(collection);
            return result;
        }

        double step = (double) (count - 1) / (maxExamples - 1);

        for (var i = 0; i < maxExamples; i++)
        {
            var index = (int) Math.Round(i * step);
            result.Add(collection[index]);
        }

        return result;
    }

    private static List<T> GetRandomExamples<T>(IList<T> collection, int maxExamples = OptionExamplesCountPerAttribute)
    {
        var result = new List<T>();
        int count = collection.Count;

        if (count == 0 || maxExamples <= 0)
            return result;

        if (maxExamples >= count)
        {
            result.AddRange(collection);
            return result;
        }

        var indexes = Enumerable.Range(0, count).ToList();
        var rng = new Random();

        for (int i = indexes.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (indexes[i], indexes[j]) = (indexes[j], indexes[i]);
        }

        for (var i = 0; i < maxExamples; i++)
        {
            result.Add(collection[indexes[i]]);
        }

        return result;
    }

    private static bool IsValidSourceUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
            return false;

        string[] allowedSchemes = ["https"];

        if (!allowedSchemes.Contains(uri.Scheme))
            return false;

        return true;
    }

    private static PromptAttributeValueTypeEnum GetPromptAttributeValueType(AttributeValueTypeEnum value)
    {
        switch (value)
        {
            case AttributeValueTypeEnum.Bool:
                return PromptAttributeValueTypeEnum.Boolean;
            case AttributeValueTypeEnum.IntegerNumber:
                return PromptAttributeValueTypeEnum.Integer;
            case AttributeValueTypeEnum.RealNumber:
                return PromptAttributeValueTypeEnum.Decimal;
            case AttributeValueTypeEnum.Text:
                return PromptAttributeValueTypeEnum.Text;
            case AttributeValueTypeEnum.RichText:
                return PromptAttributeValueTypeEnum.FormattedText;
            case AttributeValueTypeEnum.Date:
                return PromptAttributeValueTypeEnum.Date;
            case AttributeValueTypeEnum.DateRange:
                return PromptAttributeValueTypeEnum.DateRange;
            case AttributeValueTypeEnum.Select:
                return PromptAttributeValueTypeEnum.SingleChoice;
            case AttributeValueTypeEnum.MultiSelect:
            case AttributeValueTypeEnum.StringArray:
                return PromptAttributeValueTypeEnum.MultiChoice;
            default:
                throw new InvalidOperationException($"Unknown attribute value type: {value}");
        }
    }
}
