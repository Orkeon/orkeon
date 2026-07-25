using System.Collections.Concurrent;
using Orkeon.Application.Interfaces.Security;

namespace Orkeon.Rag.Validation;

/// <summary>
/// A quarantined document pending review.
/// Port of <c>Orkeon.Infrastructure.Knowledge.Validation</c> quarantine types (RAG-02/C3).
/// </summary>
public record QuarantinedDocument(
    string DocumentId,
    string Content,
    DataValidationResult ValidationResult,
    DateTimeOffset QuarantinedAt,
    string? ReviewedBy = null,
    DateTimeOffset? ReviewedAt = null,
    bool? Approved = null);

/// <summary>
/// Store for documents that have been quarantined during validation.
/// </summary>
public interface IQuarantineStore
{
    /// <summary>
    /// Quarantines a document by storing it with its validation result for later review.
    /// </summary>
    /// <param name="documentId">The unique document identifier.</param>
    /// <param name="content">The document content being quarantined.</param>
    /// <param name="result">The validation result that triggered quarantine.</param>
    /// <param name="ct">Cancellation token.</param>
    Task QuarantineAsync(string documentId, string content, DataValidationResult result, CancellationToken ct = default);

    /// <summary>
    /// Retrieves a quarantined document by its identifier.
    /// </summary>
    /// <param name="documentId">The document identifier to look up.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The quarantined document, or null if not found.</returns>
    Task<QuarantinedDocument?> GetAsync(string documentId, CancellationToken ct = default);

    /// <summary>
    /// Lists all quarantined documents that have not yet been reviewed.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A read-only list of pending quarantined documents.</returns>
    Task<IReadOnlyList<QuarantinedDocument>> ListPendingAsync(CancellationToken ct = default);

    /// <summary>
    /// Records the review decision for a quarantined document.
    /// </summary>
    /// <param name="documentId">The document identifier to review.</param>
    /// <param name="approved">Whether the document is approved or rejected.</param>
    /// <param name="reviewedBy">The identifier of the reviewer.</param>
    /// <param name="ct">Cancellation token.</param>
    Task ReviewAsync(string documentId, bool approved, string reviewedBy, CancellationToken ct = default);
}

/// <summary>
/// In-memory implementation of <see cref="IQuarantineStore"/>.
/// </summary>
public sealed class InMemoryQuarantineStore : IQuarantineStore
{
    private readonly ConcurrentDictionary<string, QuarantinedDocument> _documents = new();

    /// <inheritdoc />
    public Task QuarantineAsync(string documentId, string content, DataValidationResult result, CancellationToken ct = default)
    {
        var doc = new QuarantinedDocument(
            DocumentId: documentId,
            Content: content,
            ValidationResult: result,
            QuarantinedAt: DateTimeOffset.UtcNow);

        _documents[documentId] = doc;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<QuarantinedDocument?> GetAsync(string documentId, CancellationToken ct = default)
    {
        _documents.TryGetValue(documentId, out var doc);
        return Task.FromResult(doc);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<QuarantinedDocument>> ListPendingAsync(CancellationToken ct = default)
    {
        IReadOnlyList<QuarantinedDocument> pending = _documents.Values
            .Where(d => d.Approved is null)
            .ToList();

        return Task.FromResult(pending);
    }

    /// <inheritdoc />
    public Task ReviewAsync(string documentId, bool approved, string reviewedBy, CancellationToken ct = default)
    {
        if (_documents.TryGetValue(documentId, out var doc))
        {
            _documents[documentId] = doc with
            {
                Approved = approved,
                ReviewedBy = reviewedBy,
                ReviewedAt = DateTimeOffset.UtcNow
            };
        }

        return Task.CompletedTask;
    }
}
