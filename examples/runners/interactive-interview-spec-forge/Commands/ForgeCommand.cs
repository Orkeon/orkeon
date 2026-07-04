using Microsoft.Extensions.Logging;
using Orkeon.Cli.Abstractions.Commands;
using Orkeon.Examples.Shared;
using Orkeon.Hosting;
using Orkeon.Infrastructure.DependencyInjection;

namespace Orkeon.Examples.Interactive.InterviewSpecForge.Commands;

/// <summary>
/// Runs the interview-spec-forge crew against one transcript by slot.
/// Stages /input (transcript.txt + previous round's outputs for topics/tasks/glossary
/// + Sqlite memory db) before invoking <c>RunOneShotAsync</c> with mounts
/// <c>/input:ro</c> + <c>/output:rw</c>. Writes deliverables under
/// <c>rounds/round-NN-&lt;person-subject&gt;/attempt-MM/</c>.
/// </summary>
/// <remarks>
/// // EXCEPTION-BOOTSTRAP — stages files on physical paths before delegating
/// to <c>RunOneShotAsync</c>, which itself bootstraps the VFS for the inner host.
/// D21 acté : staging en C# côté runner (helper partagé entre forge et replay).
/// </remarks>
public sealed class ForgeCommand : IInteractiveCommand
{
    private readonly TranscriptsCatalog _catalog;
    private readonly ILogger<ForgeCommand> _logger;

    public ForgeCommand(TranscriptsCatalog catalog, ILogger<ForgeCommand> logger)
    {
        _catalog = catalog;
        _logger = logger;
    }

    public string Name => "forge";
    public IReadOnlyList<string> Aliases => new[] { "f" };
    public string Description => "forge <slot> — run the crew (with humanInput gate) on one transcript";

    public async Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var raw = context.Args is [var first, ..] ? first : null;
        if (string.IsNullOrEmpty(raw))
            return CommandResult.Continue("Usage: forge <slot>  (e.g. 'forge 001')");

        var entry = _catalog.Resolve(raw);
        if (entry is null)
            return CommandResult.Continue($"Unknown slot '{raw}'. Try 'list' to see available slots.");

        var roundDir = _catalog.ResolveRoundDir(entry);
        var attemptDir = TranscriptsCatalog.ResolveNextAttemptDir(roundDir);
        var stageDir = Path.Combine(attemptDir, "input-stage");
        var outputDir = Path.Combine(attemptDir, "outputs");
        var logsDir = Path.Combine(attemptDir, "logs");

        Directory.CreateDirectory(stageDir);
        Directory.CreateDirectory(outputDir);
        Directory.CreateDirectory(logsDir);

        ForgeStaging.StageInputs(
            transcriptPath: entry.FilePath,
            stageDir: stageDir,
            roundsRoot: _catalog.RoundsRoot,
            currentRoundDir: roundDir,
            currentAttemptDir: attemptDir,
            logger: _logger);

        context.Console.WriteLine("");
        context.Console.WriteLine($"  ── forge {entry.Slot} ({entry.Basename}) ──");
        context.Console.WriteLine($"     attempt  : {attemptDir}");
        context.Console.WriteLine($"     config   : {_catalog.ConfigPath}");
        context.Console.WriteLine($"     gate     : humanInput=true on task 3 (await_cr_validation)");
        context.Console.WriteLine("");

        var opts = new KickoffOptions
        {
            ConfigPath = _catalog.ConfigPath,
            SettingsPath = _catalog.SettingsPath,
            Mounts = new[]
            {
                $"{stageDir}:/input:ro",
                $"{outputDir}:/output:rw",
            },
            AllowExternalMounts = true,
            Verbose = _catalog.Verbose,
            LlmLogEnabled = _catalog.LlmLogEnabled,
            LlmLogPath = _catalog.LlmLogEnabled ? logsDir : null,
            Variables = new[]
            {
                $"transcript_basename={entry.Basename}",
                $"now_iso={DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}",
            },
        };

        try
        {
            var exit = await RunnerExecution
                .RunOneShotAsync(opts, "Orkeon.Examples.Interactive.InterviewSpecForge.Forge",
                    configureServices: (_, services) =>
                    {
                        services.AddSemanticSearchTool();
                        services.AddOrkeonHumanInput();
                        services.AddTerminalGuiHumanInput();
                    },
                    externalCt: cancellationToken)
                .ConfigureAwait(false);

            if (exit == 0)
            {
                ForgeStaging.PostProcessTaskUuids(outputDir, stageDir, _logger);
                ForgeStaging.PersistOutputsToRoot(outputDir, _catalog, _logger);
            }

            context.Console.WriteLine("");
            return exit switch
            {
                0   => CommandResult.Continue($"  ok — artefacts in {outputDir}"),
                130 => CommandResult.Continue("  canceled"),
                _   => CommandResult.Continue($"  failed (exit={exit}) — see {logsDir} and {Path.Combine(attemptDir, "runner-output.txt")}"),
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Forge failed for transcript-{Slot}", entry.Slot);
            return CommandResult.Continue($"  error: {ex.Message}");
        }
    }
}
