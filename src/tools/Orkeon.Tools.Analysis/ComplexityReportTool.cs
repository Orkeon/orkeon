using Microsoft.Extensions.Logging;
using Orkeon.Analysis.Abstractions.DTOs.Queries;
using Orkeon.Analysis.Abstractions.DTOs.Responses;
using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Tools.Analysis;

public sealed class ComplexityReportTool : ToolBase<ComplexityReportRequest, ComplexityReportResponse>
{
    private const int DistributionSamples = 500;
    private readonly IRaggableStore _store;

    public ComplexityReportTool(IRaggableStore store, ILogger<ComplexityReportTool>? logger = null) : base(logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public override string Name => "complexity_report";
    public override string Description => "Top-N methods by complexity (Cyclomatic, NestingDepth, FanOut, LoC, Callers) with median and P95.";

    /// <summary>Declared access class for permission gates.</summary>
    public override ToolAccess Access => ToolAccess.Read;

    protected override Task<ComplexityReportResponse> ExecuteTypedAsync(ComplexityReportRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteTypedCoreAsync();

        async Task<ComplexityReportResponse> ExecuteTypedCoreAsync()
        {
            var topQuery = new ComplexityQuery
            {
                RootFqn = request.RootFqn,
                Metric = request.Metric,
                TopN = Math.Max(1, request.TopN),
            };
            var top = await _store.TopComplexityAsync(topQuery, cancellationToken).ConfigureAwait(false);

            var distributionQuery = new ComplexityQuery
            {
                RootFqn = request.RootFqn,
                Metric = request.Metric,
                TopN = DistributionSamples,
            };
            var distribution = await _store.TopComplexityAsync(distributionQuery, cancellationToken).ConfigureAwait(false);
            var (median, p95) = ComputePercentiles(distribution);

            return new ComplexityReportResponse
            {
                Metric = request.Metric,
                Top = [.. top],
                Median = median,
                P95 = p95,
                EvaluatedCount = distribution.Count,
            };
        }
    }

    private static (double? Median, double? P95) ComputePercentiles(IReadOnlyList<ComplexityEntry> entries)
    {
        if (entries.Count == 0) return (null, null);
        var values = entries.Select(e => e.Value).OrderBy(v => v).ToList();
        double Quantile(double q)
        {
            if (values.Count == 1) return values[0];
            var pos = q * (values.Count - 1);
            var low = (int)Math.Floor(pos);
            var high = (int)Math.Ceiling(pos);
            if (low == high) return values[low];
            return values[low] + (pos - low) * (values[high] - values[low]);
        }
        return (Quantile(0.5), Quantile(0.95));
    }
}
