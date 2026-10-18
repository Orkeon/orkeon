using System.Net.Mail;

namespace Orkeon.Tools.Drive.Security;

/// <summary>
/// The share allow-list. A pattern is an address, <c>*@domain</c> or <c>*</c>; an empty list
/// allows nobody. Addresses are compared without case, the way mail systems compare them.
/// </summary>
internal static class RecipientPatterns
{
    private const string Anyone = "*";
    private const string DomainPrefix = "*@";

    /// <summary>Whether <paramref name="pattern"/> is a pattern this policy understands.</summary>
    public static bool IsValidPattern(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        if (pattern == Anyone)
            return true;

        if (pattern.StartsWith(DomainPrefix, StringComparison.Ordinal))
        {
            var domain = pattern[DomainPrefix.Length..];
            return domain.Length > 0 && !domain.Contains('@', StringComparison.Ordinal) && !domain.Contains('*', StringComparison.Ordinal);
        }

        return !pattern.Contains('*', StringComparison.Ordinal) && IsAddress(pattern);
    }

    /// <summary>Whether <paramref name="value"/> is a bare e-mail address (no display name).</summary>
    public static bool IsAddress(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return MailAddress.TryCreate(value, out var address)
            && string.Equals(address.Address, value, StringComparison.OrdinalIgnoreCase)
            && value.Contains('@', StringComparison.Ordinal);
    }

    /// <summary>Whether a pattern of <paramref name="patterns"/> allows <paramref name="address"/>.</summary>
    public static bool IsAllowed(string address, IReadOnlyList<string> patterns)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(patterns);
        foreach (var pattern in patterns)
        {
            if (pattern == Anyone)
                return true;

            if (pattern.StartsWith(DomainPrefix, StringComparison.Ordinal))
            {
                var at = address.LastIndexOf('@');
                if (at >= 0 && string.Equals(address[(at + 1)..], pattern[DomainPrefix.Length..], StringComparison.OrdinalIgnoreCase))
                    return true;
                continue;
            }

            if (string.Equals(address, pattern, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
