using Orkeon.Trading.Tools.Infrastructure.Base;
using Orkeon.Trading.Tools.Infrastructure.Portfolio.Contracts;
using Orkeon.Trading.Tools.Domain.Services;
using Microsoft.Extensions.Logging;
using MathNet.Numerics.Statistics;

namespace Orkeon.Trading.Tools.Infrastructure.Portfolio;

/// <summary>
/// Hierarchical Risk Parity (HRP) portfolio optimization tool using machine learning clustering.
/// Uses hierarchical clustering to group similar assets, then applies risk parity within and across clusters.
/// More stable than traditional Mean-Variance optimization, handles non-normal returns well.
/// </summary>
public class HierarchicalRiskParityTool(ILogger<HierarchicalRiskParityTool>? logger = null)
    : TradingToolBase<HierarchicalRiskParityRequest, HierarchicalRiskParityResponse>(logger)
{

    protected override string ToolId => "hierarchical_risk_parity";

    protected override string? ValidateTypedRequest(HierarchicalRiskParityRequest request)
    {
        if (request.Symbols.Count < 2)
            return "Hierarchical Risk Parity requires at least 2 assets for clustering. Cannot create hierarchy with single asset.";
        return null;
    }

    protected override async Task<HierarchicalRiskParityResponse> ExecuteTypedAsync(
        HierarchicalRiskParityRequest request,
        CancellationToken cancellationToken)
    {
        // Deduplicate symbols to prevent 'duplicate key' errors in dictionaries
        var symbols = request.Symbols.Distinct().ToList();

        _logger?.LogInformation("Running Hierarchical Risk Parity for {Count} symbols", symbols.Count);

        var optimization = await Task.Run(() => OptimizeHRP(
            symbols,
            request.ReturnsData,
            request.LinkageMethod,
            request.RiskFreeRate), cancellationToken);

        _logger?.LogInformation("HRP optimization completed");

        return optimization;
    }

    private static HierarchicalRiskParityResponse OptimizeHRP(
        List<string> symbols,
        Dictionary<string, List<double>> returnsData,
        string linkageMethod,
        double riskFreeRate)
    {
        // HRP Algorithm (Marcos Lopez de Prado, 2016):
        // 1. Calculate correlation matrix and distance matrix
        // 2. Perform hierarchical clustering
        // 3. Quasi-diagonalize covariance matrix based on dendrogram
        // 4. Apply recursive bisection with inverse-variance weighting

        // Step 1: Calculate correlation and covariance matrices
        var correlationMatrix = FinancialMathHelper.CalculateCorrelationMatrix(symbols, returnsData);
        var covarianceMatrix = FinancialMathHelper.CalculateCovarianceMatrix(symbols, returnsData);

        // Step 2: Convert correlation to distance matrix
        var distanceMatrix = CalculateDistanceMatrix(correlationMatrix);

        // Step 3: Perform hierarchical clustering
        var dendrogram = PerformHierarchicalClustering(symbols, distanceMatrix, linkageMethod);

        // Step 4: Sort symbols based on dendrogram (quasi-diagonalization)
        var sortedSymbols = GetSortedSymbols(dendrogram);

        // Step 5: Apply recursive bisection to allocate weights
        var optimalWeights = RecursiveBisection(sortedSymbols, covarianceMatrix, returnsData);

        // Step 6: Calculate portfolio metrics
        var expectedReturns = symbols.ToDictionary(
            s => s,
            s => returnsData.TryGetValue(s, out var r) ? r.Mean() : 0.0
        );

        var portfolioReturn = optimalWeights.Sum(w => w.Value * expectedReturns.GetValueOrDefault(w.Key, 0));
        var portfolioVolatility = FinancialMathHelper.CalculatePortfolioVolatility(optimalWeights, covarianceMatrix);
        var sharpeRatio = portfolioVolatility > 0 ? (portfolioReturn - riskFreeRate) / portfolioVolatility : 0;

        // Step 7: Analyze cluster structure
        var clusterAnalysis = AnalyzeClusterStructure(dendrogram, optimalWeights);

        return new HierarchicalRiskParityResponse
        {
            Timestamp = DateTime.UtcNow,
            Method = "HIERARCHICAL_RISK_PARITY",
            Symbols = symbols,
            OptimalWeights = optimalWeights.ToDictionary(kvp => kvp.Key, kvp => Math.Round((decimal)kvp.Value, 4)),
            ExpectedReturn = Math.Round((decimal)(portfolioReturn * 100), 2),
            ExpectedVolatility = Math.Round((decimal)(portfolioVolatility * 100), 2),
            SharpeRatio = Math.Round((decimal)sharpeRatio, 2),
            ClusterStructure = clusterAnalysis,
            SortedOrder = sortedSymbols,
            LinkageMethod = linkageMethod,
            DiversificationRatio = CalculateDiversificationRatio(optimalWeights, covarianceMatrix)
        };
    }

    private static Dictionary<string, Dictionary<string, double>> CalculateDistanceMatrix(
        Dictionary<string, Dictionary<string, double>> correlationMatrix)
    {
        // Distance = sqrt(0.5 * (1 - correlation))
        var distanceMatrix = new Dictionary<string, Dictionary<string, double>>();

        foreach (var symbol1 in correlationMatrix.Keys)
        {
            distanceMatrix[symbol1] = new Dictionary<string, double>();

            foreach (var symbol2 in correlationMatrix.Keys)
            {
                var correlation = correlationMatrix[symbol1][symbol2];
                var distance = Math.Sqrt(0.5 * (1 - correlation));
                distanceMatrix[symbol1][symbol2] = distance;
            }
        }

        return distanceMatrix;
    }

    private static ClusterNode PerformHierarchicalClustering(
        List<string> symbols,
        Dictionary<string, Dictionary<string, double>> distanceMatrix,
        string linkageMethod)
    {
        // Simplified hierarchical clustering using single linkage
        // In production, use proper implementation with all linkage methods

        var clusters = symbols.Select(s => new ClusterNode
        {
            Symbols = new List<string> { s },
            IsLeaf = true
        }).ToList();

        while (clusters.Count > 1)
        {
            // Find two closest clusters
            var minDistance = double.MaxValue;
            int minI = 0, minJ = 1;

            for (int i = 0; i < clusters.Count; i++)
            {
                for (int j = i + 1; j < clusters.Count; j++)
                {
                    var distance = CalculateClusterDistance(
                        clusters[i].Symbols, clusters[j].Symbols, distanceMatrix, linkageMethod);

                    if (distance < minDistance)
                    {
                        minDistance = distance;
                        minI = i;
                        minJ = j;
                    }
                }
            }

            // Merge closest clusters
            var newCluster = new ClusterNode
            {
                Symbols = clusters[minI].Symbols.Concat(clusters[minJ].Symbols).ToList(),
                IsLeaf = false,
                Left = clusters[minI],
                Right = clusters[minJ],
                Distance = minDistance
            };

            clusters.RemoveAt(Math.Max(minI, minJ));
            clusters.RemoveAt(Math.Min(minI, minJ));
            clusters.Add(newCluster);
        }

        return clusters.First();
    }

    private static double CalculateClusterDistance(
        List<string> cluster1,
        List<string> cluster2,
        Dictionary<string, Dictionary<string, double>> distanceMatrix,
        string linkageMethod)
    {
        var distances = new List<double>();

        foreach (var s1 in cluster1)
        {
            foreach (var s2 in cluster2)
            {
                distances.Add(distanceMatrix.GetValueOrDefault(s1)?.GetValueOrDefault(s2) ?? 1.0);
            }
        }

        return linkageMethod.ToLower() switch
        {
            "single" => distances.Min(),
            "complete" => distances.Max(),
            "average" => distances.Average(),
            "ward" => distances.Average(), // Simplified
            _ => distances.Average()
        };
    }

    private static List<string> GetSortedSymbols(ClusterNode dendrogram)
    {
        // Traverse dendrogram in-order to get sorted symbol list
        var sorted = new List<string>();

        void Traverse(ClusterNode? node)
        {
            if (node == null) return;

            if (node.IsLeaf)
            {
                sorted.AddRange(node.Symbols);
            }
            else
            {
                Traverse(node.Left);
                Traverse(node.Right);
            }
        }

        Traverse(dendrogram);
        return sorted;
    }

    private static Dictionary<string, double> RecursiveBisection(
        List<string> sortedSymbols,
        Dictionary<string, Dictionary<string, double>> covarianceMatrix,
        Dictionary<string, List<double>> returnsData)
    {
        // Recursive bisection: split portfolio into two halves, allocate based on inverse variance
        var weights = new Dictionary<string, double>();

        void Bisect(List<string> symbols, double totalWeight)
        {
            if (symbols.Count == 1)
            {
                weights[symbols[0]] = totalWeight;
                return;
            }

            // Split into two halves
            var mid = symbols.Count / 2;
            var left = symbols.Take(mid).ToList();
            var right = symbols.Skip(mid).ToList();

            // Calculate cluster variances
            var leftVariance = CalculateClusterVariance(left, covarianceMatrix, returnsData);
            var rightVariance = CalculateClusterVariance(right, covarianceMatrix, returnsData);

            // Allocate inversely proportional to variance
            var totalInvVar = 1.0 / leftVariance + 1.0 / rightVariance;
            var leftWeight = (1.0 / leftVariance) / totalInvVar * totalWeight;
            var rightWeight = (1.0 / rightVariance) / totalInvVar * totalWeight;

            // Recurse
            Bisect(left, leftWeight);
            Bisect(right, rightWeight);
        }

        Bisect(sortedSymbols, 1.0);
        return weights;
    }

    private static double CalculateClusterVariance(
        List<string> symbols,
        Dictionary<string, Dictionary<string, double>> covarianceMatrix,
        Dictionary<string, List<double>> returnsData)
    {
        if (symbols.Count == 1)
        {
            return covarianceMatrix.GetValueOrDefault(symbols[0])?.GetValueOrDefault(symbols[0]) ?? 0.01;
        }

        // Equal-weighted cluster variance
        var equalWeights = symbols.ToDictionary(s => s, s => 1.0 / symbols.Count);
        return Math.Pow(FinancialMathHelper.CalculatePortfolioVolatility(equalWeights, covarianceMatrix), 2);
    }

    private static Dictionary<string, object> AnalyzeClusterStructure(ClusterNode dendrogram, Dictionary<string, double> weights)
    {
        var clusters = new List<Dictionary<string, object>>();

        void Analyze(ClusterNode? node, int depth)
        {
            if (node == null || node.IsLeaf) return;

            var clusterWeight = node.Symbols.Sum(s => weights.GetValueOrDefault(s, 0));

            clusters.Add(new Dictionary<string, object>
            {
                ["depth"] = depth,
                ["symbols"] = node.Symbols,
                ["size"] = node.Symbols.Count,
                ["total_weight"] = Math.Round((decimal)(clusterWeight * 100), 2),
                ["distance"] = Math.Round(node.Distance, 4)
            });

            Analyze(node.Left, depth + 1);
            Analyze(node.Right, depth + 1);
        }

        Analyze(dendrogram, 0);

        return new Dictionary<string, object>
        {
            ["num_clusters"] = clusters.Count,
            ["clusters"] = clusters.OrderBy(c => (int)c["depth"]).ToList(),
            ["max_depth"] = clusters.Count > 0 ? clusters.Max(c => (int)c["depth"]) : 0
        };
    }

    private static decimal CalculateDiversificationRatio(
        Dictionary<string, double> weights,
        Dictionary<string, Dictionary<string, double>> covarianceMatrix)
    {
        var weightedVolatilities = weights.Sum(w =>
        {
            var variance = covarianceMatrix.GetValueOrDefault(w.Key)?.GetValueOrDefault(w.Key) ?? 0;
            return w.Value * Math.Sqrt(variance);
        });

        var portfolioVolatility = FinancialMathHelper.CalculatePortfolioVolatility(weights, covarianceMatrix);

        return portfolioVolatility > 0 ? Math.Round((decimal)(weightedVolatilities / portfolioVolatility), 2) : 1;
    }

    private class ClusterNode
    {
        public required List<string> Symbols { get; init; }
        public required bool IsLeaf { get; init; }
        public ClusterNode? Left { get; init; }
        public ClusterNode? Right { get; init; }
        public double Distance { get; init; }
    }
}
