using Microsoft.Extensions.Configuration;

namespace Orkeon.Tools.Email.Configuration;

/// <summary>
/// What a host needs to know, before its services exist, to provide the OAuth token store:
/// whether any declared account signs in with OAuth2, and where the operator wants the tokens.
/// </summary>
public static class EmailCredentialsLocation
{
    /// <summary>The virtual directory, under the host's credentials root, the e-mail tokens live in.</summary>
    public const string TokenSubdirectory = "email";

    /// <summary>Whether the <c>Orkeon:Tools:Email</c> section declares an account that signs in with OAuth2.</summary>
    /// <param name="emailSection">The <c>Orkeon:Tools:Email</c> section.</param>
    public static bool NeedsTokenStore(IConfiguration emailSection)
    {
        ArgumentNullException.ThrowIfNull(emailSection);
        return Bind(emailSection).Accounts.Values
            .Any(account => EmailAccountResolver.EffectiveAuthMethod(account) == EmailAuthMethod.OAuth2);
    }

    /// <summary>The operator's <c>CredentialsDirectory</c>, or null to use the host's default.</summary>
    /// <param name="emailSection">The <c>Orkeon:Tools:Email</c> section.</param>
    public static string? ConfiguredDirectory(IConfiguration emailSection)
    {
        ArgumentNullException.ThrowIfNull(emailSection);
        var directory = Bind(emailSection).CredentialsDirectory;
        return string.IsNullOrWhiteSpace(directory) ? null : directory.Trim();
    }

    private static EmailToolsOptions Bind(IConfiguration emailSection)
    {
        var options = new EmailToolsOptions();
        emailSection.Bind(options);
        return options;
    }
}
