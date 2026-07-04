using Microsoft.Extensions.Logging;
using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.Abstractions.Runners;
using Orkeon.Cli.Registry;

namespace Orkeon.Examples.Interactive.InterviewSpecForge;

/// <summary>
/// REPL for experiment 06 — interview-spec-forge. Pairs <see cref="DefaultCommandRegistry"/>
/// (help/exit/clear) with <see cref="InterviewSpecForgeCommandRegistry"/>
/// (list/show/forge/status/topics/tasks/glossary/replay).
/// </summary>
public sealed class InterviewSpecForgeRunner : InteractiveRunnerBase
{
    private readonly TranscriptsCatalog _catalog;

    public InterviewSpecForgeRunner(
        DefaultCommandRegistry defaults,
        InterviewSpecForgeCommandRegistry specific,
        IConsoleAdapter console,
        TranscriptsCatalog catalog,
        ILogger<InterviewSpecForgeRunner> logger,
        IServiceProvider services)
        : base(defaults, specific, console, logger, services)
    {
        _catalog = catalog;
    }

    protected override string Banner => """

╔════════════════════════════════════════════════════════════════╗
║   Orkeon — Experiment 06: Interview → Spec Forge               ║
║   (résumé + CR signé + indexation thèmes/tâches/glossaire)     ║
╚════════════════════════════════════════════════════════════════╝

  Type 'help' to list commands, 'list' to enumerate transcripts,
  'forge <slot>' to run the pipeline on one transcript (gate humain
  via prompt Approval), 'replay <slot>' for a re-run on an existing
  round, 'status' for corpus progress, 'topics'/'tasks'/'glossary'
  to inspect the cumulative collections.
  'search <requête>' to query the full corpus with semantic citations.
  'exit' to leave.

""";

    protected override string Prompt => "[interview-spec-forge] > ";

    protected override Task OnStartAsync(CancellationToken ct)
    {
        var entries = _catalog.Discover();
        Console.WriteLine($"  Transcripts root : {_catalog.TranscriptsRoot}");
        Console.WriteLine($"  Experiment root  : {_catalog.ExperimentRoot}");
        Console.WriteLine($"  Rounds root      : {_catalog.RoundsRoot}");
        Console.WriteLine($"  Crew config      : {_catalog.ConfigPath}");
        Console.WriteLine($"  Catalog          : {entries.Count} transcript(s) loaded.");
        Console.WriteLine("");
        return Task.CompletedTask;
    }
}
