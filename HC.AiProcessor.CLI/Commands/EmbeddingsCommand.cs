using McMaster.Extensions.CommandLineUtils;

namespace HC.AiProcessor.CLI.Commands;

[Command(CommandName)]
[Subcommand(typeof(SyncEmbeddingsCommand))]
internal sealed class EmbeddingsCommand
{
    public const string CommandName = "embeddings";

    private void OnExecute(CommandLineApplication app)
    {
        string[] commandNames =
        [
            SyncEmbeddingsCommand.CommandName
        ];

        if (commandNames.Length == 1)
        {
            Console.WriteLine($"Please specify a subcommand ({commandNames[^1]}).");
            app.ShowHelp();
            return;
        }

        Console.WriteLine(
            $"Please specify a subcommand (e.g., {string.Join(", ", commandNames[..^1])} or {commandNames[^1]}).");
        app.ShowHelp();
    }
}
