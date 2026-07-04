using Orkeon.Application.Interfaces.Security;
using Microsoft.Extensions.Logging;

namespace Orkeon.Infrastructure.Knowledge.Validation;

/// <summary>
/// Runs all registered data validators sequentially against document content.
/// First Reject wins. Any Quarantine triggers quarantine. All Allow means allow.
/// Tracks provenance for allowed documents.
/// </summary>
public sealed partial class DataValidationPipeline
{
    private readonly IEnumerable<IDataValidator> _validators;
    private readonly IQuarantineStore _quarantineStore;
    private readonly IProvenanceTracker _provenanceTracker;
    private readonly ILogger<DataValidationPipeline> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DataValidationPipeline"/> class.
    /// </summary>
    /// <param name="validators">The collection of data validators to run in sequence.</param>
    /// <param name="quarantineStore">The store for quarantined documents.</param>
    /// <param name="provenanceTracker">The tracker for document provenance records.</param>
    /// <param name="logger">The logger instance.</param>
    public DataValidationPipeline(
        IEnumerable<IDataValidator> validators,
        IQuarantineStore quarantineStore,
        IProvenanceTracker provenanceTracker,
        ILogger<DataValidationPipeline> logger)
    {
        _validators = validators;
        _quarantineStore = quarantineStore;
        _provenanceTracker = provenanceTracker;
        _logger = logger;
    }

    /// <summary>
    /// Validates document content through all registered validators.
    /// </summary>
    public Task<DataValidationResult> ValidateAsync(string content, DataValidationContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        return ValidateCoreAsync();

        async Task<DataValidationResult> ValidateCoreAsync()
        {
            var allFindings = new List<string>();
            DataValidationResult? quarantineResult = null;

            foreach (var validator in _validators)
            {
                var result = await validator.ValidateAsync(content, context, ct).ConfigureAwait(false);

                if (result.Findings.Count > 0)
                {
                    allFindings.AddRange(result.Findings);
                }

                if (result.Decision == DataValidationDecision.Reject)
                {
                    LogDocumentRejectedBy(context.DocumentId, validator.Name, result.Reason ?? "Unknown reason");

                    return DataValidationResult.Rejected(
                        result.Reason!,
                        result.RiskScore,
                        allFindings);
                }

                if (result.Decision == DataValidationDecision.Quarantine)
                {
                    quarantineResult ??= result;
                }
            }

            if (quarantineResult is not null)
            {
                LogDocumentQuarantined(context.DocumentId, quarantineResult.Reason ?? "Unknown reason");

                var finalResult = DataValidationResult.Quarantined(
                    quarantineResult.Reason!,
                    quarantineResult.RiskScore,
                    allFindings);

                await _quarantineStore.QuarantineAsync(context.DocumentId, content, finalResult, ct).ConfigureAwait(false);

                return finalResult;
            }

            // All validators allowed — track provenance
            await _provenanceTracker.TrackAsync(
                context.DocumentId,
                content,
                context.Source ?? "unknown",
                context.Metadata?.ToDictionary(kv => kv.Key, kv => kv.Value),
                ct).ConfigureAwait(false);

            LogDocumentPassedAllValidationChecks(context.DocumentId);

            return DataValidationResult.Allowed();
        }
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Document {DocumentId} rejected by {Validator}: {Reason}")]
    private partial void LogDocumentRejectedBy(string documentId, string validator, string reason);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Document {DocumentId} quarantined: {Reason}")]
    private partial void LogDocumentQuarantined(string documentId, string reason);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Document {DocumentId} passed all validation checks")]
    private partial void LogDocumentPassedAllValidationChecks(string documentId);

}
