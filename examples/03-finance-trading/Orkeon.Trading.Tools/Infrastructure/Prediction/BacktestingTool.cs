using Orkeon.Trading.Tools.Infrastructure.Base;
using Orkeon.Trading.Tools.Infrastructure.Prediction.Contracts;
using Orkeon.Trading.Tools.Domain.Models;
using Microsoft.Extensions.Logging;
using MathNet.Numerics.Statistics;

namespace Orkeon.Trading.Tools.Infrastructure.Prediction;

/// <summary>
/// Vectorized backtesting tool for strategy performance evaluation.
/// Tests trading strategies on historical data with realistic transaction costs and slippage.
/// Provides comprehensive performance metrics including Sharpe ratio, max drawdown, and win rate.
/// </summary>
public class BacktestingTool(ILogger<BacktestingTool>? logger = null)
    : TradingToolBase<BacktestingRequest, BacktestingResponse>(logger)
{
    protected override string ToolId => "backtesting";

    protected override string? ValidateTypedRequest(BacktestingRequest request)
    {
        if (request.PriceData.Count < 10)
            return "At least 10 data points required for backtesting";
        if (request.StrategySignals.Count != request.PriceData.Count)
            return "Strategy signals must match price data length";
        return null;
    }

    protected override async Task<BacktestingResponse> ExecuteTypedAsync(
        BacktestingRequest request,
        CancellationToken cancellationToken)
    {
        _logger?.LogInformation("Running backtest for {Symbol} with initial capital ${Capital}", request.Symbol, request.InitialCapital);

        var backtest = await Task.Run(() => RunBacktest(
            request.Symbol,
            request.PriceData,
            request.StrategySignals,
            request.InitialCapital,
            request.PositionSize,
            request.CommissionPct,
            request.SlippagePct,
            request.AllowShort), cancellationToken);

        _logger?.LogInformation("Backtest completed for {Symbol} - Total Return: {Return}%",
            request.Symbol, backtest.PerformanceMetrics["total_return_pct"]);

        return backtest;
    }

    private static BacktestingResponse RunBacktest(
        string symbol,
        List<MarketData> priceData,
        List<int> signals,
        decimal initialCapital,
        double positionSize,
        double commissionPct,
        double slippagePct,
        bool allowShort)
    {
        var n = priceData.Count;

        // Initialize tracking variables
        var positions = new List<decimal>(); // Number of shares held
        var cash = new List<decimal> { initialCapital }; // Cash balance
        var portfolioValue = new List<decimal> { initialCapital }; // Total portfolio value
        var trades = new List<Trade>();

        decimal currentPosition = 0;
        decimal currentCash = initialCapital;

        // Run vectorized backtest
        for (int i = 0; i < n; i++)
        {
            var price = priceData[i].Close;
            var signal = signals[i];

            // Determine target position
            decimal targetPosition = 0;
            if (signal == 1) // Long signal
            {
                targetPosition = (currentCash * (decimal)positionSize) / price;
            }
            else if (signal == -1 && allowShort) // Short signal
            {
                targetPosition = -(currentCash * (decimal)positionSize) / price;
            }

            // Execute trade if position changes
            if (targetPosition != currentPosition)
            {
                var sharesTraded = targetPosition - currentPosition;
                var tradeValue = Math.Abs(sharesTraded) * price;

                // Apply transaction costs
                var commission = tradeValue * (decimal)commissionPct;
                var slippage = tradeValue * (decimal)slippagePct;
                var totalCost = commission + slippage;

                // Update cash and position
                if (sharesTraded > 0) // Buying
                {
                    currentCash -= (sharesTraded * price + totalCost);
                }
                else // Selling
                {
                    currentCash += (Math.Abs(sharesTraded) * price - totalCost);
                }

                // Record trade
                trades.Add(new Trade
                {
                    Timestamp = priceData[i].Timestamp,
                    Type = sharesTraded > 0 ? "BUY" : "SELL",
                    Shares = Math.Abs(sharesTraded),
                    Price = price,
                    Commission = commission,
                    Slippage = slippage
                });

                currentPosition = targetPosition;
            }

            // Calculate current portfolio value
            var positionValue = currentPosition * price;
            var totalValue = currentCash + positionValue;

            positions.Add(currentPosition);
            cash.Add(currentCash);
            portfolioValue.Add(totalValue);
        }

        // Calculate performance metrics
        var performanceMetrics = CalculatePerformanceMetrics(
            portfolioValue,
            initialCapital,
            priceData
        );

        // Calculate trade statistics
        var tradeStatistics = CalculateTradeStatistics(trades, portfolioValue, initialCapital);

        // Build equity curve (skip initial pre-trading entry to align with priceData/positions)
        var equityCurve = portfolioValue.Skip(1).Select((pv, i) => new Dictionary<string, object>
        {
            ["timestamp"] = priceData[i].Timestamp.ToString("yyyy-MM-dd"),
            ["portfolio_value"] = Math.Round(pv, 2),
            ["cash"] = Math.Round(cash[i + 1], 2),
            ["position_value"] = Math.Round(positions[i] * priceData[i].Close, 2),
            ["return_pct"] = Math.Round((pv - initialCapital) / initialCapital * 100, 2)
        }).ToList();

        return new BacktestingResponse
        {
            Symbol = symbol,
            Timestamp = DateTime.UtcNow,
            BacktestConfig = new Dictionary<string, object>
            {
                ["initial_capital"] = initialCapital,
                ["position_size"] = positionSize,
                ["commission_pct"] = commissionPct * 100,
                ["slippage_pct"] = slippagePct * 100,
                ["allow_short"] = allowShort,
                ["period_start"] = priceData.First().Timestamp.ToString("yyyy-MM-dd"),
                ["period_end"] = priceData.Last().Timestamp.ToString("yyyy-MM-dd"),
                ["trading_days"] = n
            },
            PerformanceMetrics = performanceMetrics,
            TradeStatistics = tradeStatistics,
            EquityCurve = equityCurve,
            FinalPortfolioValue = Math.Round(portfolioValue.Last(), 2),
            StrategyQuality = EvaluateStrategyQuality(performanceMetrics)
        };
    }

    private static Dictionary<string, object> CalculatePerformanceMetrics(
        List<decimal> portfolioValue,
        decimal initialCapital,
        List<MarketData> priceData)
    {
        // Total return
        var finalValue = portfolioValue.Last();
        var totalReturn = (finalValue - initialCapital) / initialCapital;
        var totalReturnPct = totalReturn * 100;

        // Calculate daily returns
        var dailyReturns = new List<double>();
        for (int i = 1; i < portfolioValue.Count; i++)
        {
            var ret = (double)((portfolioValue[i] - portfolioValue[i - 1]) / portfolioValue[i - 1]);
            dailyReturns.Add(ret);
        }

        // Annualized return (assuming 252 trading days)
        var tradingDays = portfolioValue.Count;
        var years = tradingDays / 252.0;
        var annualizedReturn = years > 0 ? Math.Pow((double)(finalValue / initialCapital), 1.0 / years) - 1 : 0;

        // Volatility (annualized)
        var dailyStd = dailyReturns.StandardDeviation();
        var annualizedVolatility = dailyStd * Math.Sqrt(252);

        // Sharpe Ratio (assuming 2% risk-free rate)
        var riskFreeRate = 0.02;
        var sharpeRatio = annualizedVolatility > 0 ? (annualizedReturn - riskFreeRate) / annualizedVolatility : 0;

        // Sortino Ratio (downside deviation)
        var downsideReturns = dailyReturns.Where(r => r < 0).ToList();
        var downsideDeviation = downsideReturns.Count > 0
            ? Math.Sqrt(downsideReturns.Select(r => r * r).Average()) * Math.Sqrt(252)
            : annualizedVolatility;
        var sortinoRatio = downsideDeviation > 0 ? (annualizedReturn - riskFreeRate) / downsideDeviation : 0;

        // Maximum Drawdown
        var maxDrawdown = CalculateMaxDrawdown(portfolioValue);

        // Calmar Ratio
        var calmarRatio = maxDrawdown > 0 ? annualizedReturn / (double)maxDrawdown : 0;

        // Buy & Hold comparison
        var buyHoldReturn = (double)((priceData.Last().Close - priceData.First().Close) / priceData.First().Close);

        return new Dictionary<string, object>
        {
            ["total_return_pct"] = Math.Round((decimal)(totalReturnPct), 2),
            ["annualized_return_pct"] = Math.Round((decimal)(annualizedReturn * 100), 2),
            ["annualized_volatility_pct"] = Math.Round((decimal)(annualizedVolatility * 100), 2),
            ["sharpe_ratio"] = Math.Round((decimal)sharpeRatio, 2),
            ["sortino_ratio"] = Math.Round((decimal)sortinoRatio, 2),
            ["max_drawdown_pct"] = Math.Round(maxDrawdown * 100, 2),
            ["calmar_ratio"] = Math.Round((decimal)calmarRatio, 2),
            ["buy_hold_return_pct"] = Math.Round((decimal)(buyHoldReturn * 100), 2),
            ["excess_return_pct"] = Math.Round((decimal)((decimal)(totalReturnPct) - (decimal)(buyHoldReturn * 100)), 2)
        };
    }

    private static decimal CalculateMaxDrawdown(List<decimal> portfolioValue)
    {
        var peak = portfolioValue[0];
        var maxDrawdown = 0m;

        foreach (var value in portfolioValue)
        {
            if (value > peak)
                peak = value;

            var drawdown = (peak - value) / peak;
            if (drawdown > maxDrawdown)
                maxDrawdown = drawdown;
        }

        return maxDrawdown;
    }

    private static Dictionary<string, object> CalculateTradeStatistics(
        List<Trade> trades,
        List<decimal> portfolioValue,
        decimal initialCapital)
    {
        if (trades.Count == 0)
        {
            return new Dictionary<string, object>
            {
                ["total_trades"] = 0,
                ["total_commission"] = 0,
                ["total_slippage"] = 0,
                ["note"] = "No trades executed"
            };
        }

        // Group trades into round trips (buy/sell pairs)
        var roundTrips = new List<(Trade Entry, Trade Exit, decimal PnL)>();

        for (int i = 0; i < trades.Count - 1; i += 2)
        {
            if (i + 1 < trades.Count)
            {
                var entry = trades[i];
                var exit = trades[i + 1];

                var pnl = 0m;
                if (entry.Type == "BUY" && exit.Type == "SELL")
                {
                    pnl = (exit.Price - entry.Price) * entry.Shares - entry.Commission - entry.Slippage - exit.Commission - exit.Slippage;
                }

                roundTrips.Add((entry, exit, pnl));
            }
        }

        var winningTrades = roundTrips.Count(rt => rt.PnL > 0);
        var losingTrades = roundTrips.Count(rt => rt.PnL < 0);
        var winRate = roundTrips.Count > 0 ? (decimal)winningTrades / roundTrips.Count * 100 : 0;

        var avgWin = roundTrips.Where(rt => rt.PnL > 0).Any()
            ? roundTrips.Where(rt => rt.PnL > 0).Average(rt => rt.PnL)
            : 0;

        var avgLoss = roundTrips.Where(rt => rt.PnL < 0).Any()
            ? roundTrips.Where(rt => rt.PnL < 0).Average(rt => rt.PnL)
            : 0;

        var profitFactor = avgLoss != 0 ? Math.Abs(avgWin / avgLoss) : 0;

        return new Dictionary<string, object>
        {
            ["total_trades"] = trades.Count,
            ["round_trips"] = roundTrips.Count,
            ["winning_trades"] = winningTrades,
            ["losing_trades"] = losingTrades,
            ["win_rate_pct"] = Math.Round(winRate, 2),
            ["average_win"] = Math.Round(avgWin, 2),
            ["average_loss"] = Math.Round(avgLoss, 2),
            ["profit_factor"] = Math.Round(profitFactor, 2),
            ["total_commission"] = Math.Round(trades.Sum(t => t.Commission), 2),
            ["total_slippage"] = Math.Round(trades.Sum(t => t.Slippage), 2),
            ["total_transaction_costs"] = Math.Round(trades.Sum(t => t.Commission + t.Slippage), 2),
            ["transaction_cost_pct_of_capital"] = Math.Round(trades.Sum(t => t.Commission + t.Slippage) / initialCapital * 100, 2)
        };
    }

    private static string EvaluateStrategyQuality(Dictionary<string, object> metrics)
    {
        var sharpe = (decimal)metrics["sharpe_ratio"];
        var maxDD = Math.Abs((decimal)metrics["max_drawdown_pct"]);
        var totalReturn = (decimal)metrics["total_return_pct"];

        // Scoring criteria
        var score = 0;

        // Sharpe ratio scoring
        if (sharpe > 2.0m) score += 3;
        else if (sharpe > 1.0m) score += 2;
        else if (sharpe > 0.5m) score += 1;

        // Max drawdown scoring
        if (maxDD < 10) score += 3;
        else if (maxDD < 20) score += 2;
        else if (maxDD < 30) score += 1;

        // Total return scoring
        if (totalReturn > 50) score += 3;
        else if (totalReturn > 20) score += 2;
        else if (totalReturn > 0) score += 1;

        return score switch
        {
            >= 8 => "EXCELLENT",
            >= 6 => "GOOD",
            >= 4 => "MODERATE",
            >= 2 => "POOR",
            _ => "VERY_POOR"
        };
    }

    private record Trade
    {
        public required DateTime Timestamp { get; init; }
        public required string Type { get; init; }
        public required decimal Shares { get; init; }
        public required decimal Price { get; init; }
        public required decimal Commission { get; init; }
        public required decimal Slippage { get; init; }
    }
}
