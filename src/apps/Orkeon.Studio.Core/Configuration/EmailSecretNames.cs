using System.Globalization;

namespace Orkeon.Studio.Core.Configuration;

/// <summary>
/// The environment variable Studio keeps an e-mail account's secret in when the file names none
/// (STUDIO-68): <c>EMAIL_&lt;ACCOUNT&gt;_PASSWORD</c> and <c>EMAIL_&lt;ACCOUNT&gt;_CLIENT_SECRET</c>,
/// the account's name in upper case with everything that is no letter or digit turned into
/// <c>_</c>. Two accounts may spell the same variable (<c>a.b</c> and <c>a-b</c>): the second
/// takes <c>_2</c>, the third <c>_3</c>. A name is taken whatever its case — Windows does not
/// tell <c>Email_X</c> from <c>EMAIL_X</c>.
/// <para>
/// The prefix is never <c>ORKEON_</c>: a run reads such a variable as configuration, its prefix
/// removed. A name the file already holds is not derived again — a form calls this for an empty
/// field only, so neither a new password nor a renamed account moves the variable.
/// </para>
/// </summary>
public static class EmailSecretNames
{
    private const string Prefix = "EMAIL_";
    private const string PasswordSuffix = "_PASSWORD";
    private const string ClientSecretSuffix = "_CLIENT_SECRET";

    /// <summary>The variable of the password of the account <paramref name="accountName"/>.</summary>
    /// <param name="accountName">The account's name, as the file holds it.</param>
    /// <param name="taken">The variables the other accounts of the file already name.</param>
    public static string PasswordFor(string accountName, IEnumerable<string> taken) =>
        Derive(accountName, PasswordSuffix, taken);

    /// <summary>The variable of the OAuth client secret of the account <paramref name="accountName"/>.</summary>
    /// <param name="accountName">The account's name, as the file holds it.</param>
    /// <param name="taken">The variables the other accounts of the file already name.</param>
    public static string ClientSecretFor(string accountName, IEnumerable<string> taken) =>
        Derive(accountName, ClientSecretSuffix, taken);

    private static string Derive(string accountName, string suffix, IEnumerable<string> taken)
    {
        ArgumentNullException.ThrowIfNull(accountName);
        ArgumentNullException.ThrowIfNull(taken);

        var held = taken
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var account = string.Concat(accountName.ToUpperInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_'));
        var name = Prefix + account + suffix;
        if (!held.Contains(name))
            return name;

        var rank = 2;
        while (held.Contains(Ranked(name, rank)))
            rank++;

        return Ranked(name, rank);
    }

    private static string Ranked(string name, int rank) => name + "_" + rank.ToString(CultureInfo.InvariantCulture);
}
