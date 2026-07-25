using System.Security.Cryptography;
using System.Text;
using Orkeon.Application.Interfaces.Security;

namespace Orkeon.Rag.Validation;

/// <summary>
/// Validates content integrity using SHA-256 hashing.
/// On first ingestion, allows the content. On subsequent access, verifies the hash matches.
/// Port of legacy Infrastructure <c>ContentIntegrityValidator</c> (RAG-02/C3).
/// </summary>
public sealed class ContentIntegrityValidator : IDataValidator
{
    /// <inheritdoc />
    public string Name => "ContentIntegrity";

    /// <inheritdoc />
    public Task<DataValidationResult> ValidateAsync(string content, DataValidationContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var currentHash = ComputeHash(content);

        // First ingestion — no stored hash to compare against
        if (string.IsNullOrEmpty(context.ContentHash))
        {
            return Task.FromResult(DataValidationResult.Allowed());
        }

        // Verify hash matches
        if (!string.Equals(currentHash, context.ContentHash, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(DataValidationResult.Rejected(
                "Content modified after ingestion",
                1.0,
                [$"Expected hash: {context.ContentHash}, Actual hash: {currentHash}"]));
        }

        return Task.FromResult(DataValidationResult.Allowed());
    }

    /// <summary>
    /// Computes the SHA-256 hash of the given content.
    /// </summary>
    public static string ComputeHash(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }
}
