using Microsoft.Extensions.Configuration;
using Orkeon.Tools.Drive.Configuration;
using Orkeon.Tools.Drive.Constants;
using Orkeon.Tools.Drive.Tests.Fixtures;

namespace Orkeon.Tools.Drive.Tests.Configuration;

/// <summary>Presets and validation of declared drive accounts.</summary>
public class DriveAccountResolverTests
{
    [Fact]
    public void Should_resolve_a_Google_account_to_the_loopback_flow_with_the_full_drive_scope()
    {
        var account = TestDriveAccounts.Resolve("perso", TestDriveAccounts.Google());

        Assert.Equal(DriveProvider.GoogleDrive, account.Provider);
        Assert.Equal(OAuthFlow.LoopbackPkce, account.OAuth.Flow);
        Assert.Equal(DriveDefaults.GoogleTokenEndpoint, account.OAuth.TokenEndpoint);
        Assert.Equal([DriveDefaults.GoogleDriveScope], account.OAuth.Scopes);
        Assert.False(account.OAuth.ScopesOnRefresh);
        Assert.Equal(["Orkeon", "agents"], account.Root);
        Assert.Equal("/Orkeon/agents", account.RootPath);
        Assert.Equal(100L * 1024 * 1024, account.MaxUploadBytes);
    }

    [Fact]
    public void Should_resolve_a_OneDrive_account_to_the_device_flow_of_the_consumers_tenant()
    {
        var account = TestDriveAccounts.Resolve("pro", TestDriveAccounts.OneDrive(root: null));

        Assert.Equal(OAuthFlow.DeviceCode, account.OAuth.Flow);
        Assert.Equal("https://login.microsoftonline.com/consumers/oauth2/v2.0/token", account.OAuth.TokenEndpoint.AbsoluteUri);
        Assert.Equal("https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode", account.OAuth.DeviceCodeEndpoint!.AbsoluteUri);
        Assert.Equal([DriveDefaults.GraphFilesReadWriteScope, DriveDefaults.OfflineAccessScope], account.OAuth.Scopes);
        Assert.True(account.OAuth.ScopesOnRefresh);
        Assert.Empty(account.Root);
        Assert.Equal("/", account.RootPath);
    }

    [Theory]
    [InlineData(true, DriveDefaults.GoogleDriveReadOnlyScope)]
    [InlineData(false, DriveDefaults.GraphFilesReadScope)]
    public void Should_ask_for_a_read_only_scope_when_the_account_only_reads(bool google, string scope)
    {
        var options = google ? TestDriveAccounts.Google(DriveRights.Read) : TestDriveAccounts.OneDrive(DriveRights.Read);

        Assert.Contains(scope, TestDriveAccounts.Resolve("a", options).OAuth.Scopes);
    }

    [Fact]
    public void Should_use_the_declared_Microsoft_tenant()
    {
        var account = TestDriveAccounts.Resolve("pro", TestDriveAccounts.OneDrive(tenant: "organizations"));

        Assert.Contains("/organizations/", account.OAuth.TokenEndpoint.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_collect_every_problem_of_an_empty_declaration()
    {
        var resolution = DriveAccountResolver.Resolve("empty", new DriveAccountOptions());

        Assert.Null(resolution.Account);
        Assert.Contains(resolution.Problems, problem => problem.StartsWith("Provider is required", StringComparison.Ordinal));
        Assert.Contains(resolution.Problems, problem => problem.StartsWith("Rights is required", StringComparison.Ordinal));
    }

    [Fact]
    public void Should_require_a_client_id_and_for_Google_a_client_secret_variable()
    {
        var google = TestDriveAccounts.Google();
        google.Auth.ClientId = null;
        var noSecret = TestDriveAccounts.Google();
        noSecret.Auth.ClientSecretEnvVar = " ";

        Assert.Contains(DriveAccountResolver.Resolve("a", google).Problems, problem => problem.StartsWith("Auth:ClientId is required", StringComparison.Ordinal));
        Assert.Contains(DriveAccountResolver.Resolve("a", noSecret).Problems, problem => problem.StartsWith("Auth:ClientSecretEnvVar is required", StringComparison.Ordinal));
    }

    [Theory]
    [Trait("BR", "CH-15")]
    [InlineData(true)]
    [InlineData(false)]
    public void ShouldSetTheAccountAside_WhenItsClientSecretVariableIsAReference(bool google)
    {
        var options = google ? TestDriveAccounts.Google() : TestDriveAccounts.OneDrive();
        options.Auth.ClientSecretEnvVar = "${GOOGLE_CLIENT_SECRET}";

        var resolution = DriveAccountResolver.Resolve("a", options);

        Assert.Null(resolution.Account);
        Assert.Contains(resolution.Problems, problem => problem.StartsWith("Auth:ClientSecretEnvVar holds a variable reference", StringComparison.Ordinal));
        Assert.DoesNotContain(resolution.Problems, problem => problem.Contains("GOOGLE_CLIENT_SECRET", StringComparison.Ordinal));
    }

    [Fact]
    public void Should_refuse_a_tenant_on_a_Google_account_and_a_malformed_tenant()
    {
        var google = TestDriveAccounts.Google();
        google.Auth.Tenant = "consumers";

        Assert.Contains(DriveAccountResolver.Resolve("a", google).Problems, problem => problem.Contains("Microsoft setting", StringComparison.Ordinal));
        Assert.Contains(DriveAccountResolver.Resolve("a", TestDriveAccounts.OneDrive(tenant: "x/../y")).Problems, problem => problem.Contains("not a tenant", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Orkeon/../secret")]
    [InlineData("a/./b")]
    [InlineData("bad:name")]
    public void Should_refuse_a_root_that_is_not_a_folder_path(string root)
    {
        Assert.Contains(DriveAccountResolver.Resolve("a", TestDriveAccounts.Google(root: root)).Problems, problem => problem.StartsWith("Root ", StringComparison.Ordinal));
    }

    [Fact]
    public void Should_read_a_root_written_with_backslashes_and_extra_slashes()
    {
        Assert.Equal(["Orkeon", "agents"], TestDriveAccounts.Resolve("a", TestDriveAccounts.Google(root: "\\Orkeon\\\\agents/")).Root);
    }

    [Theory]
    [InlineData("boss@example.com", true)]
    [InlineData("*@example.com", true)]
    [InlineData("*", true)]
    [InlineData("boss.example.com", false)]
    [InlineData("*@", false)]
    [InlineData("b*ss@example.com", false)]
    public void Should_validate_share_patterns(string pattern, bool valid)
    {
        var options = TestDriveAccounts.Google();
        options.Share.AllowedRecipients.Add(pattern);

        Assert.Equal(valid, DriveAccountResolver.Resolve("a", options).Account is not null);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(20000)]
    public void Should_refuse_an_upload_ceiling_out_of_bounds(int megabytes)
    {
        var options = TestDriveAccounts.Google();
        options.MaxUploadMegabytes = megabytes;

        Assert.Contains(DriveAccountResolver.Resolve("a", options).Problems, problem => problem.StartsWith("MaxUploadMegabytes", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("-lead")]
    [InlineData("has space")]
    public void Should_refuse_an_account_name_unusable_as_a_token_file_stem(string name)
    {
        Assert.Contains(DriveAccountResolver.Resolve(name, TestDriveAccounts.Google()).Problems, problem => problem.StartsWith("the account name", StringComparison.Ordinal));
    }

    [Fact]
    public void Binder_should_keep_an_account_with_an_unreadable_value_and_report_it()
    {
        var section = Section(new()
        {
            ["Accounts:good:Provider"] = "GoogleDrive",
            ["Accounts:bad:Provider"] = "Dropbox",
            ["Accounts:bad:Rights"] = "Read, Fly",
            ["Accounts:typo:Roots"] = "Orkeon",
        });
        var options = new DriveToolsOptions();

        DriveOptionsBinder.Bind(section, options);

        Assert.Equal(3, options.Accounts.Count);
        Assert.False(options.AccountProblems.ContainsKey("good"));
        Assert.Contains(options.AccountProblems["bad"], problem => problem.StartsWith("Provider is not one of GoogleDrive, OneDrive", StringComparison.Ordinal));
        Assert.Contains(options.AccountProblems["bad"], problem => problem.StartsWith("Rights is not a list of", StringComparison.Ordinal));
        Assert.Contains(options.AccountProblems["typo"], problem => problem.StartsWith("Roots is not an account setting", StringComparison.Ordinal));
    }

    [Theory]
    [Trait("BR", "CH-464")]
    [InlineData("3")]
    [InlineData("Read, 4")]
    public void Binder_should_refuse_rights_written_as_numbers(string rights)
    {
        var section = Section(new()
        {
            ["Accounts:numbers:Provider"] = "GoogleDrive",
            ["Accounts:numbers:Rights"] = rights,
        });
        var options = new DriveToolsOptions();

        DriveOptionsBinder.Bind(section, options);

        Assert.Contains(options.AccountProblems["numbers"], problem => problem.StartsWith("Rights holds a number where DriveRights takes names: write one or more of", StringComparison.Ordinal));
    }

    /// <summary>
    /// A comma list is a combination of flags: on <c>Provider</c>, which is no <c>[Flags]</c>
    /// enumeration, the converter bound <c>"GoogleDrive, OneDrive"</c> to <c>OneDrive</c> (#464).
    /// </summary>
    [Fact]
    [Trait("BR", "CH-464")]
    public void Binder_should_refuse_a_comma_list_where_one_name_is_expected()
    {
        var section = Section(new()
        {
            ["Accounts:list:Provider"] = "GoogleDrive, OneDrive",
            ["Accounts:list:Rights"] = "Read",
        });
        var options = new DriveToolsOptions();

        DriveOptionsBinder.Bind(section, options);

        Assert.Equal(
            ["Provider is a list where a single name is expected: write one of GoogleDrive, OneDrive"],
            options.AccountProblems["list"]);
    }

    /// <summary>No refusal repeats the value (#465): pasted in the wrong key, it may be a secret.</summary>
    [Theory]
    [Trait("BR", "CH-465")]
    [InlineData("Provider", "sk-test-0123456789abcdef")]
    [InlineData("Provider", "sk-test-0123, OneDrive")]
    [InlineData("Rights", "Read, sk-test-0123456789abcdef")]
    public void Binder_should_never_repeat_the_refused_value(string key, string value)
    {
        var section = Section(new()
        {
            ["Accounts:secret:Provider"] = "GoogleDrive",
            ["Accounts:secret:Rights"] = "Read",
            [$"Accounts:secret:{key}"] = value,
        });
        var options = new DriveToolsOptions();

        DriveOptionsBinder.Bind(section, options);

        var problem = Assert.Single(options.AccountProblems["secret"]);
        Assert.StartsWith(key, problem, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-test", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void Binder_should_read_the_share_policy_and_the_section_settings()
    {
        var section = Section(new()
        {
            ["DefaultAccount"] = "perso",
            ["CredentialsDirectory"] = "/srv/tokens",
            ["Screening:WithholdRejected"] = "true",
            ["Accounts:perso:Provider"] = "OneDrive",
            ["Accounts:perso:Rights"] = "Read, Share",
            ["Accounts:perso:Share:AllowedRecipients:0"] = "*@example.com",
            ["Accounts:perso:Share:AllowPublicLinks"] = "true",
            ["Accounts:perso:Auth:ClientId"] = "id",
        });
        var options = new DriveToolsOptions();

        DriveOptionsBinder.Bind(section, options);
        var account = TestDriveAccounts.Resolve("perso", options.Accounts["perso"]);

        Assert.Equal("perso", options.DefaultAccount);
        Assert.Equal("/srv/tokens", options.CredentialsDirectory);
        Assert.True(options.Screening.WithholdRejected);
        Assert.Equal(["*@example.com"], account.Share.AllowedRecipients);
        Assert.True(account.Share.AllowPublicLinks);
        Assert.True(DriveCredentialsLocation.NeedsTokenStore(section));
        Assert.Equal("/srv/tokens", DriveCredentialsLocation.ConfiguredDirectory(section));
    }

    [Fact]
    public void An_empty_section_needs_no_token_store()
    {
        Assert.False(DriveCredentialsLocation.NeedsTokenStore(Section(new())));
    }

    private static IConfiguration Section(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void The_binder_refusal_never_repeats_the_value()
    {
        // #465: the configuration binder quotes the value it cannot convert, which may be a secret.
        var refusal = DriveOptionsBinder.Refusal("a", new InvalidOperationException(
            "Failed to convert configuration value 'sk-secret-example' at 'Accounts:a:Port' to type 'System.Int32'."));

        Assert.Equal("Failed to convert the configuration value at 'Accounts:a:Port' to type 'System.Int32'.", refusal);
        Assert.Equal("another message", DriveOptionsBinder.Refusal("a", new InvalidOperationException("another message")));
    }

    /// <summary>
    /// #469 - an account named with an apostrophe: the binder's own refusal under it, produced by the
    /// binder itself, is reworded with its key and never quotes the value.
    /// </summary>
    [Fact]
    [Trait("BR", "CH-469")]
    public void The_binder_refusal_never_repeats_the_value_when_the_account_name_holds_an_apostrophe()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Orkeon:Tools:Drive:Accounts:l'equipe:MaxUploadMegabytes"] = "sk-secret' at 'x' to type 'y" })
            .Build();
        var binderRefusal = Assert.Throws<InvalidOperationException>(
            () => configuration.GetSection("Orkeon:Tools:Drive:Accounts:l'equipe").Bind(new DriveAccountOptions()));

        var refusal = DriveOptionsBinder.Refusal("l'equipe", binderRefusal);

        Assert.Equal("Failed to convert the configuration value at 'Orkeon:Tools:Drive:Accounts:l'equipe:MaxUploadMegabytes' to type 'System.Int32'.", refusal);
        Assert.DoesNotContain("sk-secret", refusal, StringComparison.Ordinal);
    }

    /// <summary>#469 - a binder's sentence that cannot be read back names the account's section, never the value.</summary>
    [Fact]
    [Trait("BR", "CH-469")]
    public void The_binder_refusal_names_the_account_section_when_it_cannot_be_read_back()
    {
        var refusal = DriveOptionsBinder.Refusal("l'equipe", new InvalidOperationException("Error_FailedBinding, sk-secret-example, Orkeon:Tools:Drive:Accounts:l'equipe:MaxUploadMegabytes, System.Int32"));

        Assert.Equal(
            "Failed to convert a configuration value under 'Orkeon:Tools:Drive:Accounts:l'equipe'; the binder's message is withheld, since it quotes the value.",
            refusal);
    }
}
