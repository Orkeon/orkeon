using System.Collections.Immutable;

namespace Orkeon.Analysis.Abstractions.Interfaces;

/// <summary>
/// Validates citation fenced-code blocks embedded in a markdown document.
/// Each block that carries <c>// FQN  :</c> and <c>// SHA  : sha256:</c> headers
/// is verified against the live <see cref="IRaggableStore"/>.
/// Blocks without those headers are silently skipped.
/// </summary>
public interface ICitationBlockValidator
{
    /// <summary>
    /// Validates all citation blocks found in <paramref name="fileContent"/>.
    /// </summary>
    /// <param name="fileContent">Full text of the markdown/code file to validate.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A <see cref="ValidationResult"/> whose <c>IsValid</c> is <see langword="true"/> when
    /// every citation block matches the ground-truth store; <see langword="false"/> otherwise,
    /// with <c>Violations</c> listing one string per failing block.
    /// </returns>
    Task<ValidationResult> ValidateAsync(string fileContent, CancellationToken ct);
}

/// <summary>
/// Result of a citation-block validation pass.
/// </summary>
public sealed record ValidationResult
{
    /// <summary>
    /// <see langword="true"/> when all citation blocks passed (or none were found).
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// Human-readable violation messages, one per failing block.
    /// Empty when <see cref="IsValid"/> is <see langword="true"/>.
    /// </summary>
    public ImmutableArray<string> Violations { get; init; } = [];
}
