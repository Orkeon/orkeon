using System.Text;
using System.Text.RegularExpressions;
using Orkeon.Application.Interfaces.Security;

namespace Orkeon.Infrastructure.Security.Dlp;

/// <summary>
/// Detects and masks personally identifiable information in text content.
/// </summary>
public interface IPiiDetector
{
    /// <summary>
    /// Scans content for PII and returns all matches.
    /// </summary>
    DlpScanResult Scan(string content);

    /// <summary>
    /// Masks detected PII matches in content.
    /// </summary>
    string Mask(string content, IReadOnlyList<PiiMatch> matches, char maskChar = '*');
}

/// <summary>
/// PII detector using source-generated regular expressions.
/// </summary>
public sealed partial class PiiDetector : IPiiDetector
{
    [GeneratedRegex(@"[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}")]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"(\+?\d{1,3}[-.\s]?)?\(?\d{2,4}\)?[-.\s]?\d{3,4}[-.\s]?\d{3,4}")]
    private static partial Regex PhonePattern();

    [GeneratedRegex(@"\b\d{3}-\d{2}-\d{4}\b")]
    private static partial Regex SsnPattern();

    [GeneratedRegex(@"\b(?:\d{4}[-\s]?){3}\d{4}\b")]
    private static partial Regex CreditCardPattern();

    [GeneratedRegex(@"\b[A-Z]{2}\d{2}[A-Z0-9]{4}\d{7}([A-Z0-9]?){0,16}\b")]
    private static partial Regex IbanPattern();

    [GeneratedRegex(@"\b[A-Z]{1,2}\d{6,9}\b")]
    private static partial Regex PassportPattern();

    [GeneratedRegex(@"\b(?:\d{1,3}\.){3}\d{1,3}\b")]
    private static partial Regex IpAddressPattern();

    private static readonly (PiiType Type, Regex Pattern)[] Patterns =
    [
        (PiiType.Email, EmailPattern()),
        (PiiType.Phone, PhonePattern()),
        (PiiType.Ssn, SsnPattern()),
        (PiiType.CreditCard, CreditCardPattern()),
        (PiiType.Iban, IbanPattern()),
        (PiiType.Passport, PassportPattern()),
        (PiiType.IpAddress, IpAddressPattern()),
    ];

    /// <inheritdoc />
    public DlpScanResult Scan(string content)
    {
        if (string.IsNullOrEmpty(content))
            return new DlpScanResult(false, Array.Empty<PiiMatch>());

        var matches = new List<PiiMatch>();

        foreach (var (type, pattern) in Patterns)
        {
            foreach (Match match in pattern.Matches(content))
            {
                matches.Add(new PiiMatch(type, match.Value, match.Index, match.Length));
            }
        }

        return new DlpScanResult(matches.Count > 0, matches);
    }

    /// <inheritdoc />
    public string Mask(string content, IReadOnlyList<PiiMatch> matches, char maskChar = '*')
    {
        ArgumentNullException.ThrowIfNull(matches);
        if (matches.Count == 0 || string.IsNullOrEmpty(content))
            return content;

        var sb = new StringBuilder(content);

        // Process matches in reverse order to preserve positions
        foreach (var match in matches.OrderByDescending(m => m.Position))
        {
            if (match.Position >= 0 && match.Position + match.Length <= sb.Length)
            {
                var mask = new string(maskChar, match.Length);
                sb.Remove(match.Position, match.Length);
                sb.Insert(match.Position, mask);
            }
        }

        return sb.ToString();
    }
}
