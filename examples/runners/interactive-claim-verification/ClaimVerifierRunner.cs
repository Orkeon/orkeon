using Microsoft.Extensions.Logging;
using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.Abstractions.Runners;
using Orkeon.Cli.Registry;

namespace Orkeon.Examples.Interactive.ClaimVerification;

/// <summary>
/// REPL for experiment 05 — claim verification. Pairs <see cref="DefaultCommandRegistry"/>
/// (help/exit/clear) with <see cref="ClaimVerifierCommandRegistry"/> (list/show/verify/add-claim).
/// </summary>
public sealed class ClaimVerifierRunner : InteractiveRunnerBase
{
    private readonly ClaimsCatalog _catalog;

    public ClaimVerifierRunner(
        DefaultCommandRegistry defaults,
        ClaimVerifierCommandRegistry specific,
        IConsoleAdapter console,
        ClaimsCatalog catalog,
        ILogger<ClaimVerifierRunner> logger,
        IServiceProvider services)
        : base(defaults, specific, console, logger, services)
    {
        _catalog = catalog;
    }

    protected override string Banner => """

╔══════════════════════════════════════════════════════════╗
║   Orkeon — Experiment 05: Claim Verification (Debunking) ║
╚══════════════════════════════════════════════════════════╝

  Type 'help' to list commands, 'list' to enumerate claims,
  'verify <slot>' to debunk one. 'exit' to leave.

""";

    protected override string Prompt => "[claim-verifier] > ";

    protected override Task OnStartAsync(CancellationToken ct)
    {
        var entries = _catalog.Discover();
        Console.WriteLine($"  Claims root: {_catalog.ClaimsRoot}");
        Console.WriteLine($"  Rounds root: {_catalog.RoundsRoot}");
        Console.WriteLine($"  Crew config: {_catalog.ConfigPath}");
        Console.WriteLine($"  Catalog    : {entries.Count} claim(s) loaded.");
        Console.WriteLine("");
        return Task.CompletedTask;
    }
}
