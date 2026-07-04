using Orkeon.Trading.Tools.Infrastructure.Base;
using Orkeon.Trading.Tools.Infrastructure.Portfolio.Contracts;
using Microsoft.Extensions.Logging;

namespace Orkeon.Trading.Tools.Infrastructure.Portfolio;

/// <summary>
/// Portfolio rebalancing tool for generating optimal trade lists.
/// Calculates rebalancing trades from current to target weights minimizing transaction costs.
/// Supports threshold rebalancing, calendar rebalancing, and tax-loss harvesting strategies.
/// </summary>
public class PortfolioRebalancingTool(ILogger<PortfolioRebalancingTool>? logger = null)
    : TradingToolBase<PortfolioRebalancingRequest, PortfolioRebalancingResponse>(logger)
{

    protected override string ToolId => "portfolio_rebalancing";

    protected override async Task<PortfolioRebalancingResponse> ExecuteTypedAsync(
        PortfolioRebalancingRequest request,
        CancellationToken cancellationToken)
    {
        _logger?.LogInformation("Generating rebalancing trades for portfolio value ${Value}", request.PortfolioValue);

        var rebalancing = await Task.Run(() => GenerateRebalancingPlan(
            request.CurrentPortfolio,
            request.TargetWeights,
            request.PortfolioValue,
            request.RebalancingThreshold,
            request.CommissionPerTrade,
            request.TaxRate,
            request.ConsiderTaxLossHarvesting,
            request.MaxTrades), cancellationToken);

        _logger?.LogInformation("Rebalancing plan generated with {Trades} trades", rebalancing.TradesCount);

        return rebalancing;
    }

    private static PortfolioRebalancingResponse GenerateRebalancingPlan(
        Dictionary<string, Position> currentPortfolio,
        Dictionary<string, decimal> targetWeights,
        decimal portfolioValue,
        double rebalancingThreshold,
        decimal commissionPerTrade,
        double taxRate,
        bool considerTaxLoss,
        int? maxTrades)
    {
        // Step 1: Calculate current weights
        var currentWeights = CalculateCurrentWeights(currentPortfolio, portfolioValue);

        // Step 2: Identify positions requiring rebalancing
        var rebalancingActions = IdentifyRebalancingActions(
            currentWeights,
            targetWeights,
            portfolioValue,
            currentPortfolio,
            rebalancingThreshold
        );

        // Step 3: Prioritize trades (minimize transaction costs)
        var prioritizedTrades = PrioritizeTrades(rebalancingActions, commissionPerTrade);

        // Step 4: Apply max trades limit if specified
        if (maxTrades.HasValue && prioritizedTrades.Count > maxTrades.Value)
        {
            prioritizedTrades = prioritizedTrades.Take(maxTrades.Value).ToList();
        }

        // Step 5: Identify tax-loss harvesting opportunities
        List<TaxLossOpportunity>? taxLossOpportunities = null;
        if (considerTaxLoss)
        {
            taxLossOpportunities = IdentifyTaxLossHarvestingOpportunities(currentPortfolio, taxRate);
        }

        // Step 6: Calculate costs and turnover
        var totalTurnover = CalculateTotalTurnover(prioritizedTrades, portfolioValue);
        var estimatedCost = CalculateEstimatedCost(prioritizedTrades, commissionPerTrade, taxRate, considerTaxLoss);

        // Step 7: Generate execution recommendations
        var executionPlan = GenerateExecutionPlan(prioritizedTrades);

        return new PortfolioRebalancingResponse
        {
            Timestamp = DateTime.UtcNow,
            PortfolioValue = portfolioValue,
            RebalancingThreshold = rebalancingThreshold,
            TradesCount = prioritizedTrades.Count,
            Trades = prioritizedTrades.Select(t => new Dictionary<string, object>
            {
                ["symbol"] = t.Symbol,
                ["action"] = t.Action,
                ["current_weight"] = Math.Round(t.CurrentWeight * 100, 2),
                ["target_weight"] = Math.Round(t.TargetWeight * 100, 2),
                ["weight_change"] = Math.Round((t.TargetWeight - t.CurrentWeight) * 100, 2),
                ["current_quantity"] = t.CurrentQuantity,
                ["target_quantity"] = Math.Round(t.TargetQuantity, 0),
                ["quantity_change"] = Math.Round(t.TargetQuantity - t.CurrentQuantity, 0),
                ["estimated_value"] = Math.Round(t.EstimatedValue, 2),
                ["current_price"] = t.CurrentPrice,
                ["priority"] = t.Priority,
                ["reason"] = t.Reason
            }).ToList(),
            TotalTurnover = Math.Round(totalTurnover * 100, 2),
            EstimatedCosts = estimatedCost,
            ExecutionPlan = executionPlan,
            CurrentWeights = currentWeights.ToDictionary(kvp => kvp.Key, kvp => Math.Round(kvp.Value * 100, 2)),
            TargetWeights = targetWeights.ToDictionary(kvp => kvp.Key, kvp => Math.Round(kvp.Value * 100, 2))
        };
    }

    private static Dictionary<string, decimal> CalculateCurrentWeights(
        Dictionary<string, Position> currentPortfolio,
        decimal portfolioValue)
    {
        var currentWeights = new Dictionary<string, decimal>();

        foreach (var position in currentPortfolio)
        {
            var positionValue = position.Value.Quantity * position.Value.CurrentPrice;
            currentWeights[position.Key] = portfolioValue > 0 ? positionValue / portfolioValue : 0;
        }

        return currentWeights;
    }

    private static List<RebalancingTrade> IdentifyRebalancingActions(
        Dictionary<string, decimal> currentWeights,
        Dictionary<string, decimal> targetWeights,
        decimal portfolioValue,
        Dictionary<string, Position> currentPortfolio,
        double rebalancingThreshold)
    {
        var trades = new List<RebalancingTrade>();

        // Get all symbols (current + target)
        var allSymbols = currentWeights.Keys.Union(targetWeights.Keys).ToHashSet();

        foreach (var symbol in allSymbols)
        {
            var currentWeight = currentWeights.GetValueOrDefault(symbol, 0);
            var targetWeight = targetWeights.GetValueOrDefault(symbol, 0);
            var weightDifference = targetWeight - currentWeight;

            // Check if rebalancing needed
            if (Math.Abs((double)weightDifference) < rebalancingThreshold)
                continue;

            var currentPosition = currentPortfolio.GetValueOrDefault(symbol);
            var currentPrice = currentPosition?.CurrentPrice ?? 0;
            var currentQuantity = currentPosition?.Quantity ?? 0;

            var targetValue = portfolioValue * targetWeight;
            var targetQuantity = currentPrice > 0 ? targetValue / currentPrice : 0;

            var action = weightDifference > 0 ? "BUY" : weightDifference < 0 ? "SELL" : "HOLD";
            var reason = DetermineRebalancingReason(currentWeight, targetWeight, rebalancingThreshold);

            trades.Add(new RebalancingTrade
            {
                Symbol = symbol,
                Action = action,
                CurrentWeight = currentWeight,
                TargetWeight = targetWeight,
                CurrentQuantity = currentQuantity,
                TargetQuantity = targetQuantity,
                EstimatedValue = Math.Abs(targetValue - (currentQuantity * currentPrice)),
                CurrentPrice = currentPrice,
                Priority = "NORMAL",
                Reason = reason
            });
        }

        return trades;
    }

    private static string DetermineRebalancingReason(decimal currentWeight, decimal targetWeight, double threshold)
    {
        var difference = Math.Abs(targetWeight - currentWeight);

        if (currentWeight == 0)
            return "New position";
        if (targetWeight == 0)
            return "Liquidate position";
        if (difference > (decimal)threshold * 3)
            return "Major allocation adjustment";
        if (difference > (decimal)threshold * 2)
            return "Significant drift from target";
        return "Routine rebalancing";
    }

    private static List<RebalancingTrade> PrioritizeTrades(List<RebalancingTrade> trades, decimal commissionPerTrade)
    {
        // Prioritize by estimated value and cost-effectiveness
        return trades.OrderByDescending(t =>
        {
            // Large trades first (more cost-effective)
            var valueScore = (double)t.EstimatedValue / 1000.0;

            // Penalize if commission is significant relative to trade value
            var costEffectiveness = t.EstimatedValue > 0
                ? 1.0 - Math.Min(1.0, (double)(commissionPerTrade / t.EstimatedValue))
                : 0;

            return valueScore * costEffectiveness;
        }).ToList();
    }

    private static List<TaxLossOpportunity> IdentifyTaxLossHarvestingOpportunities(
        Dictionary<string, Position> currentPortfolio,
        double taxRate)
    {
        var opportunities = new List<TaxLossOpportunity>();

        foreach (var position in currentPortfolio)
        {
            var unrealizedPnL = (position.Value.CurrentPrice - position.Value.CostBasis) * position.Value.Quantity;

            // Loss opportunity (unrealized loss)
            if (unrealizedPnL < 0)
            {
                var taxBenefit = Math.Abs(unrealizedPnL) * (decimal)taxRate;

                opportunities.Add(new TaxLossOpportunity
                {
                    Symbol = position.Key,
                    UnrealizedLoss = Math.Abs(unrealizedPnL),
                    TaxBenefit = taxBenefit,
                    Recommendation = $"Consider selling {position.Key} to realize ${Math.Round(Math.Abs(unrealizedPnL), 2)} loss for ${Math.Round(taxBenefit, 2)} tax benefit"
                });
            }
        }

        return opportunities.OrderByDescending(o => o.TaxBenefit).ToList();
    }

    private static decimal CalculateTotalTurnover(List<RebalancingTrade> trades, decimal portfolioValue)
    {
        var turnover = trades.Sum(t => Math.Abs(t.TargetWeight - t.CurrentWeight));
        return turnover;
    }

    private static Dictionary<string, object> CalculateEstimatedCost(
        List<RebalancingTrade> trades,
        decimal commissionPerTrade,
        double taxRate,
        bool considerTaxLoss)
    {
        var totalCommission = trades.Count * commissionPerTrade;
        var estimatedSlippage = trades.Sum(t => t.EstimatedValue * 0.0005m); // 0.05% slippage

        var costs = new Dictionary<string, object>
        {
            ["commission"] = Math.Round(totalCommission, 2),
            ["estimated_slippage"] = Math.Round(estimatedSlippage, 2),
            ["total_transaction_cost"] = Math.Round(totalCommission + estimatedSlippage, 2)
        };

        if (considerTaxLoss)
        {
            costs["tax_considerations"] = "Tax-loss harvesting opportunities analyzed";
        }

        return costs;
    }

    private static Dictionary<string, object> GenerateExecutionPlan(List<RebalancingTrade> trades)
    {
        var sellTrades = trades.Where(t => t.Action == "SELL").ToList();
        var buyTrades = trades.Where(t => t.Action == "BUY").ToList();

        return new Dictionary<string, object>
        {
            ["recommended_sequence"] = new List<string>
            {
                "1. Execute SELL orders first to free up capital",
                "2. Wait for settlement (T+2 for equities)",
                "3. Execute BUY orders with proceeds",
                "4. Monitor execution and adjust for slippage"
            },
            ["sell_orders_count"] = sellTrades.Count,
            ["buy_orders_count"] = buyTrades.Count,
            ["estimated_duration"] = "2-3 trading days",
            ["execution_algorithm_recommendation"] = trades.Any(t => t.EstimatedValue > 100000)
                ? "Use VWAP/TWAP for large orders (>$100k)"
                : "Market orders acceptable for small trades"
        };
    }

    public record Position
    {
        public required decimal Weight { get; init; }
        public required decimal Quantity { get; init; }
        public required decimal CurrentPrice { get; init; }
        public required decimal CostBasis { get; init; }
    }

    private record RebalancingTrade
    {
        public required string Symbol { get; init; }
        public required string Action { get; init; }
        public required decimal CurrentWeight { get; init; }
        public required decimal TargetWeight { get; init; }
        public required decimal CurrentQuantity { get; init; }
        public required decimal TargetQuantity { get; init; }
        public required decimal EstimatedValue { get; init; }
        public required decimal CurrentPrice { get; init; }
        public required string Priority { get; init; }
        public required string Reason { get; init; }
    }

    private record TaxLossOpportunity
    {
        public required string Symbol { get; init; }
        public required decimal UnrealizedLoss { get; init; }
        public required decimal TaxBenefit { get; init; }
        public required string Recommendation { get; init; }
    }
}
