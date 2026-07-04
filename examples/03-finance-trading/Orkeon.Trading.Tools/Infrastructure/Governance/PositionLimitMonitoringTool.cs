using Orkeon.Trading.Tools.Infrastructure.Base;
using Orkeon.Trading.Tools.Infrastructure.Governance.Contracts;
using Microsoft.Extensions.Logging;

namespace Orkeon.Trading.Tools.Infrastructure.Governance;

public class PositionLimitMonitoringTool(ILogger<PositionLimitMonitoringTool>? logger = null)
    : TradingToolBase<PositionLimitMonitoringRequest, PositionLimitMonitoringResponse>(logger)
{
    protected override string ToolId => "position_limit_monitoring";

    protected override async Task<PositionLimitMonitoringResponse> ExecuteTypedAsync(
        PositionLimitMonitoringRequest request,
        CancellationToken cancellationToken)
    {
        await Task.CompletedTask;

        var positions = request.Positions;
        var limits = request.Limits;

        var warnings = positions.Where(p => limits.ContainsKey(p.Key) && Convert.ToDecimal(p.Value) > Convert.ToDecimal(limits[p.Key]))
            .Select(p => new Dictionary<string, object>
            {
                ["symbol"] = p.Key,
                ["current"] = Convert.ToDecimal(p.Value),
                ["limit"] = Convert.ToDecimal(limits[p.Key]),
                ["excess"] = Convert.ToDecimal(p.Value) - Convert.ToDecimal(limits[p.Key])
            }).ToList();

        return new PositionLimitMonitoringResponse
        {
            Timestamp = DateTime.UtcNow,
            PositionsMonitored = positions.Count,
            LimitBreaches = warnings.Count,
            Warnings = warnings,
            Status = warnings.Count > 0 ? "LIMITS_EXCEEDED" : "ALL_WITHIN_LIMITS"
        };
    }
}
