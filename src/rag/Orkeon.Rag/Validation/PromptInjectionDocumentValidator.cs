using System.Text.RegularExpressions;
using Orkeon.Application.Interfaces.Security;

namespace Orkeon.Rag.Validation;

/// <summary>
/// Detects prompt injection attempts embedded in documents entering the RAG pipeline.
/// Reuses patterns from PromptSanitizer for consistency.
/// Port of <c>Orkeon.Infrastructure.Knowledge.Validation.PromptInjectionDocumentValidator</c> (RAG-02/C3).
/// </summary>
public sealed partial class PromptInjectionDocumentValidator : IDataValidator
{
    /// <inheritdoc />
    public string Name => "PromptInjectionDetection";

    // Prompt injection patterns (high risk)
    [GeneratedRegex(@"ignore\s+(all\s+)?previous\s+instructions", RegexOptions.IgnoreCase)]
    private static partial Regex IgnorePreviousInstructionsPattern();

    [GeneratedRegex(@"you\s+are\s+now\s+", RegexOptions.IgnoreCase)]
    private static partial Regex YouAreNowPattern();

    [GeneratedRegex(@"act\s+as\s+(if\s+)?", RegexOptions.IgnoreCase)]
    private static partial Regex ActAsPattern();

    [GeneratedRegex(@"pretend\s+(you\s+are|to\s+be)\s+", RegexOptions.IgnoreCase)]
    private static partial Regex PretendToBePattern();

    [GeneratedRegex(@"new\s+instructions\s*:", RegexOptions.IgnoreCase)]
    private static partial Regex NewInstructionsPattern();

    [GeneratedRegex(@"override\s+(your\s+)?(system|instructions|rules|guidelines)", RegexOptions.IgnoreCase)]
    private static partial Regex OverridePattern();

    [GeneratedRegex(@"disregard\s+(all\s+)?(previous|prior|above|your)", RegexOptions.IgnoreCase)]
    private static partial Regex DisregardPattern();

    [GeneratedRegex(@"forget\s+(all\s+|everything\s+)?(you(r|\s+(were|have)))?", RegexOptions.IgnoreCase)]
    private static partial Regex ForgetPattern();

    // System prompt leaking patterns (high risk)
    [GeneratedRegex(@"^system\s*:", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex SystemRolePattern();

    [GeneratedRegex(@"reveal\s+your\s+system\s+prompt", RegexOptions.IgnoreCase)]
    private static partial Regex RevealSystemPromptPattern();

    [GeneratedRegex(@"show\s+me\s+your\s+(system\s+)?prompt", RegexOptions.IgnoreCase)]
    private static partial Regex ShowPromptPattern();

    [GeneratedRegex(@"print\s+your\s+(initial\s+)?instructions", RegexOptions.IgnoreCase)]
    private static partial Regex PrintInstructionsPattern();

    // Encoded injection patterns (medium risk)
    [GeneratedRegex(@"base64[:\s]+[A-Za-z0-9+/=]{20,}", RegexOptions.IgnoreCase)]
    private static partial Regex Base64Pattern();

    [GeneratedRegex(@"\\u[0-9a-fA-F]{4}(\\u[0-9a-fA-F]{4}){3,}", RegexOptions.None)]
    private static partial Regex UnicodeEscapePattern();

    private static readonly (Func<Regex> PatternFactory, double Weight, string Description)[] DetectionRules =
    [
        // High-risk patterns (weight 0.4 each)
        (IgnorePreviousInstructionsPattern, 0.4, "Ignore previous instructions"),
        (YouAreNowPattern, 0.35, "Identity override attempt"),
        (OverridePattern, 0.4, "Override attempt"),
        (DisregardPattern, 0.4, "Disregard instructions attempt"),
        (ForgetPattern, 0.35, "Memory reset attempt"),
        (SystemRolePattern, 0.4, "System role injection"),
        (RevealSystemPromptPattern, 0.35, "System prompt reveal"),
        (ShowPromptPattern, 0.35, "Prompt reveal request"),
        (PrintInstructionsPattern, 0.35, "Instruction print request"),
        (NewInstructionsPattern, 0.4, "New instructions injection"),

        // Medium-risk patterns (weight 0.25 each)
        (ActAsPattern, 0.25, "Act as pattern"),
        (PretendToBePattern, 0.25, "Pretend to be pattern"),

        // Encoded patterns (weight 0.2 each)
        (Base64Pattern, 0.2, "Potential base64-encoded injection"),
        (UnicodeEscapePattern, 0.2, "Potential unicode-encoded injection"),
    ];

    /// <inheritdoc />
    public Task<DataValidationResult> ValidateAsync(string content, DataValidationContext context, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return Task.FromResult(DataValidationResult.Allowed());
        }

        var findings = new List<string>();
        double totalScore = 0.0;

        foreach (var (patternFactory, weight, description) in DetectionRules)
        {
            var pattern = patternFactory();
            var matches = pattern.Matches(content);
            if (matches.Count > 0)
            {
                findings.Add($"{description} ({matches.Count} occurrence(s))");
                totalScore += weight * matches.Count;
            }
        }

        // Cap at 1.0
        var riskScore = Math.Min(totalScore, 1.0);

        if (riskScore > 0.7)
        {
            return Task.FromResult(DataValidationResult.Rejected(
                "High-risk prompt injection detected in document",
                riskScore,
                findings));
        }

        if (riskScore >= 0.3)
        {
            return Task.FromResult(DataValidationResult.Quarantined(
                "Potential prompt injection detected in document",
                riskScore,
                findings));
        }

        return Task.FromResult(DataValidationResult.Allowed());
    }
}
