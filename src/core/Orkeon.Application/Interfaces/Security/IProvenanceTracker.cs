namespace Orkeon.Application.Interfaces.Security;

/// <summary>
/// Record tracking the provenance of a document.
/// </summary>
public record ProvenanceRecord(
    string DocumentId,
    string ContentHash,
    string Source,
    DateTimeOffset IngestedAt,
    IDictionary<string, string> Metadata,
    string? LastVerifiedHash = null,
    DateTimeOffset? LastVerifiedAt = null);

/// <summary>
/// Tracks document provenance including content hashes and source metadata.
/// </summary>
public interface IProvenanceTracker
{
    /// <summary>
    /// Records provenance information for a document by computing its content hash and storing metadata.
    /// </summary>
    /// <param name="documentId">The unique document identifier.</param>
    /// <param name="content">The document content to hash.</param>
    /// <param name="source">The origin source of the document.</param>
    /// <param name="metadata">Optional key-value metadata about the document.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created provenance record.</returns>
    System.Threading.Tasks.Task<ProvenanceRecord> TrackAsync(string documentId, string content, string source, IDictionary<string, string>? metadata = null, CancellationToken ct = default);

    /// <summary>
    /// Retrieves the provenance record for a document.
    /// </summary>
    /// <param name="documentId">The document identifier to look up.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The provenance record, or null if not found.</returns>
    System.Threading.Tasks.Task<ProvenanceRecord?> GetAsync(string documentId, CancellationToken ct = default);

    /// <summary>
    /// Verifies that a document's content has not been modified since ingestion by comparing hashes.
    /// </summary>
    /// <param name="documentId">The document identifier to verify.</param>
    /// <param name="currentContent">The current content to verify against the stored hash.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the content matches the original hash; false otherwise.</returns>
    System.Threading.Tasks.Task<bool> VerifyIntegrityAsync(string documentId, string currentContent, CancellationToken ct = default);

    /// <summary>
    /// Lists provenance records, optionally filtered by source.
    /// </summary>
    /// <param name="sourceFilter">Optional source name to filter by.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A read-only list of matching provenance records.</returns>
    System.Threading.Tasks.Task<IReadOnlyList<ProvenanceRecord>> ListAsync(string? sourceFilter = null, CancellationToken ct = default);
}
