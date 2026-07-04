using Orkeon.Trading.Tools.Infrastructure.Base;
using Orkeon.Trading.Tools.Infrastructure.Governance.Contracts;
using Microsoft.Extensions.Logging;

namespace Orkeon.Trading.Tools.Infrastructure.Governance;

public class DrawdownMonitoringTool(ILogger<DrawdownMonitoringTool>? logger = null)
    : TradingToolBase<DrawdownMonitoringRequest, DrawdownMonitoringResponse>(logger)
{
    protected override string ToolId => "drawdown_monitoring";

    protected override async Task<DrawdownMonitoringResponse> ExecuteTypedAsync(
        DrawdownMonitoringRequest request,
        CancellationToken cancellationToken)
    {
        await Task.CompletedTask;

        var values = request.PortfolioValues;
        var maxDDLimit = request.MaxDrawdownLimit;

        var peak = values[0];
        var maxDrawdown = 0m;
        var currentDrawdown = 0m;

        foreach (var value in values)
        {
            if (value > peak) peak = value;
            var dd = (peak - value) / peak;
            if (dd > maxDrawdown) maxDrawdown = dd;
            currentDrawdown = dd;
        }

        var breached = maxDrawdown > (decimal)maxDDLimit;

        return new DrawdownMonitoringResponse
        {
            Timestamp = DateTime.UtcNow,
            CurrentDrawdownPct = Math.Round(currentDrawdown * 100, 2),
            MaxDrawdownPct = Math.Round(maxDrawdown * 100, 2),
            LimitPct = maxDDLimit * 100,
            LimitBreached = breached,
            AlertLevel = breached ? "CRITICAL" : maxDrawdown > (decimal)maxDDLimit * 0.8m ? "WARNING" : "NORMAL",
            DaysAnalyzed = values.Count
        };
    }
}
