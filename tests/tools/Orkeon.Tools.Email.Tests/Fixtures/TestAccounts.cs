using Orkeon.Tools.Email.Auth;
using Orkeon.Tools.Email.Configuration;

namespace Orkeon.Tools.Email.Tests.Fixtures;

/// <summary>Declared and resolved accounts the tests share.</summary>
internal static class TestAccounts
{
    /// <summary>The address of every test account.</summary>
    public const string Address = "agent@example.test";

    /// <summary>The environment variable test password accounts read.</summary>
    public const string PasswordVariable = "ORKEON_TEST_MAIL_PASSWORD";

    /// <summary>The password in <see cref="PasswordVariable"/>.</summary>
    public const string Password = "s3cret-app-password";

    /// <summary>Every right.</summary>
    public const EmailRights AllRights =
        EmailRights.Read | EmailRights.Organize | EmailRights.Draft | EmailRights.Send | EmailRights.Delete | EmailRights.Purge;

    /// <summary>An environment holding exactly <paramref name="variables"/>.</summary>
    public static EmailEnvironment Environment(params (string Name, string Value)[] variables)
    {
        var values = variables.ToDictionary(v => v.Name, v => v.Value, StringComparer.Ordinal);
        return new EmailEnvironment(name => values.GetValueOrDefault(name));
    }

    /// <summary>An environment holding the test password.</summary>
    public static EmailEnvironment PasswordEnvironment() => Environment((PasswordVariable, Password));

    /// <summary>A custom IMAP/SMTP account declaration with explicit hosts.</summary>
    public static EmailAccountOptions Custom(EmailRights rights = EmailRights.Read)
    {
        var options = new EmailAccountOptions
        {
            Provider = EmailProvider.Custom,
            Address = Address,
            Rights = rights,
            Incoming = { Host = "imap.example.test" },
            Outgoing = { Host = "smtp.example.test" },
            Auth = { Method = EmailAuthMethod.Password, PasswordEnvVar = PasswordVariable },
        };
        return options;
    }

    /// <summary>A Gmail account declaration signing in with an app password.</summary>
    public static EmailAccountOptions Gmail(EmailRights rights = EmailRights.Read) => new()
    {
        Provider = EmailProvider.Gmail,
        Address = "someone@gmail.com",
        Rights = rights,
        Auth = { PasswordEnvVar = PasswordVariable },
    };

    /// <summary>A Gmail account declaration signing in with OAuth2.</summary>
    public static EmailAccountOptions GmailOAuth(EmailRights rights = EmailRights.Read) => new()
    {
        Provider = EmailProvider.Gmail,
        Address = "someone@gmail.com",
        Rights = rights,
        Auth = { Method = EmailAuthMethod.OAuth2, ClientId = "google-client.apps.googleusercontent.com", ClientSecretEnvVar = "ORKEON_TEST_GOOGLE_SECRET" },
    };

    /// <summary>An Outlook.com account declaration (Graph, OAuth2 device code).</summary>
    public static EmailAccountOptions Outlook(EmailRights rights = EmailRights.Read) => new()
    {
        Provider = EmailProvider.Outlook,
        Address = "someone@outlook.com",
        Rights = rights,
        Auth = { ClientId = "00000000-1111-2222-3333-444444444444" },
    };

    /// <summary>Resolves a declaration that must be valid.</summary>
    public static ResolvedEmailAccount Resolve(string name, EmailAccountOptions options)
    {
        var resolution = EmailAccountResolver.Resolve(name, options);
        if (resolution.Account is null)
            Assert.Fail("The test account should resolve: " + string.Join("; ", resolution.Problems));
        return resolution.Account;
    }

    /// <summary>A custom account reaching a local fake server in clear text with the test password.</summary>
    public static ResolvedEmailAccount Loopback(
        IncomingProtocol protocol, int incomingPort, int? smtpPort = null, EmailRights rights = EmailRights.Read | EmailRights.Organize | EmailRights.Draft | EmailRights.Delete | EmailRights.Purge,
        bool? saveSentCopy = null)
    {
        var options = new EmailAccountOptions
        {
            Provider = EmailProvider.Custom,
            Address = Address,
            Rights = rights,
            TimeoutSeconds = 20,
            SaveSentCopy = saveSentCopy,
            Incoming = { Protocol = protocol, Host = "127.0.0.1", Port = incomingPort, Security = TransportSecurity.None },
            Auth = { Method = EmailAuthMethod.Password, PasswordEnvVar = PasswordVariable },
        };
        if (smtpPort is { } port)
        {
            options.Outgoing.Host = "127.0.0.1";
            options.Outgoing.Port = port;
            options.Outgoing.Security = TransportSecurity.None;
        }

        return Resolve("local", options);
    }

    /// <summary><paramref name="account"/> recast as a Gmail account signing in with a password.</summary>
    public static ResolvedEmailAccount AsGmail(ResolvedEmailAccount account) => account with { Provider = EmailProvider.Gmail };

    /// <summary><paramref name="account"/> recast as a Gmail account signing in with OAuth2 (XOAUTH2).</summary>
    public static ResolvedEmailAccount AsGmailOAuth(ResolvedEmailAccount account) => account with
    {
        Provider = EmailProvider.Gmail,
        Auth = new ResolvedAuth
        {
            Method = EmailAuthMethod.OAuth2,
            Username = account.Address,
            OAuth = new OAuthSettings
            {
                ClientId = "google-client.apps.googleusercontent.com",
                Flow = OAuthFlow.LoopbackPkce,
                TokenEndpoint = new Uri("https://oauth2.googleapis.com/token"),
                AuthorizationEndpoint = new Uri("https://accounts.google.com/o/oauth2/v2/auth"),
                Scopes = ["https://mail.google.com/"],
            },
        },
    };
}
