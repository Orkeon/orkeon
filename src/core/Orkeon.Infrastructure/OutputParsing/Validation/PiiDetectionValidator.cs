using System.Text.RegularExpressions;
using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Infrastructure.OutputParsing.Validation;

/// <summary>
/// Detects personally identifiable information (PII) in output:
/// emails, phone numbers, SSNs, credit card numbers, IP addresses.
/// Priority: 50.
/// Does NOT block by default - sets IsValid=false with warning.
/// </summary>
public sealed partial class PiiDetectionValidator : IOutputValidator
{
    /// <inheritdoc />
    public string Name => "PiiDetectionValidator";

    /// <inheritdoc />
    public int Priority => 50;

    [GeneratedRegex(@"[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}")]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"(?:\+?\d{1,3}[-.\s]?)?(?:\(?\d{2,4}\)?[-.\s]?)?\d{3,4}[-.\s]?\d{4}")]
    private static partial Regex PhonePattern();

    [GeneratedRegex(@"\b\d{3}[-\s]?\d{2}[-\s]?\d{4}\b")]
    private static partial Regex SsnPattern();

    [GeneratedRegex(@"\b(?:\d{4}[-\s]?){3}\d{4}\b")]
    private static partial Regex CreditCardPattern();

    [GeneratedRegex(@"\b(?:(?:25[0-5]|2[0-4]\d|[01]?\d\d?)\.){3}(?:25[0-5]|2[0-4]\d|[01]?\d\d?)\b")]
    private static partial Regex Ipv4Pattern();

    private static readonly (string Name, Regex Pattern)[] PiiPatterns =
    [
        ("Email address", EmailPattern()),
        ("Phone number", PhonePattern()),
        ("SSN", SsnPattern()),
        ("Credit card number", CreditCardPattern()),
        ("IPv4 address", Ipv4Pattern()),
    ];

    /// <inheritdoc />
    public Task<OutputValidationResult> ValidateAsync(
        string output,
        OutputValidationContext context,
        CancellationToken ct = default)
    {
        var detectedPii = new List<string>();

        foreach (var (name, pattern) in PiiPatterns)
        {
            if (pattern.IsMatch(output))
            {
                detectedPii.Add(name);
            }
        }

        if (detectedPii.Count > 0)
        {
            return Task.FromResult(new OutputValidationResult(
                IsValid: false,
                ErrorMessage: $"PII detected in output: {string.Join(", ", detectedPii)}",
                SuggestedFix: "Please remove or redact any personally identifiable information from the output."));
        }

        return Task.FromResult(new OutputValidationResult(IsValid: true));
    }
}
