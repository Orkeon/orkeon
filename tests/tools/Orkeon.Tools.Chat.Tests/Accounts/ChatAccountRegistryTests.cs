using Microsoft.Extensions.Options;
using Orkeon.Tools.Chat.Accounts;
using Orkeon.Tools.Chat.Configuration;
using Orkeon.Tools.Chat.Tests.Fixtures;

namespace Orkeon.Tools.Chat.Tests.Accounts;

/// <summary>Which account a call gets, and what it is told when there is none.</summary>
public class ChatAccountRegistryTests
{
    [Fact]
    public void Should_refuse_every_call_when_no_account_is_configured()
    {
        var error = Assert.Throws<ChatToolException>(() => Registry().Resolve(null));

        Assert.Equal(ChatErrorCode.NotConfigured, error.Code);
        Assert.Contains("Orkeon:Tools:Chat:Accounts", error.Message, StringComparison.Ordinal);
        Assert.Contains("docs/guides/chat.md", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_use_the_only_account_by_default_and_ask_which_one_among_several()
    {
        Assert.Equal("perso", Registry(("perso", TestChatAccounts.Teams())).Resolve(null).Name);

        var error = Assert.Throws<ChatToolException>(() => Registry(("a", TestChatAccounts.Teams()), ("b", TestChatAccounts.Teams())).Resolve(" "));
        Assert.Equal(ChatErrorCode.UnknownAccount, error.Code);
        Assert.Contains("a, b", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_name_the_problems_of_a_misconfigured_account()
    {
        var error = Assert.Throws<ChatToolException>(() => Registry(("broken", new ChatAccountOptions())).Resolve("BROKEN"));

        Assert.Equal(ChatErrorCode.InvalidConfiguration, error.Code);
        Assert.Contains("Provider is required", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_refuse_an_unknown_account_with_the_configured_ones()
    {
        var error = Assert.Throws<ChatToolException>(() => Registry(("perso", TestChatAccounts.Teams())).Resolve("pro"));

        Assert.Equal(ChatErrorCode.UnknownAccount, error.Code);
        Assert.Contains("Configured: perso", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_list_the_names_sorted_and_keep_a_resolution()
    {
        var registry = Registry(("b", TestChatAccounts.Teams()), ("a", TestChatAccounts.Teams()));

        Assert.Equal(["a", "b"], registry.Names);
        Assert.Null(registry.DefaultName);
        Assert.Same(registry.Resolve("a"), registry.Resolve(" A "));
    }

    [Fact]
    public void Should_use_the_declared_default_and_refuse_one_that_does_not_exist()
    {
        var options = new ChatToolsOptions { DefaultAccount = " b " };
        options.Accounts["a"] = TestChatAccounts.Teams();
        options.Accounts["b"] = TestChatAccounts.Teams();
        Assert.Equal("b", new ChatAccountRegistry(Options.Create(options)).Resolve(null).Name);

        options.DefaultAccount = "gone";
        var error = Assert.Throws<ChatToolException>(() => new ChatAccountRegistry(Options.Create(options)).Resolve(null));
        Assert.Equal(ChatErrorCode.UnknownAccount, error.Code);
    }

    [Fact]
    public void Should_report_unreadable_settings_on_their_account_and_those_of_the_section_on_every_one()
    {
        var options = new ChatToolsOptions();
        options.Accounts["a"] = TestChatAccounts.Teams();
        options.Accounts["b"] = TestChatAccounts.Teams();
        options.AccountProblems["a"] = ["Rights is not a list"];
        var registry = new ChatAccountRegistry(Options.Create(options));
        Assert.Contains("Rights is not a list", registry.Inspect("a").Problems);
        Assert.Null(registry.Inspect("a").Account);
        Assert.NotNull(registry.Inspect("b").Account);

        options.SectionProblems.Add("the section");
        Assert.Contains("the section", new ChatAccountRegistry(Options.Create(options)).Inspect("b").Problems);
    }

    [Fact]
    public void Should_name_the_missing_rights_and_where_to_add_them()
    {
        using var fixture = new ChatFixture(TestChatAccounts.Teams(ChatRights.Edit));
        using var sessions = new Orkeon.Tools.Chat.Providers.ChatSessions(_ => fixture.Session.Provider);
        var access = new ChatAccess(fixture.Registry, sessions);

        var error = Assert.Throws<ChatToolException>(() => access.Authorize(null, ChatRights.Read, ChatRights.Send));

        Assert.Equal(ChatErrorCode.RightMissing, error.Code);
        Assert.Contains("the Read or Send right (it grants: Edit)", error.Message, StringComparison.Ordinal);
        Assert.Contains("Orkeon:Tools:Chat:Accounts:work:Rights", error.Message, StringComparison.Ordinal);
    }

    internal static ChatAccountRegistry Registry(params (string Name, ChatAccountOptions Options)[] accounts)
    {
        var options = new ChatToolsOptions();
        foreach (var (name, declared) in accounts)
            options.Accounts[name] = declared;
        return new ChatAccountRegistry(Options.Create(options));
    }
}
