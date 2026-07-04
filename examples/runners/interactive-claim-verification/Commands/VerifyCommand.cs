using Microsoft.Extensions.Logging;
using Orkeon.Cli.Abstractions.Commands;
using Orkeon.Hosting;

namespace Orkeon.Examples.Interactive.ClaimVerification.Commands;

/// <summary>
/// Runs the claim-verifier crew against one claim by slot. Each invocation
/// builds a fresh kickoff host with the per-run mounts (<c>/input:ro</c>,
/// <c>/output:rw</c>) and writes deliverables under
/// <c>rounds/interactive-&lt;timestamp&gt;-&lt;slot&gt;-&lt;slug&gt;/</c>.
/// </summary>
/// <remarks>
/// // EXCEPTION-BOOTSTRAP — stages a claim file and creates per-run output
/// directories on physical paths before delegating to <c>RunOneShotAsync</c>,
/// which itself bootstraps the VFS for the inner host.
/// </remarks>
public sealed class VerifyCommand : IInteractiveCommand
{
    private readonly ClaimsCatalog _catalog;
    private readonly ILogger<VerifyCommand> _logger;

    public VerifyCommand(ClaimsCatalog catalog, ILogger<VerifyCommand> logger)
    {
        _catalog = catalog;
        _logger = logger;
    }

    public string Name => "verify";
    public IReadOnlyList<string> Aliases => new[] { "v" };
    public string Description => "verify <slot> — run the crew on one claim and write the verdict";

    public async Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var raw = context.Args.Count > 0 ? context.Args[0] : null;
        if (string.IsNullOrEmpty(raw))
            return CommandResult.Continue("Usage: verify <slot>  (e.g. 'verify 001')");

        var entry = _catalog.Resolve(raw);
        if (entry is null)
            return CommandResult.Continue($"Unknown slot '{raw}'. Try 'list' to see available slots.");

        var runId = $"interactive-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{entry.Slot}-{entry.Slug}";
        var runDir = Path.Combine(_catalog.RoundsRoot, runId);
        var inputDir = Path.Combine(runDir, "input");
        var outputDir = Path.Combine(runDir, "outputs");
        var logsDir = Path.Combine(runDir, "logs");

        Directory.CreateDirectory(inputDir);
        Directory.CreateDirectory(outputDir);
        Directory.CreateDirectory(logsDir);
        File.Copy(entry.FilePath, Path.Combine(inputDir, "claim.md"), overwrite: true);

        context.Console.WriteLine("");
        context.Console.WriteLine($"  ── verify claim-{entry.Slot} ({entry.Slug}) ──");
        context.Console.WriteLine($"     run dir : {runDir}");
        context.Console.WriteLine("");

        var opts = new KickoffOptions
        {
            ConfigPath = _catalog.ConfigPath,
            SettingsPath = _catalog.SettingsPath,
            Mounts = new[]
            {
                $"{inputDir}:/input:ro",
                $"{outputDir}:/output:rw",
            },
            AllowExternalMounts = true,
            Verbose = _catalog.Verbose,
            LlmLogEnabled = _catalog.LlmLogEnabled,
            LlmLogPath = _catalog.LlmLogEnabled ? logsDir : null,
        };

        try
        {
            // Pass cancellationToken through so Ctrl+C in the TUI propagates into the
            // inner crew kickoff (without this, the inner host has no way to honour
            // outer cancellation — TUI mode disables SIGINT which is the only path
            // RunOneShotAsync's RegisterGracefulShutdown listens to by default).
            var exit = await RunnerExecution
                .RunOneShotAsync(opts, "Orkeon.Examples.Interactive.ClaimVerification.Verify",
                    externalCt: cancellationToken)
                .ConfigureAwait(false);

            context.Console.WriteLine("");
            return exit switch
            {
                0   => CommandResult.Continue($"  ok — verdict in {outputDir}"),
                130 => CommandResult.Continue("  canceled"),
                _   => CommandResult.Continue($"  failed (exit={exit}) — see {logsDir} and {Path.Combine(runDir, "runner-output.txt")}"),
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Verify failed for claim-{Slot}", entry.Slot);
            return CommandResult.Continue($"  error: {ex.Message}");
        }
    }
}
