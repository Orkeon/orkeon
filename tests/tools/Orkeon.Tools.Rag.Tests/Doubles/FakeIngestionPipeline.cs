using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Tools.Rag.Tests.Doubles;

/// <summary>
/// Hand-written double for <see cref="IIngestionPipeline"/>: returns <see cref="Report"/>
/// and records the last <see cref="IngestionRequest"/> received.
/// </summary>
public sealed class FakeIngestionPipeline : IIngestionPipeline
{
    /// <summary>Report returned by <see cref="IngestAsync"/> (its Collection is echoed from the request).</summary>
    public IngestionReport Report { get; set; } = new() { Collection = "unset" };

    /// <summary>Last request received.</summary>
    public IngestionRequest? LastRequest { get; private set; }

    /// <summary>Number of <see cref="IngestAsync"/> calls.</summary>
    public int CallCount { get; private set; }

    /// <summary>Optional exception thrown instead of returning <see cref="Report"/>.</summary>
    public Exception? ThrowOnIngest { get; set; }

    public Task<IngestionReport> IngestAsync(
        IngestionRequest request, CancellationToken cancellationToken = default)
    {
        CallCount++;
        LastRequest = request;
        if (ThrowOnIngest is not null)
            throw ThrowOnIngest;
        return Task.FromResult(Report with { Collection = request.Collection });
    }
}
