using MimeKit;

namespace Orkeon.Tools.Email.Security;

/// <summary>
/// The send allow-list. A pattern is an address, <c>*@domain</c> or <c>*</c>; an empty list
/// allows nobody. Only the address is compared — never the display name, which the sender
/// writes — and case is ignored the way mail systems ignore it.
/// </summary>
internal static class RecipientPolicy
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

        // MimeKit also parses a bare local part ("boss.example.com"): without the '@' check such a
        // typo would pass validation and then never match anyone.
        return !pattern.Contains('*', StringComparison.Ordinal)
            && MailboxAddress.TryParse(pattern, out var mailbox)
            && mailbox.Address.Contains('@', StringComparison.Ordinal)
            && string.Equals(mailbox.Address, pattern, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The recipients of <paramref name="recipients"/> that no pattern allows.</summary>
    public static IReadOnlyList<MailboxAddress> Disallowed(
        IEnumerable<MailboxAddress> recipients, IReadOnlyList<string> patterns)
    {
        ArgumentNullException.ThrowIfNull(recipients);
        ArgumentNullException.ThrowIfNull(patterns);
        return recipients.Where(recipient => !IsAllowed(recipient.Address, patterns)).ToList();
    }

    private static bool IsAllowed(string address, IReadOnlyList<string> patterns)
    {
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
