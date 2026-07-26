using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Rag.Tests.Doubles;

/// <summary>
/// Hand-written double for <see cref="IIngestionPipeline"/>: records every
/// <see cref="IngestionRequest"/> received and returns a configurable report.
/// </summary>
public sealed class FakeIngestionPipeline : IIngestionPipeline
{
    /// <summary>Report returned by <see cref="IngestAsync"/> (its Collection is echoed from the request).</summary>
    public IngestionReport Report { get; set; } = new() { Collection = "unset" };

    /// <summary>All requests received, in order.</summary>
    public List<IngestionRequest> Requests { get; } = [];

    public Task<IngestionReport> IngestAsync(
        IngestionRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        return Task.FromResult(Report with { Collection = request.Collection });
    }
}
