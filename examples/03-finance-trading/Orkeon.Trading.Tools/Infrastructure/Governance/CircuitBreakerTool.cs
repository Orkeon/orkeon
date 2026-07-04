using Orkeon.Trading.Tools.Infrastructure.Base;
using Orkeon.Trading.Tools.Infrastructure.Governance.Contracts;
using Microsoft.Extensions.Logging;

namespace Orkeon.Trading.Tools.Infrastructure.Governance;

/// <summary>
/// Circuit breaker tool for risk limit enforcement and automatic trading halts.
/// Monitors portfolio metrics and triggers halts when limits are breached.
/// </summary>
public class CircuitBreakerTool(ILogger<CircuitBreakerTool>? logger = null)
    : TradingToolBase<CircuitBreakerRequest, CircuitBreakerResponse>(logger)
{

    protected override string ToolId => "circuit_breaker";

    protected override async Task<CircuitBreakerResponse> ExecuteTypedAsync(
        CircuitBreakerRequest request,
        CancellationToken cancellationToken)
    {
        await Task.CompletedTask;

        var metrics = request.PortfolioMetrics;
        var limits = request.RiskLimits;

        var violations = new List<Dictionary<string, object>>();
        foreach (var limit in limits)
        {
            if (metrics.TryGetValue(limit.Key, out var valueObj))
            {
                var value = Convert.ToDecimal(valueObj);
                var limitValue = Convert.ToDecimal(limit.Value);
                if (value > limitValue)
                {
                    violations.Add(new Dictionary<string, object>
                    {
                        ["metric"] = limit.Key,
                        ["current_value"] = value,
                        ["limit"] = limitValue,
                        ["severity"] = value > limitValue * 1.2m ? "CRITICAL" : "WARNING"
                    });
                }
            }
        }

        return new CircuitBreakerResponse
        {
            Timestamp = DateTime.UtcNow,
            BreakerTriggered = violations.Count > 0,
            Violations = violations,
            Action = violations.Count > 0 ? "HALT_TRADING" : "CONTINUE",
            Message = violations.Count > 0 ? $"{violations.Count} risk limit(s) breached" : "All limits within bounds"
        };
    }
}
