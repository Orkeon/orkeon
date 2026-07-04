using Orkeon.Trading.Tools.Infrastructure.Base;
using Orkeon.Trading.Tools.Infrastructure.Data.Contracts;
using Orkeon.Trading.Tools.Domain.Models;
using Orkeon.Trading.Tools.Domain.Services;
using Microsoft.Extensions.Logging;

namespace Orkeon.Trading.Tools.Infrastructure.Data;

/// <summary>
/// Fundamental data tool for retrieving company financials and valuation metrics.
/// Provides comprehensive fundamental analysis data via an injected <see cref="IFundamentalDataProvider"/>.
/// </summary>
public class FundamentalDataTool(
    IFundamentalDataProvider fundamentalProvider,
    ILogger<FundamentalDataTool>? logger = null)
    : TradingToolBase<FundamentalDataRequest, FundamentalDataResponse>(logger)
{
    private readonly IFundamentalDataProvider _fundamentalProvider = fundamentalProvider;

    protected override string ToolId => "fundamental_data";

    protected override async Task<FundamentalDataResponse> ExecuteTypedAsync(
        FundamentalDataRequest request,
        CancellationToken cancellationToken)
    {
        _logger?.LogInformation("Fetching fundamental data for {Symbol} ({Period}) via {Provider}",
            request.Symbol, request.Period, _fundamentalProvider.ProviderName);

        var fundamentalData = await _fundamentalProvider.GetFundamentalDataAsync(
            request.Symbol, request.Period, cancellationToken);

        var result = BuildFundamentalResult(fundamentalData, request.Metrics);

        _logger?.LogInformation("Fetched fundamental data for {Symbol}: PE={PE:F2}, ROE={ROE:F2}%",
            request.Symbol, fundamentalData.PERatio, fundamentalData.ROE);

        return result;
    }

    private static FundamentalDataResponse BuildFundamentalResult(FundamentalData data, List<string>? requestedMetrics)
    {
        var valuation = new Dictionary<string, object>
        {
            ["market_cap"] = data.MarketCap ?? 0m, ["enterprise_value"] = data.EnterpriseValue ?? 0m,
            ["pe_ratio"] = data.PERatio ?? 0m, ["peg_ratio"] = data.PEGRatio ?? 0m,
            ["price_to_book"] = data.PriceToBook ?? 0m, ["price_to_sales"] = data.PriceToSales ?? 0m,
            ["ev_to_ebitda"] = data.EVToEBITDA ?? 0m
        };

        var profitability = new Dictionary<string, object>
        {
            ["roe"] = data.ROE ?? 0m, ["roa"] = data.ROA ?? 0m, ["roi"] = data.ROI ?? 0m,
            ["net_margin"] = data.NetMargin ?? 0m, ["operating_margin"] = data.OperatingMargin ?? 0m,
            ["gross_margin"] = data.GrossMargin ?? 0m
        };

        var financialHealth = new Dictionary<string, object>
        {
            ["current_ratio"] = data.CurrentRatio ?? 0m, ["quick_ratio"] = data.QuickRatio ?? 0m,
            ["debt_to_equity"] = data.DebtToEquity ?? 0m, ["interest_coverage"] = data.InterestCoverage ?? 0m
        };

        var growth = new Dictionary<string, object>
        {
            ["revenue_growth_yoy"] = data.RevenueGrowthYoY ?? 0m,
            ["earnings_growth_yoy"] = data.EarningsGrowthYoY ?? 0m,
            ["dividend_yield"] = data.DividendYield ?? 0m
        };

        var overallScore = CalculateFundamentalScore(data);

        return new FundamentalDataResponse
        {
            Symbol = data.Symbol, ReportDate = data.ReportDate,
            Valuation = valuation, Profitability = profitability,
            FinancialHealth = financialHealth, Growth = growth,
            OverallScore = overallScore,
            ScoreBreakdown = new Dictionary<string, object>
            {
                ["valuation_score"] = CalculateValuationScore(data),
                ["profitability_score"] = CalculateProfitabilityScore(data),
                ["health_score"] = CalculateHealthScore(data),
                ["growth_score"] = CalculateGrowthScore(data)
            }
        };
    }

    private static decimal CalculateFundamentalScore(FundamentalData data) =>
        CalculateValuationScore(data) * 0.25m + CalculateProfitabilityScore(data) * 0.30m +
        CalculateHealthScore(data) * 0.25m + CalculateGrowthScore(data) * 0.20m;

    private static decimal CalculateValuationScore(FundamentalData data)
    {
        decimal score = 50;
        var pe = data.PERatio ?? 0m;
        if (pe < 15) score += 15; else if (pe < 25) score += 10; else if (pe < 35) score += 5;
        var peg = data.PEGRatio ?? 0m;
        if (peg < 1) score += 15; else if (peg < 1.5m) score += 10; else if (peg < 2) score += 5;
        var pb = data.PriceToBook ?? 0m;
        if (pb < 3) score += 10; else if (pb < 5) score += 5;
        return Math.Min(100, Math.Max(0, score));
    }

    private static decimal CalculateProfitabilityScore(FundamentalData data)
    {
        decimal score = 0;
        var roe = data.ROE ?? 0m;
        if (roe > 20) score += 20; else if (roe > 15) score += 15; else if (roe > 10) score += 10;
        var roa = data.ROA ?? 0m;
        if (roa > 10) score += 15; else if (roa > 5) score += 10; else if (roa > 2) score += 5;
        var nm = data.NetMargin ?? 0m;
        if (nm > 20) score += 20; else if (nm > 10) score += 15; else if (nm > 5) score += 10;
        var om = data.OperatingMargin ?? 0m;
        if (om > 20) score += 15; else if (om > 10) score += 10;
        return Math.Min(100, Math.Max(0, score));
    }

    private static decimal CalculateHealthScore(FundamentalData data)
    {
        decimal score = 0;
        var cr = data.CurrentRatio ?? 0m;
        if (cr > 2) score += 25; else if (cr > 1.5m) score += 20; else if (cr > 1) score += 15;
        var de = data.DebtToEquity ?? 0m;
        if (de < 0.5m) score += 30; else if (de < 1) score += 20; else if (de < 2) score += 10;
        var ic = data.InterestCoverage ?? 0m;
        if (ic > 10) score += 25; else if (ic > 5) score += 20; else if (ic > 2) score += 10;
        return Math.Min(100, Math.Max(0, score));
    }

    private static decimal CalculateGrowthScore(FundamentalData data)
    {
        decimal score = 0;
        var rg = data.RevenueGrowthYoY ?? 0m;
        if (rg > 20) score += 30; else if (rg > 10) score += 20; else if (rg > 5) score += 10;
        var eg = data.EarningsGrowthYoY ?? 0m;
        if (eg > 25) score += 30; else if (eg > 15) score += 20; else if (eg > 5) score += 10;
        return Math.Min(100, Math.Max(0, score));
    }
}
