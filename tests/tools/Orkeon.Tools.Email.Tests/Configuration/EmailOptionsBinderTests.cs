using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tools.Email.Accounts;
using Orkeon.Tools.Email.Configuration;
using Orkeon.Tools.Email.DependencyInjection;

namespace Orkeon.Tools.Email.Tests.Configuration;

/// <summary>
/// A value the configuration binder cannot convert never escapes the e-mail section: the other
/// accounts, the tool list and the host keep working, and the account that holds it says what
/// to fix when it is used.
/// </summary>
public sealed class EmailOptionsBinderTests
{
    private const string Section = "Orkeon:Tools:Email";

    [Fact]
    public void Should_keep_a_misspelt_account_aside_and_bind_the_others()
    {
        var options = Bind(new Dictionary<string, string?>
        {
            ["Accounts:broken:Provider"] = "Gmail",
            ["Accounts:broken:Address"] = "me@gmail.com",
            ["Accounts:broken:Rights"] = "Read, Organise",
            ["Accounts:sound:Provider"] = "Gmail",
            ["Accounts:sound:Address"] = "me@gmail.com",
            ["Accounts:sound:Rights"] = "Read",
        });

        Assert.Equal(["broken", "sound"], options.Accounts.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(EmailRights.None, options.Accounts["broken"].Rights);
        Assert.Equal(["Rights 'Read, Organise' is not a list of Read, Organize, Draft, Send, Delete, Purge, separated by commas"], options.AccountProblems["broken"]);
        Assert.Equal(EmailRights.Read, options.Accounts["sound"].Rights);
        Assert.False(options.AccountProblems.ContainsKey("sound"));
    }

    /// <summary>
    /// GAP-40 — a key no account carries was read as absent: <c>Incomming:Port</c> left the account on
    /// its provider's port. It sets the account aside like a value the binder cannot convert, naming the
    /// key and the keys of its level — the e-mail exception to the start refusal of every other setting.
    /// </summary>
    [Theory]
    [InlineData("Incomming:Port", "993", "Incomming is not an account setting", "Incoming")]
    [InlineData("Incoming:Prot", "993", "Incoming:Prot is not an account setting", "Port")]
    [InlineData("Send:AllowedRecipient:0", "*", "Send:AllowedRecipient is not an account setting", "AllowedRecipients")]
    public void Should_set_aside_an_account_carrying_a_key_no_account_carries(string key, string value, string problem, string known)
    {
        var options = Bind(new Dictionary<string, string?>
        {
            ["Accounts:a:Provider"] = "Gmail",
            ["Accounts:a:Address"] = "me@gmail.com",
            ["Accounts:a:Rights"] = "Read",
            [$"Accounts:a:{key}"] = value,
        });

        var reported = Assert.Single(options.AccountProblems["a"]);
        Assert.StartsWith(problem, reported, StringComparison.Ordinal);
        Assert.Contains(known, reported, StringComparison.Ordinal);
        Assert.Null(options.Accounts["a"].Address);
    }

    [Theory]
    [InlineData("Provider", "Yahoo", "Provider 'Yahoo' is not one of Custom, Gmail, Outlook")]
    [InlineData("Provider", "", "Provider '' is not one of Custom, Gmail, Outlook")]
    [InlineData("Rights", "", "Rights '' is not a list of Read, Organize, Draft, Send, Delete, Purge, separated by commas")]
    [InlineData("Incoming:Port", "nine", "Incoming:Port 'nine' is not a whole number")]
    [InlineData("Incoming:Security", "Tls", "Incoming:Security 'Tls' is not one of SslOnConnect, StartTls, None")]
    [InlineData("Outgoing:Security", "5", "Outgoing:Security '5' is not one of SslOnConnect, StartTls, None")]
    [InlineData("Auth:Method", "OAuth", "Auth:Method 'OAuth' is not one of Password, OAuth2")]
    [InlineData("Send:MaxPerHour", "ten", "Send:MaxPerHour 'ten' is not a whole number")]
    [InlineData("SaveSentCopy", "yes", "SaveSentCopy 'yes' is neither true nor false")]
    public void Should_name_each_value_that_cannot_be_read_in_the_operator_s_terms(string key, string value, string problem)
    {
        var options = Bind(new Dictionary<string, string?>
        {
            ["Accounts:perso:Address"] = "me@example.com",
            ["Accounts:perso:Rights"] = "Read",
            [$"Accounts:perso:{key}"] = value,
        });

        Assert.Equal([problem], options.AccountProblems["perso"]);
    }

    [Fact]
    public void Should_report_every_unreadable_value_of_an_account_at_once()
    {
        var options = Bind(new Dictionary<string, string?>
        {
            ["Accounts:perso:Rights"] = "Everything",
            ["Accounts:perso:Incoming:Port"] = "993a",
            ["Accounts:perso:TimeoutSeconds"] = "1.5",
        });

        Assert.Equal(3, options.AccountProblems["perso"].Count);
    }

    [Theory]
    [InlineData("Incoming:Port")]
    [InlineData("Auth:Method")]
    [InlineData("SaveSentCopy")]
    public void Should_read_an_empty_optional_value_as_unset(string key)
    {
        var options = Bind(new Dictionary<string, string?>
        {
            ["Accounts:perso:Rights"] = "Read",
            [$"Accounts:perso:{key}"] = "",
        });

        Assert.Empty(options.AccountProblems);
    }

    [Fact]
    public void Should_accept_the_rights_in_any_case_and_as_a_number()
    {
        var options = Bind(new Dictionary<string, string?>
        {
            ["Accounts:words:Rights"] = "read, ORGANIZE",
            ["Accounts:number:Rights"] = "3",
        });

        Assert.Empty(options.AccountProblems);
        Assert.Equal(EmailRights.Read | EmailRights.Organize, options.Accounts["words"].Rights);
        Assert.Equal(EmailRights.Read | EmailRights.Organize, options.Accounts["number"].Rights);
    }

    [Fact]
    public void Should_refuse_rights_the_enum_does_not_define()
    {
        var options = Bind(new Dictionary<string, string?> { ["Accounts:perso:Rights"] = "64" });

        Assert.Single(options.AccountProblems["perso"]);
    }

    [Fact]
    public void Should_withhold_and_say_so_When_the_screening_policy_cannot_be_read()
    {
        var options = Bind(new Dictionary<string, string?> { ["Screening:WithholdRejected"] = "yes" });

        Assert.True(options.Screening.WithholdRejected);
        Assert.Equal(["Orkeon:Tools:Email:Screening:WithholdRejected 'yes' is neither true nor false"], options.SectionProblems);
    }

    [Fact]
    public void Should_check_every_account_setting_that_is_not_a_string()
    {
        var checkedKeys = EmailOptionsBinder.TypedAccountSettings.Select(setting => setting.Key).ToHashSet(StringComparer.Ordinal);

        var typed = ScalarSettings(typeof(EmailAccountOptions), prefix: null)
            .Where(setting => setting.Type != typeof(string))
            .ToList();

        Assert.All(typed, setting => Assert.Contains(setting.Key, checkedKeys));
        Assert.Equal(typed.Count, checkedKeys.Count);
        Assert.All(EmailOptionsBinder.TypedAccountSettings, setting =>
            Assert.Equal(typed.Single(declared => declared.Key == setting.Key).Type, setting.Type));
    }

    [Fact]
    public void Should_report_an_unreadable_account_as_misconfigured_When_it_is_used()
    {
        var registry = new EmailAccountRegistry(Options.Create(Bind(new Dictionary<string, string?>
        {
            ["Accounts:perso:Address"] = "me@example.com",
            ["Accounts:perso:Rights"] = "Read, Organise",
        })));

        var error = Assert.Throws<EmailToolException>(() => registry.Resolve("perso"));

        Assert.Equal(EmailErrorCode.InvalidConfiguration, error.Code);
        Assert.StartsWith(
            "The e-mail account 'perso' is misconfigured: Rights 'Read, Organise' is not a list of",
            error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Should_report_an_unreadable_section_setting_for_every_account()
    {
        var registry = new EmailAccountRegistry(Options.Create(Bind(new Dictionary<string, string?>
        {
            ["Screening:WithholdRejected"] = "maybe",
            ["Accounts:perso:Provider"] = "Gmail",
            ["Accounts:perso:Address"] = "me@gmail.com",
            ["Accounts:perso:Rights"] = "Read",
            ["Accounts:perso:Auth:PasswordEnvVar"] = "SOME_PASSWORD",
        })));

        var resolution = registry.Inspect("perso");

        Assert.Null(resolution.Account);
        Assert.Equal(["Orkeon:Tools:Email:Screening:WithholdRejected 'maybe' is neither true nor false"], resolution.Problems);
    }

    [Fact]
    public void Should_build_every_tool_over_a_section_the_binder_cannot_convert()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{Section}:Screening:WithholdRejected"] = "maybe",
            [$"{Section}:Accounts:perso:Rights"] = "Read, Organise",
            [$"{Section}:Accounts:perso:Incoming:Port"] = "nine",
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService());
        services.AddOrkeonEmailTools(configuration);
        using var provider = services.BuildServiceProvider();

        var tools = provider.GetServices<IBaseTool>().ToList();

        Assert.Equal(13, tools.Count);
        Assert.Contains("perso", provider.GetRequiredService<IOptions<EmailToolsOptions>>().Value.AccountProblems.Keys);
    }

    [Fact]
    public void Should_tell_the_host_whether_it_needs_a_token_store_whatever_the_section_holds()
    {
        var section = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Accounts:broken:Provider"] = "Outlook",
            ["Accounts:broken:Rights"] = "Read, Organise",
            ["Accounts:hotmail:Provider"] = "Outlook",
            ["Accounts:hotmail:Rights"] = "Read",
        }).Build();

        Assert.True(EmailCredentialsLocation.NeedsTokenStore(section));
    }

    private static EmailToolsOptions Bind(Dictionary<string, string?> values)
    {
        var section = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var options = new EmailToolsOptions();
        EmailOptionsBinder.Bind(section, options);
        return options;
    }

    /// <summary>The settable scalar properties under <paramref name="type"/>, keyed as the configuration writes them.</summary>
    private static IEnumerable<(string Key, Type Type)> ScalarSettings(Type type, string? prefix)
    {
        foreach (var property in type.GetProperties())
        {
            var key = prefix is null ? property.Name : $"{prefix}:{property.Name}";
            var propertyType = property.PropertyType;
            if (propertyType.IsClass && propertyType != typeof(string) && !propertyType.IsGenericType)
            {
                foreach (var nested in ScalarSettings(propertyType, key))
                    yield return nested;
            }
            else if (property.CanWrite)
            {
                yield return (key, propertyType);
            }
        }
    }
}
