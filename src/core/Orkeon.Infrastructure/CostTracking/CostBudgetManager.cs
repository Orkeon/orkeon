using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Common;

namespace Orkeon.Infrastructure.CostTracking;

/// <summary>
/// Manages cost tracking, budget enforcement, and reporting for LLM usage.
/// Thread-safe implementation using concurrent collections.
/// </summary>
public sealed partial class CostBudgetManager : ICostBudgetManager
{
    private readonly ConcurrentBag<CostUsageEvent> _history = [];
    private readonly ConcurrentDictionary<string, BudgetLimit> _crewBudgets = new();
    private readonly ConcurrentDictionary<string, BudgetLimit> _agentBudgets = new();
    private readonly ConcurrentBag<BudgetAlert> _alerts = [];
    private readonly IModelPricingRegistry _pricingRegistry;
    private readonly ILogger<CostBudgetManager> _logger;
    private readonly CostTrackingOptions _options;

    /// <summary>Raised when a budget alert threshold is crossed.</summary>
    public event EventHandler<BudgetAlertEventArgs>? OnBudgetAlert;

    /// <summary>Initializes a new instance of <see cref="CostBudgetManager"/>.</summary>
    /// <param name="pricingRegistry">The model pricing registry.</param>
    /// <param name="options">The cost tracking options.</param>
    /// <param name="logger">The logger.</param>
    public CostBudgetManager(
        IModelPricingRegistry pricingRegistry,
        IOptions<CostTrackingOptions> options,
        ILogger<CostBudgetManager> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _pricingRegistry = pricingRegistry;
        _logger = logger;
        _options = options.Value;
    }

    /// <inheritdoc />
    public CostBudgetCheckResult RecordUsage(CostUsageEvent usageEvent)
    {
        ArgumentNullException.ThrowIfNull(usageEvent);
        // Auto-calculate cost if not provided
        var cost = usageEvent.Cost;
        if (cost == 0m && (usageEvent.PromptTokens > 0 || usageEvent.CompletionTokens > 0))
        {
            cost = _pricingRegistry.CalculateCost(usageEvent.Model, usageEvent.PromptTokens, usageEvent.CompletionTokens);
        }

        // Store the event (with calculated cost)
        var eventWithCost = usageEvent with { Cost = cost };
        _history.Add(eventWithCost);

        LogRecordedUsageCrewAgentModel(usageEvent.CrewId ?? "", usageEvent.AgentId ?? "", usageEvent.Model ?? "", usageEvent.PromptTokens + usageEvent.CompletionTokens, cost);

        // Check budgets
        return CheckBudgets(eventWithCost);
    }

    /// <inheritdoc />
    public CostReport GetReport(string? crewId = null, string? agentId = null)
    {
        var events = FilterEvents(crewId, agentId);
        return BuildReport(events);
    }

    /// <inheritdoc />
    public CostReport GetReportForPeriod(DateTime from, DateTime toDate, string? crewId = null)
    {
        var events = _history
            .Where(e => e.Timestamp >= from && e.Timestamp <= toDate);

        if (crewId is not null)
            events = events.Where(e => e.CrewId == crewId);

        var list = events.ToList();
        var report = BuildReport(list);

        return report with { PeriodStart = from, PeriodEnd = toDate };
    }

    /// <inheritdoc />
    public void SetCrewBudget(string crewId, BudgetLimit budget)
    {
        ArgumentNullException.ThrowIfNull(budget);
        _crewBudgets[crewId] = budget;
        LogSetCrewBudgetForMaxcost(crewId, budget.MaxCostUsd ?? 0m, budget.MaxTokens ?? 0);
    }

    /// <inheritdoc />
    public void SetAgentBudget(string crewId, string agentId, BudgetLimit budget)
    {
        ArgumentNullException.ThrowIfNull(budget);
        var key = MakeAgentKey(crewId, agentId);
        _agentBudgets[key] = budget;
        LogSetAgentBudgetForMaxcost(crewId, agentId, budget.MaxCostUsd ?? 0m, budget.MaxTokens ?? 0);
    }

    /// <inheritdoc />
    public IReadOnlyList<BudgetAlert> GetActiveAlerts()
    {
        return _alerts.ToList().AsReadOnly();
    }

    private CostBudgetCheckResult CheckBudgets(CostUsageEvent current)
    {
        var crewResult = CheckCrewBudget(current);
        if (crewResult is not null)
            return crewResult;

        var agentResult = CheckAgentBudget(current);
        if (agentResult is not null)
            return agentResult;

        return CostBudgetCheckResult.WithinBudget();
    }

    private CostBudgetCheckResult? CheckCrewBudget(CostUsageEvent current)
    {
        if (string.IsNullOrEmpty(current.CrewId))
            return null;

        var crewBudget = ResolveCrewBudget(current.CrewId);
        if (crewBudget is null)
            return null;

        var crewEvents = _history.Where(e => e.CrewId == current.CrewId).ToList();
        var result = CheckBudgetLimits(crewBudget, crewEvents, current.CrewId, null);
        return result.IsWithinBudget ? null : result;
    }

    private BudgetLimit? ResolveCrewBudget(string crewId)
    {
        if (_crewBudgets.TryGetValue(crewId, out var explicitBudget))
            return explicitBudget;
        return _options.DefaultCrewBudget;
    }

    private CostBudgetCheckResult? CheckAgentBudget(CostUsageEvent current)
    {
        if (string.IsNullOrEmpty(current.CrewId) || string.IsNullOrEmpty(current.AgentId))
            return null;

        var agentKey = MakeAgentKey(current.CrewId, current.AgentId);
        if (!_agentBudgets.TryGetValue(agentKey, out var agentBudget))
            return null;

        var agentEvents = _history
            .Where(e => e.CrewId == current.CrewId && e.AgentId == current.AgentId)
            .ToList();
        var result = CheckBudgetLimits(agentBudget, agentEvents, current.CrewId, current.AgentId);
        return result.IsWithinBudget ? null : result;
    }

    private CostBudgetCheckResult CheckBudgetLimits(
        BudgetLimit budget,
        List<CostUsageEvent> events,
        string crewId,
        string? agentId)
    {
        var totalCost = events.Sum(e => e.Cost);
        var totalTokens = events.Sum(e => e.PromptTokens + e.CompletionTokens);
        var scope = agentId is not null ? $"agent '{agentId}' in crew '{crewId}'" : $"crew '{crewId}'";

        var costResult = CheckCostBudget(budget, totalCost, crewId, agentId, scope);
        if (costResult is not null)
            return costResult;

        var tokenResult = CheckTokenBudget(budget, totalTokens, crewId, agentId, scope);
        if (tokenResult is not null)
            return tokenResult;

        var rateResult = CheckRateLimitBudget(budget, events, crewId, agentId, scope);
        if (rateResult is not null)
            return rateResult;

        return CostBudgetCheckResult.WithinBudget();
    }

    private CostBudgetCheckResult? CheckCostBudget(
        BudgetLimit budget, decimal totalCost, string crewId, string? agentId, string scope)
    {
        if (!budget.MaxCostUsd.HasValue || budget.MaxCostUsd.Value <= 0)
            return null;

        var costPercent = totalCost / budget.MaxCostUsd.Value * 100m;

        if (totalCost > budget.MaxCostUsd.Value)
        {
            var alert = CreateAlert(crewId, agentId, BudgetAlertType.CostBudgetExceeded,
                totalCost, budget.MaxCostUsd.Value, costPercent);
            RaiseAlert(alert);
            return CostBudgetCheckResult.BudgetExceeded(
                Inv.Format($"Cost budget exceeded for {scope}: ${totalCost:F4} > ${budget.MaxCostUsd.Value:F4}"), alert);
        }

        var threshold = budget.AlertThresholdPercent ?? 80m;
        if (costPercent >= threshold)
        {
            var alert = CreateAlert(crewId, agentId, BudgetAlertType.CostThresholdWarning,
                totalCost, budget.MaxCostUsd.Value, costPercent);
            RaiseAlert(alert);
        }

        return null;
    }

    private CostBudgetCheckResult? CheckTokenBudget(
        BudgetLimit budget, long totalTokens, string crewId, string? agentId, string scope)
    {
        if (!budget.MaxTokens.HasValue || budget.MaxTokens.Value <= 0)
            return null;

        if (totalTokens <= budget.MaxTokens.Value)
            return null;

        var tokenPercent = (decimal)totalTokens / budget.MaxTokens.Value * 100m;
        var alert = CreateAlert(crewId, agentId, BudgetAlertType.TokenBudgetExceeded,
            totalTokens, budget.MaxTokens.Value, tokenPercent);
        RaiseAlert(alert);
        return CostBudgetCheckResult.BudgetExceeded(
            $"Token budget exceeded for {scope}: {totalTokens} > {budget.MaxTokens.Value}", alert);
    }

    private CostBudgetCheckResult? CheckRateLimitBudget(
        BudgetLimit budget, List<CostUsageEvent> events, string crewId, string? agentId, string scope)
    {
        if (!budget.MaxCallsPerMinute.HasValue || budget.MaxCallsPerMinute.Value <= 0)
            return null;

        var oneMinuteAgo = DateTime.UtcNow.AddMinutes(-1);
        var recentCalls = events.Count(e => e.Timestamp >= oneMinuteAgo);

        if (recentCalls <= budget.MaxCallsPerMinute.Value)
            return null;

        var alert = CreateAlert(crewId, agentId, BudgetAlertType.RateLimitExceeded,
            recentCalls, budget.MaxCallsPerMinute.Value,
            (decimal)recentCalls / budget.MaxCallsPerMinute.Value * 100m);
        RaiseAlert(alert);
        return CostBudgetCheckResult.BudgetExceeded(
            $"Rate limit exceeded for {scope}: {recentCalls} calls/min > {budget.MaxCallsPerMinute.Value}", alert);
    }

    private static BudgetAlert CreateAlert(
        string crewId, string? agentId, BudgetAlertType type,
        decimal currentUsage, decimal limit, decimal usagePercent)
    {
        return new BudgetAlert
        {
            CrewId = crewId,
            AgentId = agentId,
            Type = type,
            CurrentUsage = currentUsage,
            Limit = limit,
            UsagePercent = usagePercent
        };
    }

    private void RaiseAlert(BudgetAlert alert)
    {
        _alerts.Add(alert);
        LogBudgetAlertForCrewAgent(alert.Type, alert.CrewId, alert.AgentId ?? "(crew-level)", alert.UsagePercent);

        OnBudgetAlert?.Invoke(this, new BudgetAlertEventArgs { Alert = alert });
    }

    private IEnumerable<CostUsageEvent> FilterEvents(string? crewId, string? agentId)
    {
        IEnumerable<CostUsageEvent> events = _history;

        if (crewId is not null)
            events = events.Where(e => e.CrewId == crewId);

        if (agentId is not null)
            events = events.Where(e => e.AgentId == agentId);

        return events;
    }

    private static CostReport BuildReport(IEnumerable<CostUsageEvent> events)
    {
        var list = events.ToList();

        if (list.Count == 0)
        {
            return new CostReport
            {
                PeriodStart = DateTime.UtcNow,
                PeriodEnd = DateTime.UtcNow
            };
        }

        var byAgent = list
            .GroupBy(e => e.AgentId)
            .Where(g => !string.IsNullOrEmpty(g.Key))
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var totalCost = g.Sum(e => e.Cost);
                    var totalCalls = g.Count();
                    return new AgentCostSummary(
                        AgentId: g.Key,
                        TotalCost: totalCost,
                        TotalTokens: g.Sum(e => e.PromptTokens + e.CompletionTokens),
                        TotalCalls: totalCalls,
                        AverageCostPerCall: totalCalls > 0 ? totalCost / totalCalls : 0m);
                });

        var byModel = list
            .GroupBy(e => e.Model)
            .Where(g => !string.IsNullOrEmpty(g.Key))
            .ToDictionary(g => g.Key, g => g.Sum(e => e.Cost));

        var byOperationType = list
            .GroupBy(e => e.OperationType)
            .Where(g => !string.IsNullOrEmpty(g.Key))
            .ToDictionary(g => g.Key, g => g.Sum(e => e.Cost));

        return new CostReport
        {
            TotalCost = list.Sum(e => e.Cost),
            TotalTokens = list.Sum(e => e.PromptTokens + e.CompletionTokens),
            TotalCalls = list.Count,
            ByAgent = byAgent,
            ByModel = byModel,
            ByOperationType = byOperationType,
            PeriodStart = list.Min(e => e.Timestamp),
            PeriodEnd = list.Max(e => e.Timestamp)
        };
    }

    private static string MakeAgentKey(string crewId, string agentId) => $"{crewId}::{agentId}";

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Recorded usage: crew={CrewId}, agent={AgentId}, model={Model}, tokens={Tokens}, cost={Cost:F6}")]
    private partial void LogRecordedUsageCrewAgentModel(string crewId, string agentId, string model, int tokens, decimal cost);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Set crew budget for '{CrewId}': maxCost={MaxCost}, maxTokens={MaxTokens}")]
    private partial void LogSetCrewBudgetForMaxcost(string crewId, decimal maxCost, int maxTokens);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Set agent budget for '{CrewId}/{AgentId}': maxCost={MaxCost}, maxTokens={MaxTokens}")]
    private partial void LogSetAgentBudgetForMaxcost(string crewId, string agentId, decimal maxCost, int maxTokens);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Budget alert: {AlertType} for crew '{CrewId}' agent '{AgentId}' - usage {UsagePercent:F1}%")]
    private partial void LogBudgetAlertForCrewAgent(BudgetAlertType alertType, string crewId, string agentId, decimal usagePercent);

}
