using Orkeon.Trading.Tools.Infrastructure.Base;
using Orkeon.Trading.Tools.Infrastructure.Governance.Contracts;
using Microsoft.Extensions.Logging;

namespace Orkeon.Trading.Tools.Infrastructure.Governance;

public class ConcentrationRiskTool(ILogger<ConcentrationRiskTool>? logger = null)
    : TradingToolBase<ConcentrationRiskRequest, ConcentrationRiskResponse>(logger)
{
    protected override string ToolId => "concentration_risk";

    protected override async Task<ConcentrationRiskResponse> ExecuteTypedAsync(
        ConcentrationRiskRequest request,
        CancellationToken cancellationToken)
    {
        await Task.CompletedTask;

        var positionsList = request.Positions;
        var weights = positionsList.Select(p => Convert.ToDecimal(p["weight"])).ToList();
        var herfindahl = weights.Sum(w => w * w);
        var positionsCount = positionsList.Count;
        var effectiveN = herfindahl > 0 ? 1m / herfindahl : positionsCount;

        var top5 = weights.OrderByDescending(w => w).Take(5).Sum();

        return new ConcentrationRiskResponse
        {
            Timestamp = DateTime.UtcNow,
            TotalPositions = positionsCount,
            HerfindahlIndex = Math.Round(herfindahl, 4),
            EffectivePositions = Math.Round(effectiveN, 2),
            Top5ConcentrationPct = Math.Round(top5 * 100, 2),
            MaxSinglePositionPct = Math.Round(weights.Max() * 100, 2),
            ConcentrationRating = top5 > 0.7m ? "HIGH" : top5 > 0.5m ? "MODERATE" : "LOW",
            DiversificationScore = Math.Round((effectiveN / positionsCount) * 100, 1)
        };
    }
}
