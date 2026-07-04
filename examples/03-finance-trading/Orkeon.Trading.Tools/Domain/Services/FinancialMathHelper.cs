using MathNet.Numerics.Statistics;

namespace Orkeon.Trading.Tools.Domain.Services;

/// <summary>
/// Shared financial math utilities used across multiple trading tools.
/// Centralizes correlation, covariance, and portfolio calculations to avoid duplication.
/// </summary>
public static class FinancialMathHelper
{
    /// <summary>
    /// Calculate the correlation matrix for a set of symbols.
    /// Aligns series to the same length when symbols have different history depths.
    /// </summary>
    public static Dictionary<string, Dictionary<string, double>> CalculateCorrelationMatrix(
        List<string> symbols,
        Dictionary<string, List<double>> returnsData)
    {
        var corrMatrix = new Dictionary<string, Dictionary<string, double>>();

        foreach (var symbol1 in symbols)
        {
            corrMatrix[symbol1] = new Dictionary<string, double>();

            foreach (var symbol2 in symbols)
            {
                if (!returnsData.ContainsKey(symbol1) || !returnsData.ContainsKey(symbol2))
                {
                    corrMatrix[symbol1][symbol2] = symbol1 == symbol2 ? 1.0 : 0.0;
                    continue;
                }

                var returns1 = returnsData[symbol1];
                var returns2 = returnsData[symbol2];

                // Align series to the same length (symbols may have different history depths)
                var minLength = Math.Min(returns1.Count, returns2.Count);
                if (minLength < 2)
                {
                    corrMatrix[symbol1][symbol2] = symbol1 == symbol2 ? 1.0 : 0.0;
                    continue;
                }
                var aligned1 = returns1.TakeLast(minLength).ToArray();
                var aligned2 = returns2.TakeLast(minLength).ToArray();

                var correlation = symbol1 == symbol2 ? 1.0 : Correlation.Pearson(aligned1, aligned2);
                corrMatrix[symbol1][symbol2] = correlation;
            }
        }

        return corrMatrix;
    }

    /// <summary>
    /// Calculate the covariance matrix for a set of symbols.
    /// Aligns series to the same length when symbols have different history depths.
    /// </summary>
    public static Dictionary<string, Dictionary<string, double>> CalculateCovarianceMatrix(
        List<string> symbols,
        Dictionary<string, List<double>> returnsData)
    {
        var covMatrix = new Dictionary<string, Dictionary<string, double>>();

        foreach (var symbol1 in symbols)
        {
            covMatrix[symbol1] = new Dictionary<string, double>();

            foreach (var symbol2 in symbols)
            {
                if (!returnsData.ContainsKey(symbol1) || !returnsData.ContainsKey(symbol2))
                {
                    covMatrix[symbol1][symbol2] = 0;
                    continue;
                }

                var returns1 = returnsData[symbol1];
                var returns2 = returnsData[symbol2];

                // Align series to the same length (symbols may have different history depths)
                var minLength = Math.Min(returns1.Count, returns2.Count);
                if (minLength < 2)
                {
                    covMatrix[symbol1][symbol2] = 0;
                    continue;
                }
                var aligned1 = returns1.TakeLast(minLength).ToArray();
                var aligned2 = returns2.TakeLast(minLength).ToArray();

                if (symbol1 == symbol2)
                {
                    covMatrix[symbol1][symbol2] = aligned1.Variance();
                }
                else
                {
                    var covariance = Correlation.Pearson(aligned1, aligned2) *
                                   aligned1.StandardDeviation() * aligned2.StandardDeviation();
                    covMatrix[symbol1][symbol2] = covariance;
                }
            }
        }

        return covMatrix;
    }

    /// <summary>
    /// Calculate portfolio volatility from weights and covariance matrix.
    /// </summary>
    public static double CalculatePortfolioVolatility(
        Dictionary<string, double> weights,
        Dictionary<string, Dictionary<string, double>> covarianceMatrix)
    {
        double variance = 0;

        foreach (var w1 in weights)
        {
            foreach (var w2 in weights)
            {
                var cov = covarianceMatrix.GetValueOrDefault(w1.Key)?.GetValueOrDefault(w2.Key) ?? 0;
                variance += w1.Value * w2.Value * cov;
            }
        }

        return Math.Sqrt(Math.Max(0, variance));
    }
}
