using Orkeon.Trading.Tools.Infrastructure.Base;
using Orkeon.Trading.Tools.Infrastructure.Governance.Contracts;
using Microsoft.Extensions.Logging;

namespace Orkeon.Trading.Tools.Infrastructure.Governance;

public class LiquidityAnalysisTool(ILogger<LiquidityAnalysisTool>? logger = null)
    : TradingToolBase<LiquidityAnalysisRequest, LiquidityAnalysisResponse>(logger)
{
    protected override string ToolId => "liquidity_analysis";

    protected override async Task<LiquidityAnalysisResponse> ExecuteTypedAsync(
        LiquidityAnalysisRequest request,
        CancellationToken cancellationToken)
    {
        await Task.CompletedTask;

        var liquidityScores = request.Positions.Select(p =>
        {
            var quantity = Convert.ToDecimal(p.GetValueOrDefault("quantity", 0));
            var avgDailyVolume = Convert.ToDecimal(p.GetValueOrDefault("avg_daily_volume", 1000000));
            var daysToLiquidate = avgDailyVolume > 0 ? quantity / (avgDailyVolume * 0.1m) : 999;
            var score = daysToLiquidate < 1 ? 100 : daysToLiquidate < 5 ? 80 : daysToLiquidate < 10 ? 60 : 40;

            return new Dictionary<string, object>
            {
                ["symbol"] = p.GetValueOrDefault("symbol", ""),
                ["days_to_liquidate"] = Math.Round(daysToLiquidate, 1),
                ["liquidity_score"] = score,
                ["avg_daily_volume"] = avgDailyVolume
            };
        }).ToList();

        var avgScore = liquidityScores.Average(s => Convert.ToDecimal(s["liquidity_score"]));
        var maxDays = liquidityScores.Max(s => Convert.ToDecimal(s["days_to_liquidate"]));

        return new LiquidityAnalysisResponse
        {
            Timestamp = DateTime.UtcNow,
            PortfolioLiquidityScore = Math.Round(avgScore, 1),
            MaxDaysToLiquidate = maxDays,
            PositionLiquidity = liquidityScores,
            LiquidityRating = avgScore > 80 ? "HIGH" : avgScore > 60 ? "MODERATE" : "LOW",
            IlliquidPositions = liquidityScores.Count(s => Convert.ToDecimal(s["liquidity_score"]) < 60)
        };
    }
}
