using Orkeon.Trading.Tools.Infrastructure.Base;
using Orkeon.Trading.Tools.Infrastructure.Governance.Contracts;
using Microsoft.Extensions.Logging;

namespace Orkeon.Trading.Tools.Infrastructure.Governance;

public class PerformanceAttributionTool(ILogger<PerformanceAttributionTool>? logger = null)
    : TradingToolBase<PerformanceAttributionRequest, PerformanceAttributionResponse>(logger)
{
    protected override string ToolId => "performance_attribution";

    protected override async Task<PerformanceAttributionResponse> ExecuteTypedAsync(
        PerformanceAttributionRequest request,
        CancellationToken cancellationToken)
    {
        await Task.CompletedTask;

        var totalReturn = request.TotalReturn;

        var attributions = request.Positions.Select(p =>
        {
            var weight = Convert.ToDecimal(p.GetValueOrDefault("weight", 0));
            var returnPct = Convert.ToDecimal(p.GetValueOrDefault("return", 0));
            var contribution = weight * returnPct;

            return new Dictionary<string, object>
            {
                ["symbol"] = p.GetValueOrDefault("symbol", ""),
                ["weight_pct"] = Math.Round(weight * 100, 2),
                ["return_pct"] = Math.Round(returnPct, 2),
                ["contribution_to_return"] = Math.Round(contribution, 4),
                ["contribution_pct_of_total"] = Math.Round((contribution / totalReturn) * 100, 2)
            };
        }).OrderByDescending(a => Math.Abs(Convert.ToDecimal(a["contribution_to_return"]))).ToList();

        var topContributors = attributions.Take(5).ToList();
        var bottomDetractors = attributions.Where(a => Convert.ToDecimal(a["contribution_to_return"]) < 0).Take(5).ToList();

        return new PerformanceAttributionResponse
        {
            Timestamp = DateTime.UtcNow,
            TotalReturnPct = totalReturn,
            Attributions = attributions,
            Top5Contributors = topContributors,
            Top5Detractors = bottomDetractors,
            PositionsAnalyzed = request.Positions.Count
        };
    }
}
