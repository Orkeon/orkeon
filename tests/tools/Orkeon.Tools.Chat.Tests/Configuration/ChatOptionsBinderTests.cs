using Microsoft.Extensions.Configuration;
using Orkeon.Tools.Chat.Configuration;

namespace Orkeon.Tools.Chat.Tests.Configuration;

/// <summary>The section is bound without ever throwing, and what cannot be read is said on its account.</summary>
public sealed class ChatOptionsBinderTests
{
    private const string Account = "Accounts:work:";

    [Fact]
    public void Should_bind_every_setting()
    {
        var options = Bind(
            ("DefaultAccount", "work"), ("CredentialsDirectory", "/srv/tokens"), ("Screening:WithholdRejected", " True "),
            (Account + "Provider", "teams"), (Account + "Rights", "Read, Send"), (Account + "Cloud", "UsGov"),
            (Account + "Auth:Method", "OAuth2"), (Account + "Auth:ClientId", "id"), (Account + "Auth:Tenant", "example.org"),
            (Account + "Auth:AdminGranted:0", "ChannelMessages"), (Account + "Read:AllowedPlaces:0", "*"),
            (Account + "Send:AllowedPlaces:0", "team:Platform"), (Account + "Send:MaxPerHour", "12"));

        Assert.Empty(options.SectionProblems);
        Assert.Empty(options.AccountProblems);
        Assert.Equal(("work", "/srv/tokens", true), (options.DefaultAccount, options.CredentialsDirectory, options.Screening.WithholdRejected));
        var account = options.Accounts["WORK"];
        Assert.Equal((ChatProvider.Teams, ChatRights.Read | ChatRights.Send, ChatCloud.UsGov), (account.Provider, account.Rights, account.Cloud));
        Assert.Equal((ChatAuthMethod.OAuth2, "id", "example.org"), (account.Auth.Method, account.Auth.ClientId, account.Auth.Tenant));
        Assert.Equal(["ChannelMessages"], account.Auth.AdminGranted);
        Assert.Equal(["*"], account.Read.AllowedPlaces);
        Assert.Equal(["team:Platform"], account.Send.AllowedPlaces);
        Assert.Equal(12, account.Send.MaxPerHour);
    }

    [Theory]
    [InlineData("Screning:WithholdRejected", "true", "Orkeon:Tools:Chat:Screning is not a chat setting: Orkeon:Tools:Chat carries DefaultAccount, CredentialsDirectory, Accounts, Screening")]
    [InlineData("Screening:WithholdRejects", "true", "Orkeon:Tools:Chat:Screening:WithholdRejects is not a chat setting: Screening carries WithholdRejected")]
    [InlineData("Screening", "true", "Orkeon:Tools:Chat:Screening is a single value where a section is expected")]
    public void Should_report_a_key_the_section_does_not_carry(string key, string value, string problem)
    {
        var options = Bind((key, value), (Account + "Provider", "Teams"));

        Assert.Equal([problem], options.SectionProblems);
        Assert.Empty(options.AccountProblems);
    }

    [Theory]
    [InlineData("Rights", "Read, Publish", "Rights is not a list of None, Read, Send, Edit, Delete, React, Meetings, separated by commas")]
    [InlineData("Rights", "3", "Rights holds a number where ChatRights takes names: write one or more of None, Read, Send, Edit, Delete, React, Meetings, separated by commas")]
    [InlineData("Provider", "Slack", "Provider is not one of Teams, TeamsWebhook")]
    [InlineData("Provider", "0", "Provider is a number where a name is expected: write one of Teams, TeamsWebhook")]
    [InlineData("Cloud", "Global, UsGov", "Cloud is a list where a single name is expected: write one of Global, UsGov, UsGovDoD, China")]
    [InlineData("Auth:Method", "Basic", "Auth:Method is not one of OAuth2, ClientCredentials")]
    [InlineData("Send:MaxPerHour", "many", "Send:MaxPerHour is not a whole number")]
    [InlineData("Send:AllowedPlace", "*", "Send:AllowedPlace is not an account setting: Send carries AllowedPlaces, MaxPerHour")]
    [InlineData("Auth:ClientSecret", "x", "Auth:ClientSecret is not an account setting: Auth carries Method, ClientId, Tenant, AdminGranted")]
    [InlineData("Send:MaxPerHour", "", "Send:MaxPerHour is not a whole number")]
    [InlineData("Send:AllowedPlaces", "chat:1", "Send:AllowedPlaces is a single value where a list is expected")]
    [InlineData("Read:AllowedPlaces", "*", "Read:AllowedPlaces is a single value where a list is expected")]
    [InlineData("Auth:AdminGranted", "ChannelMessages", "Auth:AdminGranted is a single value where a list is expected")]
    [InlineData("Send", "*", "Send is a single value where a section is expected")]
    [InlineData("Places", "x", "Places is not an account setting: an account carries Provider, Rights, Auth, Webhook, Cloud, Read, Send")]
    public void Should_set_an_unreadable_account_aside_and_bind_the_others(string key, string value, string problem)
    {
        var options = Bind((Account + key, value), ("Accounts:other:Provider", "Teams"));

        Assert.Equal([problem], options.AccountProblems["work"]);
        Assert.Equal(ChatProvider.Teams, options.Accounts["other"].Provider);
        Assert.Single(options.AccountProblems);
    }

    [Fact]
    public void Should_withhold_When_the_screening_setting_cannot_be_read()
    {
        var options = Bind(("Screening:WithholdRejected", "maybe"));

        Assert.True(options.Screening.WithholdRejected);
        Assert.Equal(["Orkeon:Tools:Chat:Screening:WithholdRejected is neither true nor false"], options.SectionProblems);
    }

    [Fact]
    public void Should_not_withhold_by_default() => Assert.False(Bind().Screening.WithholdRejected);

    [Fact]
    public void Should_tell_a_host_when_a_token_store_is_needed_and_where()
    {
        Assert.Equal("chat", ChatCredentialsLocation.TokenSubdirectory);
        Assert.False(ChatCredentialsLocation.NeedsTokenStore(Section()));
        Assert.True(ChatCredentialsLocation.NeedsTokenStore(Section((Account + "Rights", "unreadable"))));
        Assert.Null(ChatCredentialsLocation.ConfiguredDirectory(Section(("CredentialsDirectory", "  "))));
        Assert.Equal("/srv/tokens", ChatCredentialsLocation.ConfiguredDirectory(Section(("CredentialsDirectory", " /srv/tokens "))));
        Assert.Throws<ArgumentNullException>(() => ChatCredentialsLocation.NeedsTokenStore(null!));
        Assert.Throws<ArgumentNullException>(() => ChatCredentialsLocation.ConfiguredDirectory(null!));
    }

    private static ChatToolsOptions Bind(params (string Key, string Value)[] settings)
    {
        var options = new ChatToolsOptions();
        ChatOptionsBinder.Bind(Section(settings), options);
        return options;
    }

    private static IConfiguration Section(params (string Key, string Value)[] settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings.ToDictionary(pair => pair.Key, pair => (string?)pair.Value)).Build();
}
