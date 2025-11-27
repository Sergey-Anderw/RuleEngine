using HC.Packages.Messaging.EventBus;
using HC.AiProcessor.Application.Extensions;
using HC.AiProcessor.Infrastructure;
using HC.Packages.Common.Contracts.V1;
using HC.Packages.Common.Extensions;
using HC.AiProcessor.Infrastructure.Extensions;
using HC.AiProcessor.Worker.Consumers;
using HC.AiProcessor.Worker.Models;
using HC.AiProcessor.Worker.Queue;
using HC.AiProcessor.Worker.Services;
using HC.AiProcessor.Worker.Workers;
using HC.Packages.Auth;
using HC.Packages.Contracts.V1;
using HC.Packages.Diagnostics;
using HC.Packages.Monitoring.Health;
using HC.Packages.Storage.Extensions;
using Microsoft.Extensions.Options;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddHostConfiguration();

builder.Services.Configure<AiProcessorQueueConfig>(
    builder.Configuration.GetSection(nameof(AiProcessorQueueConfig)));

builder.Services.Configure<OpenAiSettings>(
    builder.Configuration.GetSection(nameof(OpenAiSettings)));

builder.Services.AddSingleton<IEndClientContext, OptionsSourceEndClientContext>();
builder.Services.AddSingleton<IAiProcessorQueue, AiProcessorQueue>();
builder.Services.AddScoped<AiProcessorTaskCompletionSource>();
builder.Services.AddScoped<IAiProcessorTaskCompletionSource>(
    provider => provider.GetRequiredService<AiProcessorTaskCompletionSource>());
builder.Services.AddScoped<IAiProcessorTaskResultProvider>(
    provider => provider.GetRequiredService<AiProcessorTaskCompletionSource>());

builder.Services.AddHostedService<AiProcessorQueueWorker>();
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly()));

builder.Services
    .AddScoped<IProductAttributeEmbeddingRequestsCreator, ProductAttributeEmbeddingRequestsCreator>()
    .AddScoped<IProductRequestsCreator, ProductRequestsCreator>();

builder.AddAndConfigureMediatR();
builder.AddAndConfigureLogging();
builder.ConfigureStore();
builder.AddAndConfigureServices();
builder.AddPersistenceInfrastructure("DBConnection");
builder.AddUserAuth("JwtOptions");

// TODO: fix services resolving after migration to HC, should be rechecked (Roman Yefimchuk)

#region Trace context

builder.Services.AddScoped<ITraceContextFactory, TraceContextFactory>();
builder.Services.AddScoped<ITraceContextAccessor, TraceContextAccessor>();
builder.Services.AddScoped<IActivityFactory, ActivityFactory>();

builder.Services.AddSingleton(TelemetrySources.Source);

builder.Services
    .AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService(
            serviceName: builder.Environment.ApplicationName,
            serviceInstanceId: Environment.MachineName))
    .WithTracing(b =>
    {
        b.SetResourceBuilder(ResourceBuilder.CreateDefault()
                .AddService(builder.Environment.ApplicationName))
            .AddSource(TelemetrySources.Source.Name)
            .AddOtlpExporter();
    });

#endregion

await builder.AddAndConfigureRabbitMqAsync(
    "RabbitMQConnection",
    x =>
    {
        // TODO: temporary
        // x.AddConsumer<AiProcessorProductsCreatedConsumer>();
        // x.AddConsumer<AiProcessorProductsDeletedConsumer>();
        // x.AddConsumer<AiProcessorProductsUpdatedConsumer>();
        x.AddConsumer<ExternalAiProcessorBatchGenerateRequestConsumer>();
        x.AddConsumer<ExternalAiProcessorBatchPopulateAttributesRequestConsumer>();
        x.AddConsumer<ExternalAiProcessorBatchRephraseRequestConsumer>();
        x.AddConsumer<ExternalAiProcessorBatchTranslateRequestConsumer>();
        x.AddConsumer<ExternalAiProcessorDetermineProductFamilyRequestConsumer>();
        x.AddConsumer<ExternalAiProcessorGenerateRequestConsumer>();
        x.AddConsumer<ExternalAiProcessorPopulateAttributesRequestConsumer>();
        x.AddConsumer<ExternalAiProcessorRephraseRequestConsumer>();
        x.AddConsumer<ExternalAiProcessorTranslateRequestConsumer>();
    },
    busHealthTags: [HealthCheckConstants.DefaultTags.Startup, HealthCheckConstants.DefaultTags.Ready]);

builder.Services.AddHealthChecks()
    .AddNpgSql(
        builder.Configuration.GetConnectionString("DBConnection")!,
        name: "npgsql-lm-platform",
        tags: [HealthCheckConstants.DefaultTags.Startup, HealthCheckConstants.DefaultTags.Ready]);

var app = builder.Build();

app.SeedDatabase<AiProcessorDbContext>((dbContext, environment, services) =>
{
    var openAiConfigAccessor = services.GetRequiredService<IOptions<OpenAiSettings>>();
    OpenAiSettings openAiSettings = openAiConfigAccessor.Value;
    var variables = new Dictionary<string, string>
    {
        {
            "###OPEN_AI_SETTINGS.API_KEY###",
            string.IsNullOrWhiteSpace(openAiSettings.ApiKey) ? string.Empty : openAiSettings.ApiKey
        }
    };
    DbInitialization.Seed(dbContext, variables);
});
app.Run();
