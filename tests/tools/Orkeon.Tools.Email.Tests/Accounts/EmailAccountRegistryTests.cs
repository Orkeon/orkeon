using Microsoft.Extensions.Options;
using Orkeon.Tools.Email.Accounts;
using Orkeon.Tools.Email.Configuration;
using Orkeon.Tools.Email.Tests.Fixtures;

namespace Orkeon.Tools.Email.Tests.Accounts;

/// <summary>Which account a call gets, and what it is told when there is none it can use.</summary>
public sealed class EmailAccountRegistryTests
{
    [Fact]
    public void Should_say_where_to_declare_an_account_When_none_is_configured()
    {
        var registry = Registry(new EmailToolsOptions());

        var error = Assert.Throws<EmailToolException>(() => registry.Resolve(null));

        Assert.Equal(EmailErrorCode.NotConfigured, error.Code);
        Assert.Equal(
            "No e-mail account is configured. Declare one under Orkeon:Tools:Email:Accounts (see the e-mail guide, docs/guides/email.md).",
            error.Message);
        Assert.Empty(registry.Names);
        Assert.Null(registry.DefaultName);
    }

    [Fact]
    public void Should_list_the_configured_accounts_When_an_unknown_one_is_named()
    {
        var registry = Registry(Options(("work", TestAccounts.Custom()), ("perso", TestAccounts.Gmail())));

        var error = Assert.Throws<EmailToolException>(() => registry.Resolve("nope"));

        Assert.Equal(EmailErrorCode.UnknownAccount, error.Code);
        Assert.Equal("Unknown e-mail account 'nope'. Configured: perso, work.", error.Message);
    }

    [Fact]
    public void Should_use_the_only_account_When_the_call_names_none()
    {
        var registry = Registry(Options(("solo", TestAccounts.Custom())));

        Assert.Equal("solo", registry.DefaultName);
        Assert.Equal("solo", registry.Resolve(null).Name);
        Assert.Equal("solo", registry.Resolve("  ").Name);
    }

    [Fact]
    public void Should_use_the_declared_default_account_When_the_call_names_none()
    {
        var options = Options(("work", TestAccounts.Custom()), ("perso", TestAccounts.Gmail()));
        options.DefaultAccount = " perso ";
        var registry = Registry(options);

        Assert.Equal("perso", registry.DefaultName);
        Assert.Equal("perso", registry.Resolve(null).Name);
        Assert.Equal("work", registry.Resolve("work").Name);
    }

    [Fact]
    public void Should_ask_for_an_account_When_several_exist_and_none_is_the_default()
    {
        var registry = Registry(Options(("work", TestAccounts.Custom()), ("perso", TestAccounts.Gmail())));

        var error = Assert.Throws<EmailToolException>(() => registry.Resolve(null));

        Assert.Equal(EmailErrorCode.UnknownAccount, error.Code);
        Assert.Equal("Several e-mail accounts are configured (perso, work): pass `account` to choose one.", error.Message);
        Assert.Null(registry.DefaultName);
    }

    [Fact]
    public void Should_report_a_default_account_that_does_not_exist()
    {
        var options = Options(("work", TestAccounts.Custom()));
        options.DefaultAccount = "ghost";
        var registry = Registry(options);

        var error = Assert.Throws<EmailToolException>(() => registry.Resolve(null));

        Assert.Equal(EmailErrorCode.UnknownAccount, error.Code);
        Assert.StartsWith("Unknown e-mail account 'ghost'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_explain_every_problem_of_a_misconfigured_account()
    {
        var broken = TestAccounts.Custom(EmailRights.None);
        broken.Incoming.Host = null;
        var registry = Registry(Options(("broken", broken)));

        var error = Assert.Throws<EmailToolException>(() => registry.Resolve("broken"));

        Assert.Equal(EmailErrorCode.InvalidConfiguration, error.Code);
        Assert.StartsWith("The e-mail account 'broken' is misconfigured: Rights is required", error.Message, StringComparison.Ordinal);
        Assert.Contains("; Incoming:Host is required for a Custom account", error.Message, StringComparison.Ordinal);
        Assert.EndsWith("(see the e-mail guide, docs/guides/email.md).", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_find_accounts_whatever_the_case_and_keep_their_declared_name()
    {
        var registry = Registry(Options(("Perso", TestAccounts.Gmail())));

        var account = registry.Resolve("PERSO");
        var inspected = registry.Inspect("perso");

        Assert.Equal("Perso", account.Name);
        Assert.Same(account, inspected.Account);
    }

    [Fact]
    public void Should_inspect_an_invalid_account_without_throwing()
    {
        var registry = Registry(Options(("broken", TestAccounts.Custom(EmailRights.None))));

        var resolution = registry.Inspect("broken");

        Assert.Null(resolution.Account);
        Assert.Single(resolution.Problems);
    }

    [Fact]
    public void Should_list_names_sorted_whatever_the_case()
    {
        var registry = Registry(Options(("zeta", TestAccounts.Custom()), ("Alpha", TestAccounts.Custom()), ("beta", TestAccounts.Custom())));

        Assert.Equal(["Alpha", "beta", "zeta"], registry.Names);
    }

    private static EmailToolsOptions Options(params (string Name, EmailAccountOptions Account)[] accounts)
    {
        var options = new EmailToolsOptions();
        foreach (var (name, account) in accounts)
            options.Accounts[name] = account;
        return options;
    }

    private static EmailAccountRegistry Registry(EmailToolsOptions options) => new(Microsoft.Extensions.Options.Options.Create(options));
}
