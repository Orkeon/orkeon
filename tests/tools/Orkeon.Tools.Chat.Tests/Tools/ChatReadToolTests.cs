using System.Globalization;
using System.Text.Json.Nodes;
using Orkeon.Tools.Chat.Configuration;
using Orkeon.Tools.Chat.Security;
using Orkeon.Tools.Chat.Tests.Fixtures;

namespace Orkeon.Tools.Chat.Tests.Tools;

/// <summary>chat_read: what is read, by page and by date, and what is withheld.</summary>
public sealed class ChatReadToolTests
{
    private const string GroupPlace = "chat:" + Samples.Group;
    private const string GroupMessages = "chats/" + Samples.Group + "/messages";
    private static readonly string Escaped = Hostile.Text.Replace("<", "&lt;", StringComparison.Ordinal);
    private const string NextPage = "https://graph.microsoft.com/v1.0/" + GroupMessages + "?$skiptoken=two";

    [Fact]
    public async Task Should_read_a_channel_newest_first_with_its_authors()
    {
        using var fixture = Reading();
        fixture.Graph.Get(Samples.BuildsMessages, Samples.List(Samples.Message("2", "second"), Samples.Message("1", "first")));

        var result = await fixture.ResultAsync("chat_read", ("place", "team:Platform/channel:builds"));

        Assert.Equal(("work", Samples.BuildsPlace, ChatContentScreen.UntrustedNotice), (result["account"], result["place"], result["notice"]));
        Assert.Equal(["2", "1"], Ids(result));
        Assert.Null(result.GetValueOrDefault("next_cursor"));
        var message = ToolResults.Objects(result, "messages")[0];
        Assert.Equal(("second", "2026-09-30T10:00:00Z"), (message["text"], message["time"]));
        var author = ToolResults.Object(message, "author");
        Assert.Equal((Samples.Ada, "Ada", "ada@example.org"), (author["id"], author["name"], author["address"]));
        Assert.Single(fixture.Graph.To(HttpMethod.Get, "users/" + Samples.Ada));
    }

    [Fact]
    public async Task Should_read_a_thread_of_a_channel()
    {
        using var fixture = Reading();
        fixture.Graph.Get(Samples.BuildsMessages + "/1/replies", Samples.List(Samples.Message("7", "reply")));

        var result = await fixture.ResultAsync("chat_read", ("place", Samples.BuildsPlace), ("message_id", "1"));

        Assert.Equal("1", result["thread"]);
        Assert.Equal(["7"], Ids(result));
    }

    [Fact]
    public async Task Should_need_the_administrators_grant_in_a_channel_before_any_request()
    {
        using var fixture = new ChatFixture(TestChatAccounts.Teams(read: ["*"]));

        var error = await fixture.ErrorAsync("chat_read", ("place", "team:Platform/channel:builds"));

        Assert.Contains("ChannelMessage.Read.All", error, StringComparison.Ordinal);
        Assert.Empty(fixture.Handler.Requests);
    }

    [Theory]
    [InlineData("place", " ", "`place` is required: pass a place from chat_list")]
    [InlineData("message_id", "../x", "`message_id` must be the id of a message from chat_read")]
    [InlineData("message_id", "17", "`message_id` reads the replies of a channel's message")]
    [InlineData("text_chars", 0, "`text_chars` must be positive")]
    [InlineData("since", "soon", "`since` must be a date such as")]
    [InlineData("until", "32/13", "`until` must be a date such as")]
    [InlineData("cursor", "forged", "`cursor` is not one this call returned")]
    public async Task Should_refuse_a_wrong_argument(string key, object value, string message)
    {
        using var fixture = Reading();

        var error = key == "place"
            ? await fixture.ErrorAsync("chat_read", (key, value))
            : await fixture.ErrorAsync("chat_read", ("place", GroupPlace), (key, value));

        Assert.Contains(message, error, StringComparison.Ordinal);
        Assert.Empty(fixture.Graph.To(HttpMethod.Get, GroupMessages));
    }

    [Fact]
    public async Task Should_refuse_a_place_that_is_not_allowed()
    {
        using var fixture = new ChatFixture(TestChatAccounts.Teams(read: ["chat:" + Samples.Direct]));

        Assert.Contains(":Read:AllowedPlaces", await fixture.ErrorAsync("chat_read", ("place", GroupPlace)), StringComparison.Ordinal);
        Assert.Empty(fixture.Graph.To(HttpMethod.Get, GroupMessages));
    }

    [Fact]
    public async Task Should_page_through_a_chat_without_repeating_or_losing_a_message()
    {
        using var fixture = Reading();
        fixture.Graph.On(HttpMethod.Get, GroupMessages, request => FakeHttpResponse.Json(
            request.Uri.Query.Contains("skiptoken", StringComparison.Ordinal)
                ? Samples.List(Samples.Message("2", "b"), Samples.Message("1", "a"))
                : Samples.Page(NextPage, Samples.Message("4", "d"), Samples.Message("3", "c"))));

        var read = new List<string>();
        string? cursor = null;
        do
        {
            var result = await fixture.ResultAsync("chat_read", ("place", GroupPlace), ("limit", 1), ("cursor", cursor));
            read.AddRange(Ids(result));
            cursor = (string?)result.GetValueOrDefault("next_cursor");
        }
        while (cursor is not null && read.Count < 10);

        Assert.Equal(["4", "3", "2", "1"], read);
    }

    [Fact]
    public async Task Should_refuse_the_cursor_of_another_place_or_thread()
    {
        using var fixture = Reading();
        fixture.Graph.Get(GroupMessages, Samples.Page(NextPage, Samples.Message("4", "d"), Samples.Message("3", "c")));
        var cursor = (await fixture.ResultAsync("chat_read", ("place", GroupPlace), ("limit", 1)))["next_cursor"];

        Assert.Contains("`cursor` is not one", await fixture.ErrorAsync("chat_read", ("place", "chat:" + Samples.Direct), ("cursor", cursor)), StringComparison.Ordinal);
        Assert.Contains("`cursor` is not one", await fixture.ErrorAsync("chat_read", ("place", Samples.BuildsPlace), ("message_id", "1"), ("cursor", cursor)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_keep_the_messages_inside_the_dates_of_a_chat()
    {
        using var fixture = Reading();
        fixture.Graph.Get(GroupMessages, Samples.Page(NextPage,
            Samples.Message("4", "d", "2026-09-30T10:00:00Z"), Samples.Message("3", "c", "2026-09-29T00:00:00Z"), Samples.Message("2", "b", "2026-09-28T23:59:59Z")));

        var result = await fixture.ResultAsync("chat_read", ("place", GroupPlace), ("since", "2026-09-29"), ("until", "2026-09-30T10:00:00Z"));

        Assert.Equal(["3"], Ids(result));
        Assert.Null(result.GetValueOrDefault("next_cursor"));
        Assert.Contains("createdDateTime lt 2026-09-30T10:00:00.000Z", Uri.UnescapeDataString(Assert.Single(fixture.Graph.To(HttpMethod.Get, GroupMessages)).Uri.Query), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_keep_the_messages_inside_the_dates_of_a_channel_whatever_their_order()
    {
        // A channel is ordered by last activity: an old thread answered today comes first.
        using var fixture = Reading();
        fixture.Graph.Get(Samples.BuildsMessages, Samples.List(
            Samples.Message("1", "old", "2026-09-01T00:00:00Z"), Samples.Message("2", "in", "2026-09-29T00:00:00Z"), Samples.Message("3", "new", "2026-09-30T10:00:00Z")));

        Assert.Equal(["2"], Ids(await fixture.ResultAsync("chat_read", ("place", Samples.BuildsPlace), ("since", "2026-09-28"), ("until", "2026-09-30"))));
    }

    [Fact]
    public async Task Should_go_through_a_bounded_number_of_pages_and_give_a_cursor()
    {
        using var fixture = Reading();
        fixture.Graph.Get(GroupMessages, Samples.Page(NextPage));

        var result = await fixture.ResultAsync("chat_read", ("place", GroupPlace));

        Assert.Empty(Ids(result));
        Assert.NotNull(result["next_cursor"]);
        Assert.Equal(5, fixture.Graph.To(HttpMethod.Get, GroupMessages).Count);
    }

    [Theory]
    [InlineData(null, 20)]
    [InlineData(500, 50)]
    [InlineData(0, 1)]
    public async Task Should_bound_the_page(int? limit, int top)
    {
        using var fixture = Reading();
        fixture.Graph.Get(GroupMessages, Samples.List());

        await fixture.ResultAsync("chat_read", ("place", GroupPlace), ("limit", limit));

        Assert.Contains(string.Create(CultureInfo.InvariantCulture, $"$top={top}&"), Uri.UnescapeDataString(fixture.Graph.To(HttpMethod.Get, GroupMessages)[0].Uri.Query), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_cut_a_long_text_and_say_so()
    {
        using var fixture = Reading();
        fixture.Graph.Get(GroupMessages, Samples.List(Samples.Message("1", new string('x', 1500))));

        var message = ToolResults.Objects(await fixture.ResultAsync("chat_read", ("place", GroupPlace)), "messages")[0];
        var shorter = ToolResults.Objects(await fixture.ResultAsync("chat_read", ("place", GroupPlace), ("text_chars", 10)), "messages")[0];

        Assert.Equal((1001, true), (((string)message["text"]!).Length, message["text_truncated"]));
        Assert.Equal("xxxxxxxxxx…", shorter["text"]);
    }

    [Theory]
    [InlineData("deleted", "it was deleted")]
    [InlineData("policy", "the service marks it as a policy violation")]
    [InlineData("screen", "the prompt-injection screen rejected it; an operator can lift Orkeon:Tools:Chat:Screening:WithholdRejected")]
    public async Task Should_withhold_a_message_but_its_id_author_and_time(string why, string reason)
    {
        using var fixture = Reading(withhold: true);
        fixture.Graph.Get(GroupMessages, Samples.List(Samples.Message("1", why == "screen" ? Hostile.Text.Replace("<", "&lt;", StringComparison.Ordinal) : "secret plan", edit: json =>
        {
            if (why == "deleted")
                json["deletedDateTime"] = "2026-09-30T11:00:00Z";
            if (why == "policy")
                json["policyViolation"] = new JsonObject { ["dlpAction"] = "blockAccess" };
            json["attachments"] = new JsonArray(new JsonObject { ["name"] = "plan.docx" });
        })));

        var message = ToolResults.Objects(await fixture.ResultAsync("chat_read", ("place", GroupPlace)), "messages")[0];

        Assert.Equal(("1", "2026-09-30T10:00:00Z", true, reason), (message["id"], message["time"], message["withheld"], message["reason"]));
        Assert.Equal(Samples.Ada, ToolResults.Object(message, "author")["id"]);
        Assert.Null(message.GetValueOrDefault("text"));
        Assert.Null(message.GetValueOrDefault("attachments"));
        Assert.Null(message.GetValueOrDefault("mentions"));
        Assert.Null(message.GetValueOrDefault("screening"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Should_screen_the_author_of_a_message_without_its_text(bool hostileName)
    {
        using var fixture = Reading();
        fixture.Graph.Get(GroupMessages, Samples.List(Samples.Message("1", Escaped, edit: json =>
        {
            json["deletedDateTime"] = "2026-09-30T11:00:00Z";
            if (hostileName)
                json["from"]!["user"]!["displayName"] = Hostile.Text;
        })));

        var message = ToolResults.Objects(await fixture.ResultAsync("chat_read", ("place", GroupPlace)), "messages")[0];

        Assert.Equal(true, message["withheld"]);
        Assert.Equal(hostileName ? "rejected" : null, message.GetValueOrDefault("screening"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Should_screen_the_reactions_and_the_address_too(bool reaction)
    {
        using var fixture = Reading();
        if (!reaction)
            fixture.Graph.Get("users/" + Samples.Sales, Samples.Person(Samples.Sales, "Ada", Hostile.Text));
        fixture.Graph.Get(GroupMessages, Samples.List(Samples.Message("1", "hello", edit: json =>
        {
            json["from"]!["user"]!["id"] = reaction ? Samples.Ada : Samples.Sales;
            if (reaction)
                json["reactions"] = new JsonArray(new JsonObject { ["reactionType"] = Hostile.Text });
        })));

        var message = ToolResults.Objects(await fixture.ResultAsync("chat_read", ("place", GroupPlace)), "messages")[0];

        Assert.Equal("rejected", message.GetValueOrDefault("screening"));
    }

    [Theory]
    [InlineData("text")]
    [InlineData("author")]
    [InlineData("attachment")]
    [InlineData("mention")]
    public async Task Should_flag_what_the_screen_rejects_wherever_it_is(string where)
    {
        using var fixture = Reading();
        fixture.Graph.Get(GroupMessages, Samples.List(Samples.Message("1", where == "text" ? Hostile.Text.Replace("<", "&lt;", StringComparison.Ordinal) : "hello", edit: json =>
        {
            if (where == "author")
                json["from"]!["user"]!["displayName"] = Hostile.Text;
            if (where == "attachment")
                json["attachments"] = new JsonArray(new JsonObject { ["name"] = Hostile.Text });
            if (where == "mention")
                json["mentions"] = new JsonArray(new JsonObject { ["mentionText"] = Hostile.Text });
        })));

        var message = ToolResults.Objects(await fixture.ResultAsync("chat_read", ("place", GroupPlace)), "messages")[0];

        Assert.Equal("rejected", message["screening"]);
        Assert.NotNull(message["text"]);
    }

    [Fact]
    public async Task Should_tell_hidden_text_a_guest_an_application_and_system_events()
    {
        using var fixture = Reading();
        fixture.Graph.Get(GroupMessages, Samples.List(
            Samples.Message("4", "x", edit: json => json["messageType"] = "systemEventMessage"),
            Samples.Message("3", "a<span style=\\\"display:none\\\">b</span>".Replace("\\", string.Empty, StringComparison.Ordinal)),
            Samples.Message("2", "x", edit: json => json["from"]!["user"]!["id"] = Samples.Guest),
            Samples.Message("1", "x", edit: json => json["from"] = new JsonObject { ["application"] = new JsonObject { ["id"] = "bot", ["displayName"] = "Bot" } })));

        var messages = ToolResults.Objects(await fixture.ResultAsync("chat_read", ("place", GroupPlace)), "messages");

        Assert.Equal(["3", "2", "1"], messages.Select(message => message["id"]));
        Assert.Equal(("a", true), (messages[0]["text"], messages[0]["hidden_text"]));
        Assert.Equal(true, ToolResults.Object(messages[1], "author")["guest"]);
        Assert.Equal(true, ToolResults.Object(messages[2], "author")["application"]);
    }

    [Fact]
    public async Task Should_read_the_chat_with_a_person_or_say_there_is_none()
    {
        using var fixture = Reading();
        fixture.Graph.Get("me/chats", Samples.List(Samples.GroupChat, Samples.DirectChat)).Get($"chats/{Samples.Direct}/messages", Samples.List(Samples.Message("1", "hi")));

        Assert.Equal("chat:" + Samples.Direct, (await fixture.ResultAsync("chat_read", ("place", "user:ada@example.org")))["place"]);
        Assert.Contains("chat_send opens one", await fixture.ErrorAsync("chat_read", ("place", "user:" + Samples.Guest)), StringComparison.Ordinal);
        Assert.Empty(fixture.Graph.Writes);
    }

    private static IEnumerable<string> Ids(Dictionary<string, object?> result) => ToolResults.Objects(result, "messages").Select(message => (string)message["id"]!);

    private static ChatFixture Reading(bool withhold = false)
    {
        var fixture = new ChatFixture(TestChatAccounts.Teams(ChatRights.Read, read: ["*"], granted: "ChannelMessages"), withhold);
        fixture.Graph.Directory();
        return fixture;
    }
}
