using Microsoft.Extensions.Configuration;
using Orkeon.Tools.Email.Configuration;

namespace Orkeon.Tools.Email.Tests.Configuration;

/// <summary>What a host learns before its services exist: whether a token store is needed, and where.</summary>
public sealed class EmailCredentialsLocationTests
{
    [Theory]
    [InlineData("Provider", "Outlook", true)]
    [InlineData("Auth:ClientId", "client-id", true)]
    [InlineData("Auth:Method", "OAuth2", true)]
    [InlineData("Auth:Method", "Password", false)]
    [InlineData("Provider", "Gmail", false)]
    public void Should_need_a_token_store_only_When_an_account_signs_in_with_OAuth2(string key, string value, bool needed)
    {
        var section = Section(new Dictionary<string, string?>
        {
            ["Accounts:plain:Provider"] = "Custom",
            ["Accounts:plain:Auth:PasswordEnvVar"] = "SOME_PASSWORD",
            [$"Accounts:other:{key}"] = value,
        });

        Assert.Equal(needed, EmailCredentialsLocation.NeedsTokenStore(section));
    }

    [Fact]
    public void Should_need_no_token_store_When_no_account_is_declared()
    {
        Assert.False(EmailCredentialsLocation.NeedsTokenStore(Section([])));
    }

    [Theory]
    [InlineData(" /srv/orkeon/tokens ", "/srv/orkeon/tokens")]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void Should_report_the_configured_credentials_directory_trimmed_or_null(string? declared, string? expected)
    {
        var section = Section(new Dictionary<string, string?> { ["CredentialsDirectory"] = declared });

        Assert.Equal(expected, EmailCredentialsLocation.ConfiguredDirectory(section));
    }

    [Fact]
    public void Should_keep_tokens_in_the_email_subdirectory()
    {
        Assert.Equal("email", EmailCredentialsLocation.TokenSubdirectory);
    }

    private static IConfigurationSection Section(Dictionary<string, string?> values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(pair => "Orkeon:Tools:Email:" + pair.Key, pair => pair.Value))
            .Build()
            .GetSection("Orkeon:Tools:Email");
}
