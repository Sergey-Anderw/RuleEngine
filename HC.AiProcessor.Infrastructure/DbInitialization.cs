using System.Text;
using HC.AiProcessor.Entity.Ai;
using HC.AiProcessor.Entity.Ai.Enums;
using HC.AiProcessor.Infrastructure.Seed;

namespace HC.AiProcessor.Infrastructure;

public static class DbInitialization
{
    private static JsonSerializerOptions JsonSerializerOptions => new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static void Seed(AiProcessorDbContext dbContext, IReadOnlyDictionary<string, string> variables)
    {
        SeedAsync(dbContext, variables)
            .GetAwaiter()
            .GetResult();
    }

    public static async Task SeedAsync(AiProcessorDbContext dbContext, IReadOnlyDictionary<string, string> variables)
    {
        List<AiSettingsItem> seedData = await GetSeedDataAsync(variables);
        await SeedAiSettingsAsync(dbContext, seedData);
    }

    private static async Task<List<AiSettingsItem>> GetSeedDataAsync(IReadOnlyDictionary<string, string> variables)
    {
        string contentPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        string aiSettingsSeedPath = Path.Combine(contentPath, "Seed", "AiSettingsSeed.json");
        string aiSettingsSeed = await File.ReadAllTextAsync(aiSettingsSeedPath);

        var sb = new StringBuilder(aiSettingsSeed);

        foreach ((string key, string value) in variables)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;

            sb.Replace(key, value);
        }

        var seedData = JsonSerializer.Deserialize<List<AiSettingsItem>>(sb.ToString(), JsonSerializerOptions);
        return seedData ?? throw new JsonException("AI settings seed data cannot be deserialized");
    }

    private static async Task SeedAiSettingsAsync(AiProcessorDbContext dbContext, List<AiSettingsItem> seedData)
    {
        List<AiSettings> aiSettingsList = await dbContext.AiSettings.ToListAsync();

        foreach (AiSettingsItem seedItem in seedData)
        {
            List<AiSettings> aiSettingsListByType = aiSettingsList
                .Where(x => x.Type == seedItem.Type)
                .ToList();

            if (aiSettingsListByType.Count == 0)
            {
                AiSettings aiSettings = (await dbContext.Set<AiSettings>().AddAsync(new AiSettings())).Entity;
                aiSettings.ClientId = 1;
                aiSettings.Type = seedItem.Type;
                aiSettings.Status = AiSettingsStatusType.Enabled;
                aiSettings.Settings = seedItem.Settings;
                aiSettings.Config = seedItem.Config;
                dbContext.Entry(aiSettings).State = EntityState.Added;
            }
            else
            {
                foreach (AiSettings aiSettings in aiSettingsListByType
                             .Where(x => (x.UpdatedAt ?? x.CreatedAt) < seedItem.CreatedAt))
                {
                    aiSettings.Settings = seedItem.Settings;
                    aiSettings.Config = MergeConfigs(aiSettings.Config, seedItem.Config);
                    dbContext.Entry(aiSettings).State = EntityState.Modified;
                }
            }
        }

        if (!dbContext.ChangeTracker.HasChanges())
            return;

        await dbContext.SaveChangesAsync();
    }

    private static JsonObject MergeConfigs(JsonObject oldConfig, JsonObject newConfig)
    {
        var config = (JsonObject) oldConfig.DeepClone();
        string[] protectedKeys = ["ApiKey"];

        foreach ((string key, JsonNode? value) in newConfig)
        {
            if (protectedKeys.Contains(key))
            {
                continue;
            }

            config[key] = value?.DeepClone();
        }

        return config;
    }
}
