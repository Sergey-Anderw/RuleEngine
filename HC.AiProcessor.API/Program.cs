using HC.Packages.Common.Contracts.V1;
using HC.Packages.Common.Extensions;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using HC.AiProcessor.Application.Extensions;
using HC.AiProcessor.Application.Models;
using HC.AiProcessor.Infrastructure.Extensions;
using HC.Packages.Auth;
using HC.Packages.Contracts.V1;
using HC.Packages.Monitoring.Health;
using HC.Packages.Services.API.Diagnostics;
using HC.Packages.Storage.Extensions;
using Microsoft.AspNetCore.Authentication.JwtBearer;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddHostConfiguration();

builder.Services.Configure<AiProcessorConfig>(
    builder.Configuration.GetSection(nameof(AiProcessorConfig)));
builder.Services.Configure<ImageToolClientSettings>(
    builder.Configuration.GetSection(nameof(ImageToolClientSettings)));

builder.Services.AddSingleton<IEndClientContext, OptionsSourceEndClientContext>();

builder.AddAndConfigureMediatR();
builder.AddAndConfigureLogging();
builder.ConfigureStore();
builder.AddPersistenceInfrastructure("DBConnection");
builder.AddTraceContext();
builder.AddUserAuth("JwtOptions");
builder.AddServiceAuth("UserServiceAuthOptions");
builder.Services.AddMemoryCache();
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Encoder = JavaScriptEncoder.Create(UnicodeRanges.All);
        options.JsonSerializerOptions.WriteIndented = true;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.JsonSerializerOptions.MaxDepth = int.MaxValue;
    });
builder.AddAndConfigureServices();

builder.Services.AddCors();
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly()));

builder.AddAndConfigureApiWithSwagger("AI Engine", 1.0, JwtBearerDefaults.AuthenticationScheme);

builder.Services.AddHealthChecks()
    .AddNpgSql(
        builder.Configuration.GetConnectionString("DBConnection")!,
        name: "npgsql-lm-platform",
        tags: [HealthCheckConstants.DefaultTags.Startup, HealthCheckConstants.DefaultTags.Ready]);

var app = builder.Build();

var pathBase = app.Configuration["PATH_BASE"] ?? "/api";
app.UsePathBase(pathBase);

if (app.Environment.IsDevelopment() || app.Environment.EnvironmentName == "Test")
{
    app.UseDeveloperExceptionPage();

    app.UseOpenApi(); // serve OpenAPI/Swagger documents
    app.UseSwaggerUI(); // serve Swagger UI
    app.UseReDoc(); // serve ReDoc UI
}
else
{
    app.UseHsts();
}

app.UseRouting();

app.UseCors(x => x
    .AllowAnyOrigin()
    .AllowAnyHeader()
    .AllowAnyMethod());

app.UseCorrelationId();
// app.UseHttpRequestLogging(context =>
// {
//     // This code determines whether specific HTTP requests should be excluded
//     // from logging or processing based on their paths and response status codes
//     var excludedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
//         HealthCheckConstants.Endpoints.Readiness,
//         HealthCheckConstants.Endpoints.Liveness,
//         HealthCheckConstants.Endpoints.Startup,
//     };
//
//     var isSuccessfulStatus = context.Response.StatusCode >= 200 && context.Response.StatusCode < 300;
//
//     return !excludedPaths.Contains(context.Request.Path) || !isSuccessfulStatus;
// });

app.UseApiExceptionHandler();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapK8SHealthEndpoints(extendedHealthReport: true);

app.Run();
