using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Responses;
using Orkeon.Analysis.Abstractions.Interfaces;

namespace Orkeon.Analysis.Core;

/// <summary>
/// Default implementation of <see cref="ICitationBlockValidator"/>.
/// Parses fenced code blocks from markdown, extracts citation headers
/// (<c>// FQN  :</c> and <c>// SHA  : sha256:</c>), and verifies them
/// against the live <see cref="IRaggableStore"/>.
/// </summary>
public sealed partial class CitationBlockValidator : ICitationBlockValidator
{
    [GeneratedRegex(@"```(?:\w+)?\n(.*?)```", RegexOptions.Singleline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex FencedBlockRegex();

    [GeneratedRegex(@"^// FQN  : (.+)$", RegexOptions.Multiline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex FqnHeaderRegex();

    [GeneratedRegex(@"^// SHA  : sha256:([0-9a-fA-F]+)$", RegexOptions.Multiline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ShaHeaderRegex();

    private readonly IRaggableStore _store;

    /// <summary>
    /// Initializes a new instance of <see cref="CitationBlockValidator"/>.
    /// </summary>
    /// <param name="store">The RaggableTree store used to retrieve ground-truth source slices.</param>
    public CitationBlockValidator(IRaggableStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <inheritdoc/>
    public async Task<ValidationResult> ValidateAsync(string fileContent, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(fileContent))
            return new ValidationResult { IsValid = true };

        var violations = ImmutableArray.CreateBuilder<string>();

        foreach (Match blockMatch in FencedBlockRegex().Matches(fileContent))
        {
            ct.ThrowIfCancellationRequested();
            await ValidateBlockAsync(blockMatch.Groups[1].Value, violations, ct).ConfigureAwait(false);
        }

        return new ValidationResult
        {
            IsValid = violations.Count == 0,
            Violations = violations.ToImmutable(),
        };
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Service-boundary fault barrier: any store lookup failure is recorded as a validation violation so a flaky store cannot abort citation validation. Cancellation is rethrown.")]
    private async Task ValidateBlockAsync(
        string blockBody, ImmutableArray<string>.Builder violations, CancellationToken ct)
    {
        // Extract FQN and SHA headers — skip blocks that lack them
        var fqnMatch = FqnHeaderRegex().Match(blockBody);
        var shaMatch = ShaHeaderRegex().Match(blockBody);

        if (!fqnMatch.Success || !shaMatch.Success)
            return; // not a citation block — skip silently

        var fqn = fqnMatch.Groups[1].Value.Trim();
        var headerSha = shaMatch.Groups[1].Value.Trim();

        // Retrieve ground-truth slice from the store
        SourceSlice? slice;
        try
        {
            slice = await _store.GetSourceAsync(fqn, SourceMode.FullSpan, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            violations.Add($"FQN={fqn}: store lookup failed — {ex.Message}");
            return;
        }

        if (slice is null)
        {
            violations.Add($"FQN={fqn}: symbol not found in store");
            return;
        }

        // Check SHA prefix match
        if (!slice.Sha256.StartsWith(headerSha, StringComparison.OrdinalIgnoreCase))
        {
            violations.Add($"FQN={fqn}: expected SHA sha256:{headerSha}, got sha256:{slice.Sha256}");
            // Still check body (collect all violations per block)
        }

        // Strip header lines from blockBody to get the actual code body
        var codeBody = StripCitationHeaders(blockBody);

        // Normalise line endings then check containment
        var normalizedSource = slice.Source.Replace("\r\n", "\n", StringComparison.Ordinal);
        var normalizedBody = codeBody.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();

        if (!string.IsNullOrEmpty(normalizedBody) &&
            !normalizedSource.Contains(normalizedBody, StringComparison.Ordinal))
        {
            violations.Add($"FQN={fqn}: code body mismatch — cited body not found in ground-truth source");
        }
    }

    /// <summary>
    /// Removes the citation header lines (<c>// FQN  :</c>, <c>// File :</c>,
    /// <c>// SHA  :</c>) from a fenced block body so that only the actual code remains.
    /// </summary>
    private static string StripCitationHeaders(string blockBody)
    {
        var lines = blockBody.Split('\n');
        var result = new List<string>(lines.Length);
        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("// FQN  :", StringComparison.Ordinal)) continue;
            if (trimmed.StartsWith("// File :", StringComparison.Ordinal)) continue;
            if (trimmed.StartsWith("// SHA  :", StringComparison.Ordinal)) continue;
            result.Add(line);
        }
        return string.Join('\n', result);
    }
}
