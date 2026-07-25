using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Scripting.Tests.Doubles;

/// <summary>
/// Hand-written double for <see cref="IIngestionPipeline"/>: returns <see cref="Report"/>
/// (its Collection echoed from the request) and records the last request received.
/// </summary>
public sealed class FakeIngestionPipeline : IIngestionPipeline
{
    /// <summary>Report returned by <see cref="IngestAsync"/>.</summary>
    public IngestionReport Report { get; set; } = new() { Collection = "unset" };

    /// <summary>Last request received.</summary>
    public IngestionRequest? LastRequest { get; private set; }

    /// <summary>Number of <see cref="IngestAsync"/> calls.</summary>
    public int CallCount { get; private set; }

    public Task<IngestionReport> IngestAsync(
        IngestionRequest request, CancellationToken cancellationToken = default)
    {
        CallCount++;
        LastRequest = request;
        return Task.FromResult(Report with { Collection = request.Collection });
    }
}
