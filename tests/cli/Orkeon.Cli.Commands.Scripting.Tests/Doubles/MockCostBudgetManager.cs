using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Cli.Commands.Scripting.Tests.Doubles;

/// <summary>
/// Hand-written <see cref="ICostBudgetManager"/>: keeps what it records, answers <see cref="NextCost"/> as the
/// settled cost of each call and <see cref="Report"/> for every report.
/// </summary>
internal sealed class MockCostBudgetManager : ICostBudgetManager
{
    /// <summary>The events recorded, in order.</summary>
    public List<CostUsageEvent> Recorded { get; } = new();

    /// <summary>The cost each <see cref="RecordUsage"/> answers as settled; null for a call nobody priced.</summary>
    public decimal? NextCost { get; set; }

    /// <summary>The report <see cref="GetReport"/> answers.</summary>
    public CostReport Report { get; set; } = new();

    public event EventHandler<BudgetAlertEventArgs>? OnBudgetAlert { add { } remove { } }

    public CostBudgetCheckResult RecordUsage(CostUsageEvent usageEvent)
    {
        Recorded.Add(usageEvent);
        return CostBudgetCheckResult.WithinBudget() with { Cost = NextCost };
    }

    public CostReport GetReport(string? crewId = null, string? agentId = null) => Report;
    public CostReport GetReportForPeriod(DateTime from, DateTime toDate, string? crewId = null) => Report;
    public void SetCrewBudget(string crewId, BudgetLimit budget) { }
    public void SetAgentBudget(string crewId, string agentId, BudgetLimit budget) { }
    public IReadOnlyList<BudgetAlert> GetActiveAlerts() => Array.Empty<BudgetAlert>();
}
