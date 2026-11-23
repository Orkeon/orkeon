using System.Net;
using Orkeon.Tools.Chat.Configuration;
using Orkeon.Tools.Chat.Places;
using Orkeon.Tools.Chat.Tests.Fixtures;

namespace Orkeon.Tools.Chat.Tests.Places;

/// <summary>Places are compared by their ids, and what is not allowed is refused before any write.</summary>
public sealed class PlaceGateTests
{
    [Fact]
    public async Task Should_compare_ids_of_a_channel_named_twice_over()
    {
        using var fixture = Sending("team:platform/channel:BUILDS");

        var target = await fixture.AdmitAsync("team:Platform/channel:builds");
        Assert.Equal((Samples.Platform, Samples.Builds), (target.TeamId, target.ChannelId));
        Assert.Equal(Samples.BuildsPlace, (await fixture.AdmitAsync(Samples.BuildsPlace)).Place);
        // The same channel name, in another team.
        Assert.Equal(ChatErrorCode.PlaceNotAllowed, (await fixture.RefusedAsync("team:Sales/channel:builds")).Code);
    }

    [Fact]
    public async Task Should_not_let_a_renamed_place_take_an_allowed_name()
    {
        using var fixture = Sending("team:Platform/channel:builds");
        await fixture.AdmitAsync(Samples.BuildsPlace);
        fixture.Graph.Get($"teams/{Samples.Platform}/channels", Samples.List(Samples.Channel(Samples.General, "builds"), Samples.Channel(Samples.Builds, "General")));
        await fixture.Session.Provider.ListChannelsAsync(Samples.Platform, TestContext.Current.CancellationToken);

        Assert.Equal(ChatErrorCode.PlaceNotAllowed, (await fixture.RefusedAsync("team:Platform/channel:builds")).Code);
        Assert.Equal(Samples.BuildsPlace, (await fixture.AdmitAsync("team:Platform/channel:General")).Place);
    }

    [Fact]
    public async Task Should_ignore_the_case_of_a_directory_id()
    {
        const string id = "abcdefab-0000-0000-0000-00000000abcd";
        using var fixture = Sending($"team:{id.ToUpperInvariant()}/channel:{Samples.Builds}");

        Assert.Equal(id, (await fixture.AdmitAsync($"team:{id}/channel:{Samples.Builds}")).TeamId);
    }

    [Fact]
    public async Task Should_refuse_an_ambiguous_name()
    {
        using var fixture = Sending("*");
        fixture.Graph.Get("me/joinedTeams", Samples.List(Samples.Named(Samples.Platform, "Twin"), Samples.Named(Samples.Sales, "Twin")));
        await fixture.Session.Provider.ListTeamsAsync(TestContext.Current.CancellationToken);

        var error = await fixture.RefusedAsync("team:twin/channel:builds");

        Assert.Equal(ChatErrorCode.NotFound, error.Code);
        Assert.Equal("2 matches for team 'twin': use the id chat_list returns.", error.Message);
    }

    [Fact]
    public async Task Should_set_aside_the_entries_that_resolve_to_nothing_and_say_so()
    {
        using var fixture = Sending("team:Gone", "user:nobody@example.org", "team:Platform");
        fixture.Graph.Get("users", Samples.List());

        Assert.Equal(Samples.BuildsPlace, (await fixture.AdmitAsync("team:Platform/channel:builds")).Place);
        var error = await fixture.RefusedAsync("team:Sales/channel:builds");

        Assert.Equal(ChatErrorCode.PlaceNotAllowed, error.Code);
        Assert.Contains("is not a place chat account 'work' may post in. An operator adds it under Orkeon:Tools:Chat:Accounts:work:Send:AllowedPlaces.", error.Message, StringComparison.Ordinal);
        Assert.Contains("'team:Gone' is set aside: No match for team 'Gone'", error.Message, StringComparison.Ordinal);
        Assert.Contains("'user:nobody@example.org' is set aside", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_not_keep_the_failure_to_reach_the_service()
    {
        using var fixture = new ChatFixture(TestChatAccounts.Teams(send: ["team:Platform"]));
        fixture.Graph
            .On(HttpMethod.Get, "me/joinedTeams", _ => GraphStub.Error(HttpStatusCode.BadGateway, "bad"))
            .Get("me/joinedTeams", Samples.List(Samples.Named(Samples.Platform, "Platform")))
            .Get($"teams/{Samples.Platform}/channels", Samples.List(Samples.Channel(Samples.Builds, "builds")));

        Assert.Equal(ChatErrorCode.ServerError, (await fixture.RefusedAsync(Samples.BuildsPlace)).Code);
        Assert.Equal(Samples.BuildsPlace, (await fixture.AdmitAsync(Samples.BuildsPlace)).Place);
    }

    [Theory]
    [InlineData(Samples.BuildsPlace)]
    [InlineData("chat:" + Samples.Group)]
    [InlineData("chat:" + Samples.Direct)]
    [InlineData("user:ada@example.org")]
    public async Task Should_allow_nothing_with_an_empty_list(string place)
    {
        using var fixture = Sending();

        Assert.Equal(ChatErrorCode.PlaceNotAllowed, (await fixture.RefusedAsync(place)).Code);
        Assert.Equal(ChatErrorCode.PlaceNotAllowed, (await fixture.RefusedAsync(place, PlaceUse.Read)).Code);
    }

    [Fact]
    public async Task Should_keep_reading_and_sending_apart()
    {
        using var fixture = new ChatFixture(TestChatAccounts.Teams(read: ["chat:" + Samples.Group], send: ["chat:" + Samples.Direct]));

        Assert.Equal(Samples.Group, (await fixture.AdmitAsync("chat:" + Samples.Group, PlaceUse.Read)).ChatId);
        Assert.Equal(Samples.Direct, (await fixture.AdmitAsync("chat:" + Samples.Direct)).ChatId);
        Assert.Equal(ChatErrorCode.PlaceNotAllowed, (await fixture.RefusedAsync("chat:" + Samples.Group)).Code);
        Assert.Equal(ChatErrorCode.PlaceNotAllowed, (await fixture.RefusedAsync("chat:" + Samples.Direct, PlaceUse.Read)).Code);
    }

    [Theory]
    [InlineData(Samples.BuildsPlace)]
    [InlineData("chat:" + Samples.Group)]
    [InlineData("user:ada@example.org")]
    public async Task Should_allow_everything_with_a_star(string place)
    {
        using var fixture = Sending("*");

        Assert.NotNull(await fixture.AdmitAsync(place));
    }

    [Theory]
    [InlineData("*")]
    [InlineData("team:Platform")]
    [InlineData("user:*@example.org")]
    public async Task Should_refuse_a_call_that_names_no_single_place(string place)
    {
        using var fixture = Sending("*");

        Assert.Equal(ChatErrorCode.InvalidRequest, (await fixture.RefusedAsync(place)).Code);
    }

    [Fact]
    public async Task Should_refuse_a_channel_id_an_allowed_team_does_not_list()
    {
        using var fixture = Sending("team:Platform");

        var error = await fixture.RefusedAsync($"team:{Samples.Platform}/channel:{Samples.Deals}");

        Assert.Equal(ChatErrorCode.PlaceNotAllowed, error.Code);
        Assert.Equal(Samples.BuildsPlace, (await fixture.AdmitAsync(Samples.BuildsPlace)).Place);
        Assert.Empty(fixture.Graph.Writes);
    }

    [Theory]
    [InlineData("user:ada@example.org")]
    [InlineData("user:*@EXAMPLE.org")]
    [InlineData("user:" + Samples.Ada)]
    public async Task Should_allow_a_one_to_one_chat_by_the_person_it_is_with(string entry)
    {
        using var fixture = Sending(entry);

        Assert.Equal(Samples.Direct, (await fixture.AdmitAsync("chat:" + Samples.Direct)).ChatId);
        Assert.Equal(Samples.Ada, (await fixture.AdmitAsync("user:ada@example.org")).Person?.Id);
    }

    [Fact]
    public async Task Should_not_allow_a_group_chat_for_the_people_in_it()
    {
        using var fixture = Sending("user:ada@example.org", "user:*@example.org");

        Assert.Equal(ChatErrorCode.PlaceNotAllowed, (await fixture.RefusedAsync("chat:" + Samples.Group)).Code);
    }

    [Theory]
    [InlineData("group", false)]
    [InlineData("oneOnOne", true)]
    public async Task Should_not_believe_the_id_of_a_chat_that_is_not_one_to_one(string kind, bool third)
    {
        using var fixture = new ChatFixture(TestChatAccounts.Teams(send: ["user:" + Samples.Ada]));
        var members = new List<(string Id, string Name)> { (Samples.Self, "Me"), (Samples.Ada, "Ada") };
        if (third)
            members.Add((Samples.Guest, "Gus"));
        fixture.Graph.Get($"users/{Samples.Ada}", Samples.AdaPerson)
            .Get($"chats/{Samples.Direct}", Samples.Chat(Samples.Direct, kind, null, [.. members]));

        Assert.Equal(ChatErrorCode.PlaceNotAllowed, (await fixture.RefusedAsync("chat:" + Samples.Direct)).Code);
    }

    [Fact]
    public async Task Should_not_tell_who_a_chat_is_with_without_the_right_to_read()
    {
        using var fixture = new ChatFixture(TestChatAccounts.Teams(ChatRights.Send, send: ["user:ada@example.org"]));
        fixture.Graph.Directory();

        var error = await fixture.RefusedAsync("chat:" + Samples.Direct);

        Assert.Equal(ChatErrorCode.PlaceNotAllowed, error.Code);
        Assert.Contains("write user:<address> instead", error.Message, StringComparison.Ordinal);
        Assert.Empty(fixture.Graph.To(HttpMethod.Get, $"chats/{Samples.Direct}"));
    }

    [Fact]
    public async Task Should_refuse_the_account_itself()
    {
        using var fixture = Sending("*");
        fixture.Graph.Get("users/me@example.org", Samples.Person(Samples.Self, "Me", "me@example.org"));

        Assert.Equal(ChatErrorCode.NotSupportedByAccount, (await fixture.RefusedAsync("user:me@example.org")).Code);
    }

    [Theory]
    [InlineData("partner.test", true)]
    [InlineData("example.org", false)]
    [InlineData("evilpartner.test", false)]
    [InlineData("test.partner.test", false)]
    public async Task Should_take_the_home_address_of_a_guest(string domain, bool allowed)
    {
        using var fixture = Sending($"user:*@{domain}");

        if (allowed)
        {
            Assert.Equal(Samples.Guest, (await fixture.AdmitAsync("user:" + Samples.Guest)).Person?.Id);
            Assert.Equal(Samples.GuestDirect, (await fixture.AdmitAsync("chat:" + Samples.GuestDirect)).ChatId);
        }
        else
        {
            Assert.Equal(ChatErrorCode.PlaceNotAllowed, (await fixture.RefusedAsync("user:" + Samples.Guest)).Code);
            Assert.Equal(ChatErrorCode.PlaceNotAllowed, (await fixture.RefusedAsync("chat:" + Samples.GuestDirect)).Code);
        }
    }

    [Theory]
    // A guest whose tenant gave it a mail of the home domain.
    [InlineData("gus_evil.test#EXT#@example.org", "gus@example.org")]
    // A name that holds the allowed domain before its own.
    [InlineData("gus_example.org_evil.test#EXT#@example.org", null)]
    [InlineData("gus@evil-example.org", null)]
    [InlineData("gus@example.org.evil.test", "gus@example.org.evil.test")]
    [InlineData("#EXT#@example.org", null)]
    [InlineData("gus#ext#@example.org", "gus@example.org")]
    [InlineData(null, "gus@example.org")]
    public async Task Should_not_take_a_lookalike_for_someone_of_the_domain(string? upn, string? mail)
    {
        using var fixture = new ChatFixture(TestChatAccounts.Teams(send: ["user:*@example.org"]));
        fixture.Graph.Get($"users/{Samples.Guest}", Samples.Person(Samples.Guest, "Gus", upn, mail));

        Assert.Equal(ChatErrorCode.PlaceNotAllowed, (await fixture.RefusedAsync("user:" + Samples.Guest)).Code);
    }

    [Fact]
    public async Task Should_not_take_someone_by_a_mail_that_is_not_theirs()
    {
        using var fixture = new ChatFixture(TestChatAccounts.Teams(send: ["user:ada@example.org"]));
        fixture.Graph.Get("users", Samples.List(Samples.GuestPerson("ada_evil.test#EXT#@example.org", "ada@example.org")))
            .Get($"users/{Samples.Guest}", Samples.GuestPerson("ada_evil.test#EXT#@example.org", "ada@example.org"));

        Assert.Equal(ChatErrorCode.PlaceNotAllowed, (await fixture.RefusedAsync("user:" + Samples.Guest)).Code);
    }

    [Fact]
    public async Task Should_not_believe_the_address_a_chat_lists_for_someone_of_another_tenant()
    {
        const string stranger = "66666666-6666-6666-6666-666666666666";
        const string chat = "19:" + Samples.Self + "_" + stranger + "@unq.gbl.spaces";
        using var fixture = new ChatFixture(TestChatAccounts.Teams(send: ["user:*@example.org"]));
        fixture.Graph.Get($"chats/{chat}", $$"""
            {"id":"{{chat}}","chatType":"oneOnOne","members":[{{Samples.Member(Samples.Self, "Me")}},{{Samples.Member(stranger, "Eve", "eve@example.org")}}]}
            """);

        Assert.Equal(ChatErrorCode.PlaceNotAllowed, (await fixture.RefusedAsync("chat:" + chat)).Code);
    }

    private static ChatFixture Sending(params string[] places)
    {
        var fixture = new ChatFixture(TestChatAccounts.Teams(read: [], send: places));
        fixture.Graph.Directory();
        return fixture;
    }
}
