using Orkeon.Tools.Email.Auth;
using Orkeon.Tools.Email.Configuration;
using Orkeon.Tools.Email.Tests.Doubles;

namespace Orkeon.Tools.Email.Tests.Fixtures;

/// <summary>
/// An <see cref="EmailCredentialProvider"/> wired to test doubles: a fake clock, an in-memory
/// token store, a scripted identity provider and an environment holding the test password.
/// </summary>
internal sealed class CredentialsFixture : IDisposable
{
    /// <summary>Creates the fixture.</summary>
    public CredentialsFixture(EmailEnvironment? environment = null)
    {
        Http = new HttpClient(Handler, disposeHandler: false);
        OAuth = new OAuth2Client(Http, Time, (_, _) => Task.CompletedTask);
        Provider = new EmailCredentialProvider(Store, OAuth, environment ?? TestAccounts.PasswordEnvironment());
    }

    /// <summary>The scripted identity provider (and Graph, when shared).</summary>
    public FakeHttpMessageHandler Handler { get; } = new();

    /// <summary>The client over <see cref="Handler"/>.</summary>
    public HttpClient Http { get; }

    /// <summary>The fake clock.</summary>
    public FakeTimeProvider Time { get; } = new();

    /// <summary>The token store.</summary>
    public FakeEmailTokenStore Store { get; } = new();

    /// <summary>The OAuth client.</summary>
    public OAuth2Client OAuth { get; }

    /// <summary>The provider under test or in use.</summary>
    public EmailCredentialProvider Provider { get; }

    /// <summary>Stores a token for <paramref name="account"/> valid for another hour.</summary>
    public string SeedFreshToken(ResolvedEmailAccount account, string accessToken = "fresh-access-token")
    {
        Store.With(EmailCredentialProvider.TokenKey(account), new EmailTokenSet
        {
            AccessToken = accessToken,
            RefreshToken = "stored-refresh-token",
            ExpiresAt = Time.GetUtcNow().AddHours(1),
        });
        return accessToken;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Http.Dispose();
        Handler.Dispose();
    }
}
