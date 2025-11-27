using System.Text;
using HC.AiProcessor.Application.Extensions;
using HC.AiProcessor.Application.Models;
using HC.AiProcessor.CLI.Commands;
using HC.AiProcessor.Infrastructure.Extensions;
using HC.Packages.Common.Contracts.V1;
using HC.Packages.Common.Extensions;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HC.AiProcessor.CLI;

[Command("host", UnrecognizedArgumentHandling = UnrecognizedArgumentHandling.CollectAndContinue)]
[Subcommand(
    typeof(EmbeddingsCommand),
    typeof(ProductsCommand))]
public class Program
{
    public static async Task<int> Main(params string[] args)
    {
        Console.OutputEncoding = Encoding.Unicode;

        using IHost host = CreateHostApplicationBuilder(args).Build();
        await host.StartAsync();

        var logger = host.Services.GetRequiredService<ILogger<Program>>();

        try
        {
            var app = host.Services.GetRequiredService<CommandLineApplication>();
            app.Conventions.UseConstructorInjection(host.Services);

            return await app.ExecuteAsync(args);
        }
        catch (CommandParsingException ex)
        {
            logger.LogError(ex, $"Invalid command: {ex.Message}");
            return AppConstants.FailCode;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Unhandled error: {ex.Message}");
            return AppConstants.FailCode;
        }
        finally
        {
            await host.StopAsync();
        }
    }

    private void OnExecute(CommandLineApplication app)
    {
        string[] commandNames =
        [
            EmbeddingsCommand.CommandName,
            ProductsCommand.CommandName
        ];
        Console.WriteLine(
            $"Please specify a subcommand (e.g., {string.Join(", ", commandNames[..^1])} or {commandNames[^1]}).");
        app.ShowHelp();
    }

    private static HostApplicationBuilder CreateHostApplicationBuilder(string[] args)
    {
        var app = new CommandLineApplication<Program>();
        app.Conventions.UseDefaultConventions();

        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

        builder.Configuration.AddHostConfiguration();

        builder.Services.Configure<AiProcessorConfig>(
            builder.Configuration.GetSection(nameof(AiProcessorConfig)));

        builder
            .AddAndConfigureMediatR()
            .AddAndConfigureLogging()
            .AddPersistenceInfrastructure("DBConnection")
            .AddAndConfigureServices();

        builder.Services.AddSingleton<CommandLineApplication>(app);
        builder.Services.AddSingleton<IIdentityContextUser, DummyIdentityContextUser>();

        return builder;
    }

    private sealed class DummyIdentityContextUser : IIdentityContextUser
    {
        public Guid GetUserId() => Guid.Empty;
        public Guid GetUserIdOrEmpty() => Guid.Empty;
    }
}
