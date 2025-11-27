using System.Text.Json;
using HC.AiProcessor.Application.Models;
using HC.AiProcessor.Application.Services;
using HC.AiProcessor.Entity.Ai;
using HC.AiProcessor.Entity.Ai.Enums;
using HC.AiProcessor.Infrastructure.Repositories.Ai;
using Microsoft.Extensions.DependencyInjection;

namespace HC.AiProcessor.Application.v1.Commands;

public record MapOptionsCommand(
    IReadOnlyCollection<OptionsMappingInput> Request
) : IRequest<MapOptionsCommandResult>;

public record MapOptionsCommandResult(
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Results,
    IReadOnlyCollection<string> UnmappedCodes);

public class MapOptionsCommandHandler(
    IServiceProvider serviceProvider,
    IAiSettingsRepository aiSettingsRepository
) : IRequestHandler<MapOptionsCommand, MapOptionsCommandResult>
{
    public async Task<MapOptionsCommandResult> Handle(
        MapOptionsCommand command,
        CancellationToken cancellationToken)
    {
        AiSettings aiSettings =
            await aiSettingsRepository.GetLatestOrNull(
                clientId: 1,
                type: AiSettingsType.AttributesPopulationChatGpt,
                updatedAt: default,
                cancellationToken) ?? throw new InvalidOperationException();

        var config = aiSettings.Config.Deserialize<AttributesPopulationConfig>()!;
        var settings = aiSettings.Settings.Deserialize<Dictionary<string, AttributesPopulationSettings>>()!["default"];

        var optionsMappingProcessor = ActivatorUtilities.CreateInstance<SyncOptionsMappingProcessor>(
            serviceProvider,
            (GetOptionsMappingSettingsAsync) GetOptionsMappingSettingsAsync);

        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> results =
            await optionsMappingProcessor.ProcessAsync(
                command.Request,
                config: new ChatCompletionConfig
                {
                    Model = string.IsNullOrWhiteSpace(config.OptionsMappingModel)
                        ? config.Model
                        : config.OptionsMappingModel,
                    ApiKey = config.ApiKey
                },
                cancellationToken);

        var unmappedCodes = new List<string>();

        foreach (OptionsMappingInput input in command.Request)
        {
            if (!results.ContainsKey(input.Code))
            {
                unmappedCodes.Add(input.Code);
            }
        }

        return new MapOptionsCommandResult(results, unmappedCodes);

        Task<OptionsMappingSettings> GetOptionsMappingSettingsAsync(CancellationToken _) =>
            Task.FromResult(new OptionsMappingSettings
            {
                SystemPrompt = settings.OptionsMappingSetupPrompt,
                UserPrompt = settings.OptionsMappingPrompt
            });
    }
}
