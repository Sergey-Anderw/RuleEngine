using Dapper;
using HC.Packages.Persistent;
using HC.Packages.Persistent.Extensions;
using HC.Packages.Persistent.Infrastructure;
using Pgvector.Dapper;

namespace HC.AiProcessor.Infrastructure.Extensions;

public static class HostApplicationBuilderExtensions
{
    private static readonly JsonSerializerOptions DefaultOptions
        = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.BasicLatin, UnicodeRanges.Cyrillic),
            WriteIndented = true
        };

    public static T AddPersistenceInfrastructure<T>(
        this T builder,
        string connectionStringName) where T : IHostApplicationBuilder
    {
        SqlMapper.AddTypeHandler(new VectorTypeHandler());

        string? migrationsAssembly = typeof(HostApplicationBuilderExtensions).GetTypeInfo().Assembly.GetName().Name;
        builder.AddPersistentPostgres<T, AiProcessorDbContext>(connectionStringName);

        builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
        builder.Services.AddRepositories(typeof(AiProcessorDbContext).Assembly);
        builder.Services
            .AddDbContext<AiProcessorDbContext>(options =>
            {
                options.UseNpgsql(
                    builder.Configuration.GetConnectionString(connectionStringName),
                    npgsqlOptionsAction: optionsBuilder =>
                    {
                        optionsBuilder.MigrationsAssembly(migrationsAssembly);
                        optionsBuilder.UseVector();
                        //Configuring Connection Resiliency: https://docs.microsoft.com/en-us/ef/core/miscellaneous/connection-resiliency
                        optionsBuilder.EnableRetryOnFailure(
                            maxRetryCount: 15,
                            maxRetryDelay: TimeSpan.FromSeconds(30),
                            errorCodesToAdd: null);
                    });

                options.UseNpgsqlJsonSerializerOptions(DefaultOptions);
            });

        return builder;
    }
}
