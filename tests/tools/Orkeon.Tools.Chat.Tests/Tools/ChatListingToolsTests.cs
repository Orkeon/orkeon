using Orkeon.Tools.Chat.Configuration;
using Orkeon.Tools.Chat.Security;
using Orkeon.Tools.Chat.Tests.Fixtures;

namespace Orkeon.Tools.Chat.Tests.Tools;

/// <summary>chat_accounts, chat_list and chat_members.</summary>
public sealed class ChatListingToolsTests
{
    [Fact]
    public async Task Should_list_the_accounts_and_what_keeps_one_from_being_ready()
    {
        using var fixture = new ChatFixture(TestChatAccounts.Teams());
        fixture.Options.Accounts["broken"] = new ChatAccountOptions();

        var accounts = ToolResults.Objects(await fixture.ResultAsync("chat_accounts"), "accounts");

        Assert.Equal(("broken", false), (accounts[0]["name"], accounts[0]["ready"]));
        Assert.Contains("Provider is required", (string)accounts[0]["problem"]!, StringComparison.Ordinal);
        Assert.Equal(("work", "Teams", "Read, Send", true), (accounts[1]["name"], accounts[1]["provider"], accounts[1]["rights"], accounts[1]["ready"]));
        Assert.Null(accounts[1].GetValueOrDefault("problem"));
        Assert.Empty(fixture.Handler.Requests);
    }

    [Fact]
    public async Task Should_list_the_allowed_teams_alone()
    {
        using var fixture = Listing(read: ["team:Platform"], send: ["team:Sales/channel:builds", "team:Gone"]);

        var result = await fixture.ResultAsync("chat_list");

        Assert.Equal(ChatContentScreen.UntrustedNotice, result["notice"]);
        Assert.StartsWith("'team:Gone' is set aside", Assert.Single(ToolResults.Strings(result, "set_aside")), StringComparison.Ordinal);
        var places = ToolResults.Objects(result, "places");
        Assert.Equal(["team:" + Samples.Platform, "team:" + Samples.Sales], places.Select(place => place["place"]));
        Assert.Equal((true, false), (places[0]["can_read"], places[0]["can_send"]));
        Assert.Equal((false, true), (places[1]["can_read"], places[1]["can_send"]));
    }

    [Fact]
    public async Task Should_list_nothing_with_empty_lists()
    {
        using var fixture = Listing([], []);

        Assert.Empty(ToolResults.Objects(await fixture.ResultAsync("chat_list"), "places"));
        Assert.Contains("is among the allowed places", await fixture.ErrorAsync("chat_list", ("team", "Platform")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_list_the_allowed_channels_of_a_team()
    {
        using var fixture = Listing([], ["team:Platform/channel:builds"]);

        var place = Assert.Single(ToolResults.Objects(await fixture.ResultAsync("chat_list", ("team", "platform")), "places"));

        Assert.Equal((Samples.BuildsPlace, "channel", "builds"), (place["place"], place["kind"], place["name"]));
    }

    [Fact]
    public async Task Should_list_the_allowed_chats_and_flag_a_topic_others_wrote()
    {
        using var fixture = Listing(["chat:" + Samples.Group, "user:ada@example.org"], []);
        fixture.Graph.Get("me/chats", Samples.List(
            Samples.Chat(Samples.Group, "group", Hostile.Text, (Samples.Self, "Me"), (Samples.Ada, "Ada")),
            Samples.DirectChat,
            Samples.Chat(Samples.GuestDirect, "oneOnOne", null, (Samples.Self, "Me"), (Samples.Guest, "Gus"))));

        var places = ToolResults.Objects(await fixture.ResultAsync("chat_list", ("kind", "Chats")), "places");

        Assert.Equal(["chat:" + Samples.Group, "chat:" + Samples.Direct], places.Select(place => place["place"]));
        Assert.Equal("rejected", places[0]["screening"]);
        Assert.Equal(["Ada"], ToolResults.Strings(places[1], "members"));
        Assert.Null(places[1].GetValueOrDefault("screening"));
    }

    [Fact]
    public async Task Should_page_places_and_refuse_the_cursor_of_another_list()
    {
        using var fixture = Listing(["*"], []);

        var first = await fixture.ResultAsync("chat_list", ("limit", 1));
        var second = await fixture.ResultAsync("chat_list", ("limit", 1), ("cursor", first["next_cursor"]));

        Assert.Equal("team:" + Samples.Platform, Assert.Single(ToolResults.Objects(first, "places"))["place"]);
        Assert.Equal("team:" + Samples.Sales, Assert.Single(ToolResults.Objects(second, "places"))["place"]);
        Assert.Null(second.GetValueOrDefault("next_cursor"));
        Assert.Contains("`cursor` is not one", await fixture.ErrorAsync("chat_list", ("team", "Platform"), ("cursor", first["next_cursor"])), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_page_chats_by_the_services_own_pages()
    {
        using var fixture = Listing(["*"], []);
        fixture.Graph.Get("me/chats", Samples.Page("https://graph.microsoft.com/v1.0/me/chats?$skiptoken=two", Samples.GroupChat)).Get("me/chats", Samples.List(Samples.DirectChat));

        var first = await fixture.ResultAsync("chat_list", ("kind", "chats"));
        var second = await fixture.ResultAsync("chat_list", ("kind", "chats"), ("cursor", first["next_cursor"]));

        Assert.Equal("chat:" + Samples.Direct, Assert.Single(ToolResults.Objects(second, "places"))["place"]);
        Assert.Contains("$skiptoken=two", Uri.UnescapeDataString(fixture.Graph.To(HttpMethod.Get, "me/chats")[1].Uri.Query), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("people", null, "`kind` must be teams, channels or chats")]
    [InlineData("channels", null, "`team` is required to list channels")]
    [InlineData("chats", "Platform", "`team` goes with `kind` channels alone")]
    public async Task Should_refuse_a_wrong_listing(string kind, string? team, string message)
    {
        using var fixture = Listing(["*"], []);

        Assert.Contains(message, await fixture.ErrorAsync("chat_list", ("kind", kind), ("team", team)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_not_list_chats_without_the_right_to_read()
    {
        using var fixture = new ChatFixture(TestChatAccounts.Teams(ChatRights.Send, send: ["*"]));
        fixture.Graph.Directory();

        Assert.Contains("Read", await fixture.ErrorAsync("chat_list", ("kind", "chats")), StringComparison.Ordinal);
        Assert.Equal(2, ToolResults.Objects(await fixture.ResultAsync("chat_list"), "places").Count);
        Assert.Empty(fixture.Graph.To(HttpMethod.Get, "me/chats"));
    }

    [Fact]
    public async Task Should_list_the_members_of_a_place_by_page()
    {
        using var fixture = Listing(["chat:" + Samples.Group], []);
        fixture.Graph.Get($"chats/{Samples.Group}/members", Samples.List(
            Samples.Member(Samples.Ada, "Ada", "ada@example.org", "owner"), Samples.Member(Samples.Guest, Hostile.Text, "gus@partner.test", "guest")));

        var first = await fixture.ResultAsync("chat_members", ("place", "chat:" + Samples.Group), ("limit", 1));
        var second = await fixture.ResultAsync("chat_members", ("place", "chat:" + Samples.Group), ("limit", 1), ("cursor", first["next_cursor"]));

        var ada = Assert.Single(ToolResults.Objects(first, "members"));
        Assert.Equal((Samples.Ada, "Ada", "ada@example.org", true), (ada["id"], ada["name"], ada["address"], ada["owner"]));
        var guest = Assert.Single(ToolResults.Objects(second, "members"));
        Assert.Equal((true, "rejected"), (guest["guest"], guest["screening"]));
        Assert.Null(second.GetValueOrDefault("next_cursor"));
        Assert.Contains(":Read:AllowedPlaces", await fixture.ErrorAsync("chat_members", ("place", "chat:" + Samples.Direct)), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Samples.Ada)]
    [InlineData("ada@example.org")]
    [InlineData("Ada")]
    public async Task Should_find_a_person_by_id_address_or_name(string person)
    {
        using var fixture = new ChatFixture(TestChatAccounts.Teams(ChatRights.Send));
        fixture.Graph.Directory().Get("users", Samples.List(Samples.AdaPerson));

        var member = Assert.Single(ToolResults.Objects(await fixture.ResultAsync("chat_members", ("person", person)), "members"));

        Assert.Equal((Samples.Ada, "ada@example.org"), (member["id"], member["address"]));
    }

    [Fact]
    public async Task Should_find_no_one_rather_than_fail()
    {
        using var fixture = Listing(["*"], []);
        fixture.Graph.Get("users", Samples.List());

        Assert.Empty(ToolResults.Objects(await fixture.ResultAsync("chat_members", ("person", "../me/messages")), "members"));
        Assert.Contains("users?", Uri.UnescapeDataString(fixture.Handler.Requests[^1].Uri.PathAndQuery), StringComparison.Ordinal);
        Assert.Contains("pass either `place` or `person`", await fixture.ErrorAsync("chat_members"), StringComparison.Ordinal);
        Assert.Contains("pass either `place` or `person`", await fixture.ErrorAsync("chat_members", ("person", "a"), ("place", "b")), StringComparison.Ordinal);
    }

    private static ChatFixture Listing(string[] read, string[] send)
    {
        var fixture = new ChatFixture(TestChatAccounts.Teams(read: read, send: send));
        fixture.Graph.Directory();
        return fixture;
    }
}
