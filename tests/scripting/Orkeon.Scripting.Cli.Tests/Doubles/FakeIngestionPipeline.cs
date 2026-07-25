using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Scripting.Cli.Tests.Doubles;

/// <summary>
/// Hand-written double for <see cref="IIngestionPipeline"/>: returns <see cref="Report"/>
/// (its Collection echoed from the request) and records the last request received.
/// Pre-registered through <c>RagCommandOptionsBase.ConfigureTestServices</c> so it wins
/// the TryAdd race over the real pipeline.
/// </summary>
internal sealed class FakeIngestionPipeline : IIngestionPipeline
{
    /// <summary>Report returned by <see cref="IngestAsync"/>.</summary>
    public IngestionReport Report { get; set; } = new() { Collection = "unset" };

    /// <summary>Last request received.</summary>
    public IngestionRequest? LastRequest { get; private set; }

    /// <summary>Optional exception thrown instead of returning <see cref="Report"/>.</summary>
    public Exception? ThrowOnIngest { get; set; }

    public Task<IngestionReport> IngestAsync(
        IngestionRequest request, CancellationToken cancellationToken = default)
    {
        LastRequest = request;
        if (ThrowOnIngest is not null)
            throw ThrowOnIngest;
        return Task.FromResult(Report with { Collection = request.Collection });
    }
}
