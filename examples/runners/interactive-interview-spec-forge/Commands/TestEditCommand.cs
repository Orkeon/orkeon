using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Cli.Abstractions.Commands;
using Orkeon.Domain.Constants.HumanInput;
using Orkeon.Domain.HumanInput;
using Orkeon.Domain.Tools.Security;
using Orkeon.Examples.Shared;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.FileSystem;
using Orkeon.Infrastructure.Security;

namespace Orkeon.Examples.Interactive.InterviewSpecForge.Commands;

/// <summary>
/// Diagnostic command — pops the human-input choice + inline edit dialogs
/// directly against a VFS-mounted markdown file. Bypasses LLM + crew so the
/// TUI can be iterated on in seconds.
/// </summary>
/// <remarks>
/// Usage: <c>test-edit-dialog [path]</c> (alias: <c>ted</c>).
/// When no path is given, the command creates a sample markdown file under
/// <c>{experimentRoot}/dialog-test/sample.md</c> and uses it as the target.
/// Otherwise <paramref>path</paramref> must be an absolute file path on disk;
/// its parent directory is mounted as <c>/test</c> so the edit dialog reads
/// and writes via the standard <c>IFileSystemService</c>.
/// </remarks>
public sealed class TestEditCommand : IInteractiveCommand
{
    private readonly TranscriptsCatalog _catalog;

    public TestEditCommand(TranscriptsCatalog catalog) => _catalog = catalog;

    public string Name => "test-edit-dialog";
    public IReadOnlyList<string> Aliases => new[] { "ted" };
    public string Description => "Pop the human-input choice + inline edit dialog on a markdown file (no LLM).";

    public async Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        // Resolve target file
        string filePath;
        if (context.Args.Count == 0)
        {
            var dir = Path.Combine(_catalog.ExperimentRoot, "dialog-test");
            Directory.CreateDirectory(dir); // EXCEPTION-BOOTSTRAP — provisioning a one-shot mount for the test command
            filePath = Path.Combine(dir, "sample.md");
            if (!File.Exists(filePath)) // EXCEPTION-BOOTSTRAP
            {
                await File.WriteAllTextAsync(filePath,
                    "# Sample markdown\n\nEdit me freely and press **Approve & save**.\n\n"
                    + "- Line A\n- Line B\n- Line C\n\n```code\nconst x = 42;\n```\n",
                    cancellationToken).ConfigureAwait(false); // EXCEPTION-BOOTSTRAP
            }
        }
        else
        {
            filePath = Path.GetFullPath(context.Args[0]);
            if (!File.Exists(filePath)) // EXCEPTION-BOOTSTRAP
            {
                context.Console.WriteLine($"  ! file not found: {filePath}");
                return CommandResult.Continue();
            }
        }

        var parentDir = Path.GetDirectoryName(filePath)!;
        var fileName = Path.GetFileName(filePath);
        var virtualPath = $"/test/{fileName}";

        context.Console.WriteLine("");
        context.Console.WriteLine($"  Test target  : {filePath}");
        context.Console.WriteLine($"  Mount        : {parentDir} -> /test (rw)");
        context.Console.WriteLine($"  Virtual path : {virtualPath}");
        context.Console.WriteLine("  Popping dialog (no LLM)…");
        context.Console.WriteLine("");

        // Tiny scope: VFS + human-input + Terminal.Gui provider, nothing else.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Orkeon:FileSystem:Mounts:0"] = $"{parentDir}:/test:rw",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Information));
        // IPathValidator is a hard dep of FileSystemService — normally registered
        // by AddOrkeonInfrastructure(), but we don't want to pull the full stack
        // (LLM providers, memory providers, etc.) for a one-shot dialog test.
        services.AddOptions<PathSecurityOptions>().BindConfiguration("PathSecurity");
        services.AddSingleton<IPathValidator, PathValidator>();
        services.AddOrkeonFileSystem(configuration);
        services.AddOrkeonHumanInput();
        services.AddTerminalGuiHumanInput();

        await using var sp = services.BuildServiceProvider();
        var provider = sp.GetRequiredService<IHumanInputProvider>();

        var dialogContext = new HumanInputContext
        {
            Prompt = $"Test edit dialog on {virtualPath}\nClick 'edit_then_approve' to open the editor.",
            InputType = HumanInputDefaults.ChoiceInputType,
            Options = new[] { "approved", "rejected", "edit_then_approve" },
            Metadata = new Dictionary<string, object>
            {
                [HumanInputDefaults.EditFilePathMetadataKey] = virtualPath,
            },
        };

        try
        {
            var chosen = await provider.GetChoiceAsync(dialogContext, cancellationToken).ConfigureAwait(false);
            context.Console.WriteLine($"  Choice returned: {chosen}");
            var finalContent = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false); // EXCEPTION-BOOTSTRAP
            context.Console.WriteLine($"  File length after: {finalContent.Length} chars");
        }
        catch (Exception ex)
        {
            context.Console.WriteLine($"  ! dialog threw: {ex.GetType().Name}: {ex.Message}");
        }

        return CommandResult.Continue();
    }
}
