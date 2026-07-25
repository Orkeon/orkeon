using System.Collections.Concurrent;
using Orkeon.Application.Interfaces.Security;

namespace Orkeon.Rag.Validation;

/// <summary>
/// In-memory provenance tracker that records document hashes and metadata.
/// Port of legacy Infrastructure <c>ProvenanceTracker</c> (RAG-02/C3).
/// </summary>
public sealed class ProvenanceTracker : IProvenanceTracker
{
    private readonly ConcurrentDictionary<string, ProvenanceRecord> _records = new();

    /// <inheritdoc />
    public Task<ProvenanceRecord> TrackAsync(string documentId, string content, string source, IDictionary<string, string>? metadata = null, CancellationToken ct = default)
    {
        var hash = ContentIntegrityValidator.ComputeHash(content);
        var record = new ProvenanceRecord(
            DocumentId: documentId,
            ContentHash: hash,
            Source: source,
            IngestedAt: DateTimeOffset.UtcNow,
            Metadata: metadata ?? new Dictionary<string, string>());

        _records[documentId] = record;
        return Task.FromResult(record);
    }

    /// <inheritdoc />
    public Task<ProvenanceRecord?> GetAsync(string documentId, CancellationToken ct = default)
    {
        _records.TryGetValue(documentId, out var record);
        return Task.FromResult(record);
    }

    /// <inheritdoc />
    public Task<bool> VerifyIntegrityAsync(string documentId, string currentContent, CancellationToken ct = default)
    {
        if (!_records.TryGetValue(documentId, out var record))
        {
            return Task.FromResult(false);
        }

        var currentHash = ContentIntegrityValidator.ComputeHash(currentContent);
        var matches = string.Equals(currentHash, record.ContentHash, StringComparison.OrdinalIgnoreCase);

        // Update verification timestamp
        _records[documentId] = record with
        {
            LastVerifiedHash = currentHash,
            LastVerifiedAt = DateTimeOffset.UtcNow
        };

        return Task.FromResult(matches);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ProvenanceRecord>> ListAsync(string? sourceFilter = null, CancellationToken ct = default)
    {
        IReadOnlyList<ProvenanceRecord> result = sourceFilter is null
            ? _records.Values.ToList()
            : _records.Values.Where(r => string.Equals(r.Source, sourceFilter, StringComparison.OrdinalIgnoreCase)).ToList();

        return Task.FromResult(result);
    }
}
