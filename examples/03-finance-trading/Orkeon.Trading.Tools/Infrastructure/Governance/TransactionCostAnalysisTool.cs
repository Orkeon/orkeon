using Orkeon.Trading.Tools.Infrastructure.Base;
using Orkeon.Trading.Tools.Infrastructure.Governance.Contracts;
using Microsoft.Extensions.Logging;

namespace Orkeon.Trading.Tools.Infrastructure.Governance;

public class TransactionCostAnalysisTool(ILogger<TransactionCostAnalysisTool>? logger = null)
    : TradingToolBase<TransactionCostAnalysisRequest, TransactionCostAnalysisResponse>(logger)
{
    protected override string ToolId => "transaction_cost_analysis";

    protected override async Task<TransactionCostAnalysisResponse> ExecuteTypedAsync(
        TransactionCostAnalysisRequest request,
        CancellationToken cancellationToken)
    {
        await Task.CompletedTask;

        var costs = request.Trades.Select(t =>
        {
            var quantity = Convert.ToDecimal(t.GetValueOrDefault("quantity", 0));
            var price = Convert.ToDecimal(t.GetValueOrDefault("execution_price", 0));
            var benchmarkPrice = Convert.ToDecimal(t.GetValueOrDefault("benchmark_price", price));
            var commission = Convert.ToDecimal(t.GetValueOrDefault("commission", 0));

            var slippage = Math.Abs(price - benchmarkPrice) * quantity;
            var totalCost = commission + slippage;
            var costBps = benchmarkPrice > 0 ? (totalCost / (quantity * benchmarkPrice)) * 10000 : 0;

            return new Dictionary<string, object>
            {
                ["symbol"] = t.GetValueOrDefault("symbol", ""),
                ["commission"] = Math.Round(commission, 2),
                ["slippage"] = Math.Round(slippage, 2),
                ["total_cost"] = Math.Round(totalCost, 2),
                ["cost_bps"] = Math.Round(costBps, 2)
            };
        }).ToList();

        return new TransactionCostAnalysisResponse
        {
            Timestamp = DateTime.UtcNow,
            TradesAnalyzed = request.Trades.Count,
            TotalCommission = Math.Round(costs.Sum(c => Convert.ToDecimal(c["commission"])), 2),
            TotalSlippage = Math.Round(costs.Sum(c => Convert.ToDecimal(c["slippage"])), 2),
            TotalCost = Math.Round(costs.Sum(c => Convert.ToDecimal(c["total_cost"])), 2),
            AverageCostBps = Math.Round(costs.Average(c => Convert.ToDecimal(c["cost_bps"])), 2),
            TradeCosts = costs
        };
    }
}
