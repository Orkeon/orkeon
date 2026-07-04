using Microsoft.Extensions.Logging;
using Orkeon.Cli.Abstractions.Commands;
using Orkeon.Examples.Shared;
using Orkeon.Hosting;
using Orkeon.Infrastructure.DependencyInjection;

namespace Orkeon.Examples.Interactive.InterviewSpecForge.Commands;

/// <summary>
/// Re-runs the forge pipeline on a transcript that already has at least one
/// attempt under <c>rounds/round-NN-...</c>. Allocates a new <c>attempt-MM</c>
/// (max+1) under the existing round directory (D10 acté) — the existing
/// attempts are preserved for diagnosis. Stages the transcript and the
/// previous round's outputs (NOT the previous attempt's outputs of the same
/// round, which would re-feed a possibly-bad CR back into indexation).
/// </summary>
/// <remarks>
/// // EXCEPTION-BOOTSTRAP — physical filesystem operations before/after the
/// per-forge VFS.
/// </remarks>
public sealed class ReplayCommand : IInteractiveCommand
{
    private readonly TranscriptsCatalog _catalog;
    private readonly ILogger<ReplayCommand> _logger;

    public ReplayCommand(TranscriptsCatalog catalog, ILogger<ReplayCommand> logger)
    {
        _catalog = catalog;
        _logger = logger;
    }

    public string Name => "replay";
    public IReadOnlyList<string> Aliases => Array.Empty<string>();
    public string Description => "replay <slot> — re-run the crew on an existing round (allocates attempt-MM = max+1)";

    public async Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var raw = context.Args is [var first, ..] ? first : null;
        if (string.IsNullOrEmpty(raw))
            return CommandResult.Continue("Usage: replay <slot>");

        var entry = _catalog.Resolve(raw);
        if (entry is null)
            return CommandResult.Continue($"Unknown slot '{raw}'. Try 'list' to see available slots.");

        var roundDir = _catalog.ResolveRoundDir(entry);
        if (!Directory.Exists(roundDir))
            return CommandResult.Continue(
                $"  No existing round for slot {entry.Slot}. Use 'forge {entry.Slot}' for the first run.");

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
        context.Console.WriteLine($"  ── replay {entry.Slot} ({entry.Basename}) ──");
        context.Console.WriteLine($"     attempt  : {attemptDir}");
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
                .RunOneShotAsync(opts, "Orkeon.Examples.Interactive.InterviewSpecForge.Replay",
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

            return exit switch
            {
                0   => CommandResult.Continue($"  ok — artefacts in {outputDir}"),
                130 => CommandResult.Continue("  canceled"),
                _   => CommandResult.Continue($"  failed (exit={exit}) — see {logsDir}"),
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Replay failed for transcript-{Slot}", entry.Slot);
            return CommandResult.Continue($"  error: {ex.Message}");
        }
    }
}
