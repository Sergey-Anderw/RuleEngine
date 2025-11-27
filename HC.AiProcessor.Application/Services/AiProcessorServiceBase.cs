using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using HC.AiProcessor.Application.Common;
using HC.AiProcessor.Application.Models;
using HC.AiProcessor.Entity.Ai;
using HC.AiProcessor.Entity.Ai.Enums;
using HC.AiProcessor.Infrastructure.Repositories.Ai;
using HC.Packages.Persistent.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel.Embeddings;
using Microsoft.SemanticKernel.TextGeneration;

namespace HC.AiProcessor.Application.Services;

internal abstract class AiProcessorServiceBase<TConfig, TSettings>(
    AiSettingsType aiSettingsType,
    IServiceProvider serviceProvider,
    ISystemClock clock,
    IOptions<AiProcessorConfig> aiProcessorConfigAccessor)
{
    private readonly IServiceProvider _serviceProvider =
        serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

    private readonly ISystemClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    private readonly AiProcessorConfig _config = aiProcessorConfigAccessor.Value;

    private readonly ConcurrentDictionary<long, ClientSettingsState> _clientSettingsStates = new();

    protected async Task TryLoadSettingsAsync(long clientId, CancellationToken ct)
    {
        DateTimeOffset settingsLastUpdate = default;

        if (_clientSettingsStates.TryGetValue(clientId, out ClientSettingsState? existingState))
        {
            TimeSpan elapsed = _clock.UtcNow - existingState.SettingsLastUpdate;
            if (elapsed < TimeSpan.FromSeconds(_config.AiSettingsTtlInSeconds))
            {
                return;
            }

            settingsLastUpdate = existingState.SettingsLastUpdate;
        }

        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        using var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var repository = scope.ServiceProvider.GetRequiredService<IAiSettingsRepository>();

        AiSettings? settings =
            await repository.GetLatestOrNull(clientId, aiSettingsType, settingsLastUpdate, ct);

        if (settings is null)
            return;

        var state = new ClientSettingsState
        {
            Config = settings.Config.Deserialize<TConfig>(JsonSettingsExtensions.Default)!,
            Settings = settings.Settings.Deserialize<Dictionary<string, TSettings?>>(JsonSettingsExtensions.Default)!,
            SettingsLastUpdate = settings.UpdatedAt ?? _clock.UtcNow
        };

        _clientSettingsStates.AddOrUpdate(clientId, state, (_, _) => state);
    }

    protected TConfig GetConfig(long clientId)
    {
        if (!_clientSettingsStates.TryGetValue(clientId, out ClientSettingsState? state) || state.Config is null)
            throw new Exception($"AI agent config for {clientId} was not found");
        return state.Config;
    }

    protected IReadOnlyDictionary<string, TSettings?> GetSettings(long clientId)
    {
        if (!_clientSettingsStates.TryGetValue(clientId, out ClientSettingsState? state) || state.Settings is null)
            throw new Exception($"AI setting for {clientId} was not found");
        return state.Settings;
    }

    private sealed class ClientSettingsState
    {
        public TConfig? Config { get; init; }
        public Dictionary<string, TSettings?>? Settings { get; init; }
        public DateTimeOffset SettingsLastUpdate { get; init; }
    }
}

internal abstract class AiProcessorChatCompletionServiceBase<TSettings>(
    AiSettingsType aiSettingsType,
    IServiceProvider serviceProvider,
    ISystemClock clock,
    IOptions<AiProcessorConfig> aiProcessorConfigAccessor,
    ITextGenerationServiceFactory textGenerationServiceFactory)
    : AiProcessorServiceBase<ChatCompletionConfig, TSettings>(
        aiSettingsType,
        serviceProvider,
        clock,
        aiProcessorConfigAccessor)
{
    private readonly ITextGenerationServiceFactory _textGenerationServiceFactory = textGenerationServiceFactory ??
        throw new ArgumentNullException(nameof(textGenerationServiceFactory));

    protected ITextGenerationService GetTextGenerationService(long clientId)
    {
        ChatCompletionConfig config = GetConfig(clientId);

        ITextGenerationService textGenerationService = _textGenerationServiceFactory.Create(config);
        return textGenerationService;
    }

    protected TSettings GetSettings(long clientId, string flow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(flow);

        if (!GetSettings(clientId).TryGetValue(flow, out TSettings? settings) || settings is null)
            throw new Exception($"AI settings for flow {flow} was not found");

        return settings;
    }
}

internal abstract class AiProcessorTextEmbeddingGenerationServiceBase<TSettings>(
    AiSettingsType aiSettingsType,
    IServiceProvider serviceProvider,
    ISystemClock clock,
    IOptions<AiProcessorConfig> aiProcessorConfigAccessor,
    ITextEmbeddingGenerationServiceFactory textEmbeddingGenerationServiceFactory)
    : AiProcessorServiceBase<ChatGptTextEmbeddingGenerationConfig, TSettings>(
        aiSettingsType,
        serviceProvider,
        clock,
        aiProcessorConfigAccessor)
{
    private readonly ITextEmbeddingGenerationServiceFactory _textEmbeddingGenerationServiceFactory =
        textEmbeddingGenerationServiceFactory ??
        throw new ArgumentNullException(nameof(textEmbeddingGenerationServiceFactory));

    [Experimental("SKEXP0001")]
    protected ITextEmbeddingGenerationService GetTextEmbeddingGenerationService(long clientId)
    {
        ChatGptTextEmbeddingGenerationConfig config = GetConfig(clientId);

        var textEmbeddingGenerationService = _textEmbeddingGenerationServiceFactory.Create(config);
        return textEmbeddingGenerationService;
    }
}
