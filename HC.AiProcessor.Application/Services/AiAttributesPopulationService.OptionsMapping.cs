using HC.AiProcessor.Application.Models;
using Microsoft.Extensions.DependencyInjection;

namespace HC.AiProcessor.Application.Services;

internal sealed partial class ChatGptAttributesPopulationService
{
    private IOptionsMappingProcessor CreateSyncOptionsMappingProcessor(AttributesPopulationSettings settings)
    {
        return ActivatorUtilities.CreateInstance<SyncOptionsMappingProcessor>(
            _serviceProvider,
            (GetOptionsMappingSettingsAsync) GetOptionsMappingSettingsAsync);

        Task<OptionsMappingSettings> GetOptionsMappingSettingsAsync(CancellationToken _) =>
            Task.FromResult(new OptionsMappingSettings
            {
                SystemPrompt = settings.OptionsMappingSetupPrompt,
                UserPrompt = settings.OptionsMappingPrompt
            });
    }

    private IOptionsMappingProcessor CreateAsyncOptionsMappingProcessor(AttributesPopulationSettings settings)
    {
        return ActivatorUtilities.CreateInstance<AsyncOptionsMappingProcessor>(
            _serviceProvider,
            (GetOptionsMappingSettingsAsync) GetOptionsMappingSettingsAsync);

        Task<OptionsMappingSettings> GetOptionsMappingSettingsAsync(CancellationToken _) =>
            Task.FromResult(new OptionsMappingSettings
            {
                SystemPrompt = settings.OptionsMappingSetupPrompt,
                UserPrompt = settings.OptionsMappingPrompt
            });
    }

    private static async Task<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>>
        GetOptionsMappingResultsAsync(
            IOptionsMappingProcessor optionsMappingProcessor,
            List<UnrecognizedSelectableAttributePopulationResult> unrecognizedResults,
            AttributesPopulationContext context,
            CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<OptionsMappingInput> optionsMappingInputs = GetOptionsMappingInputs(unrecognizedResults);

        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> optionsMappingResults =
            await optionsMappingProcessor.ProcessAsync(
                optionsMappingInputs,
                config: new ChatCompletionConfig
                {
                    Model = string.IsNullOrWhiteSpace(context.Config.OptionsMappingModel)
                        ? context.Config.Model
                        : context.Config.OptionsMappingModel,
                    ApiKey = context.Config.ApiKey
                },
                cancellationToken);

        return optionsMappingResults;
    }

    private static IReadOnlyCollection<OptionsMappingInput> GetOptionsMappingInputs(
        List<UnrecognizedSelectableAttributePopulationResult> unrecognizedResults)
    {
        var inputs = new List<OptionsMappingInput>();

        foreach (UnrecognizedSelectableAttributePopulationResult unrecognizedResult in unrecognizedResults)
        {
            OptionsMappingInput? input =
                inputs.FirstOrDefault(x => x.Code == unrecognizedResult.Code);

            if (input is null)
            {
                input = new OptionsMappingInput
                {
                    Code = unrecognizedResult.Code,
                    Label = unrecognizedResult.Label,
                    Options = unrecognizedResult.Options,
                    Values = []
                };
                inputs.Add(input);
            }

            List<string> values = unrecognizedResult.Values;

            foreach (string value in values.Where(value => input.Values.All(v => v != value)))
            {
                input.Values.Add(value);
            }
        }

        return inputs;
    }
}
