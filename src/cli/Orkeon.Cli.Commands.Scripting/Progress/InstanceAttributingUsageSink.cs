using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Cli.Commands.Scripting.Progress;

/// <summary>
/// Host-side <see cref="ILlmUsageSink"/> for the scripted REPL: every LLM usage event is
/// (a) forwarded to <see cref="ICostBudgetManager"/> — the session accounting behind the
/// status line's token readout and <c>/cost</c> — and (b) credited to the dispatch
/// instance ambient at call time (<see cref="ProgressAmbient.CurrentInstance"/>, the same
/// AsyncLocal correlation the progress channel uses), which is what puts a real
/// <c>↓ NN.Nk tokens</c> on that agent's row of the TUI agents pane.
/// </summary>
/// <remarks>
/// Foreground commands run with no ambient instance — their usage then only feeds the
/// session total, which is the correct attribution. Thread-safe: the cost manager guards
/// itself and <see cref="Dispatch.CommandInstance.AddUsage"/> is lock-free.
/// </remarks>
public sealed class InstanceAttributingUsageSink : ILlmUsageSink
{
    private readonly ICostBudgetManager? _costManager;

    /// <summary>Creates the sink; a null <paramref name="costManager"/> keeps per-instance attribution only.</summary>
    public InstanceAttributingUsageSink(ICostBudgetManager? costManager = null)
        => _costManager = costManager;

    /// <inheritdoc />
    public void Record(CostUsageEvent usage)
    {
        ArgumentNullException.ThrowIfNull(usage);
        // The cost the session settled (vendor charge, else registry price); without a manager, the vendor's charge.
        var cost = _costManager is null ? usage.Cost : _costManager.RecordUsage(usage).Cost;
        ProgressAmbient.CurrentInstance?.AddUsage(usage, cost);
    }
}
