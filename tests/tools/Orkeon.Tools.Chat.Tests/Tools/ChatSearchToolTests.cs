using System.Net;
using System.Text.Json.Nodes;
using Orkeon.Tools.Chat.Configuration;
using Orkeon.Tools.Chat.Security;
using Orkeon.Tools.Chat.Tests.Fixtures;

namespace Orkeon.Tools.Chat.Tests.Tools;

/// <summary>chat_search: the place of every hit is checked, and its text is read from that place.</summary>
public sealed class ChatSearchToolTests
{
    private const string GroupPlace = "chat:" + Samples.Group;
    private const string GroupMessages = "chats/" + Samples.Group + "/messages";
    private const string Summary = "SUMMARY-OF-GRAPH";

    [Fact]
    public async Task Should_find_messages_and_read_them_from_their_place()
    {
        using var fixture = Searching(GroupPlace);
        Finds(fixture, ChatHit("1", Samples.Group));
        fixture.Graph.Get(GroupMessages + "/1", Samples.Message("1", "the build is red"));

        var result = await fixture.ResultAsync("chat_search", ("text", "red build"));

        Assert.Equal(ChatContentScreen.UntrustedNotice, result["notice"]);
        var hit = Assert.Single(ToolResults.Objects(result, "hits"));
        Assert.Equal(GroupPlace, hit["place"]);
        Assert.Equal("the build is red", ToolResults.Object(hit, "message")["text"]);
        var request = JsonNode.Parse(Assert.Single(Searches(fixture)).Body!)!["requests"]![0]!;
        Assert.Equal("\"red\" \"build\"", (string)request["query"]!["queryString"]!);
        Assert.Equal("chatMessage", (string)Assert.Single(request["entityTypes"]!.AsArray())!);
    }

    [Fact]
    public async Task Should_show_nothing_of_a_hit_outside_the_allowed_places()
    {
        using var fixture = Searching("chat:" + Samples.Direct);
        Finds(fixture, ChatHit("1", Samples.Group));
        fixture.Graph.Get(GroupMessages + "/1", Samples.Message("1", "secret"));

        var call = await fixture.ResultAsync("chat_search", ("text", "secret"));

        Assert.Empty(ToolResults.Objects(call, "hits"));
        Assert.Null(call.GetValueOrDefault("next_cursor"));
        Assert.Empty(fixture.Graph.To(HttpMethod.Get, GroupMessages + "/1"));
    }

    [Fact]
    public async Task Should_answer_the_same_with_hidden_hits_as_with_none()
    {
        using var none = Searching("chat:" + Samples.Direct);
        Finds(none);
        using var hidden = Searching("chat:" + Samples.Direct);
        Finds(hidden, ChatHit("1", Samples.Group), ChatHit("2", Samples.Group));

        Assert.Equal(
            System.Text.Json.JsonSerializer.Serialize(await none.ResultAsync("chat_search", ("text", "x"))),
            System.Text.Json.JsonSerializer.Serialize(await hidden.ResultAsync("chat_search", ("text", "x"))));
    }

    [Fact]
    public async Task Should_never_return_the_summary_of_the_service()
    {
        using var fixture = Searching(GroupPlace);
        Finds(fixture, ChatHit("1", Samples.Group));

        var result = await fixture.ResultAsync("chat_search", ("text", "x"));

        var message = ToolResults.Object(Assert.Single(ToolResults.Objects(result, "hits")), "message");
        Assert.Equal(true, message["withheld"]);
        Assert.DoesNotContain(Summary, System.Text.Json.JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_withhold_a_policy_violation_and_a_rejected_text()
    {
        using var fixture = Searching(true, GroupPlace);
        Finds(fixture, ChatHit("1", Samples.Group), ChatHit("2", Samples.Group));
        fixture.Graph
            .Get(GroupMessages + "/1", Samples.Message("1", "leak", edit: message => message["policyViolation"] = new JsonObject { ["dlpAction"] = "blockAccess" }))
            .Get(GroupMessages + "/2", Samples.Message("2", Hostile.Text.Replace("<", "&lt;", StringComparison.Ordinal)));

        var result = await fixture.ResultAsync("chat_search", ("text", "x"));

        Assert.All(ToolResults.Objects(result, "hits"), hit =>
        {
            Assert.Equal(true, ToolResults.Object(hit, "message")["withheld"]);
            Assert.Null(ToolResults.Object(hit, "message").GetValueOrDefault("text"));
        });
    }

    [Fact]
    public async Task Should_leave_channels_out_until_the_administrator_grants_them()
    {
        using var fixture = Searching("*");
        Finds(fixture, ChannelHit("1"));

        var result = await fixture.ResultAsync("chat_search", ("text", "x"));

        Assert.Empty(ToolResults.Objects(result, "hits"));
        Assert.Contains("ChannelMessages", (string)result["limits"]!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_read_a_reply_of_a_channel_under_its_thread()
    {
        using var fixture = new ChatFixture(TestChatAccounts.Teams(ChatRights.Read, read: [Samples.BuildsPlace], granted: "ChannelMessages"));
        fixture.Graph.Directory().Get(Samples.BuildsMessages + "/5/replies/7", Samples.Message("7", "reply"));
        Finds(fixture, ChannelHit("7", "https://teams.microsoft.com/l/message/x/7?parentMessageId=5"));

        var hit = Assert.Single(ToolResults.Objects(await fixture.ResultAsync("chat_search", ("text", "x")), "hits"));

        Assert.Equal((Samples.BuildsPlace, "5"), (hit["place"], hit["thread"]));
        Assert.Equal("reply", ToolResults.Object(hit, "message")["text"]);
    }

    [Theory]
    [InlineData("../../me", Samples.Group)]
    [InlineData("1", "../x")]
    public async Task Should_drop_a_hit_whose_ids_have_not_their_shape(string id, string chat)
    {
        using var fixture = Searching("*");
        Finds(fixture, ChatHit(id, chat));

        Assert.Empty(ToolResults.Objects(await fixture.ResultAsync("chat_search", ("text", "x")), "hits"));
    }

    [Fact]
    public async Task Should_search_by_author_and_drop_what_another_wrote()
    {
        using var fixture = Searching(GroupPlace);
        Finds(fixture, ChatHit("1", Samples.Group), ChatHit("2", Samples.Group));
        fixture.Graph
            .Get(GroupMessages + "/1", Samples.Message("1", "by ada"))
            .Get(GroupMessages + "/2", Samples.Message("2", "by me", edit: message => message["from"]!["user"]!["id"] = Samples.Self));

        var result = await fixture.ResultAsync("chat_search", ("author", "ada@example.org"));

        Assert.Equal("by ada", ToolResults.Object(Assert.Single(ToolResults.Objects(result, "hits")), "message")["text"]);
        Assert.Contains("from:\\u0022ada@example.org\\u0022", Assert.Single(Searches(fixture)).Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_search_what_mentions_the_user()
    {
        using var fixture = Searching(GroupPlace);
        Finds(fixture);

        await fixture.ResultAsync("chat_search", ("mentions_me", true));

        Assert.Contains("mentions:" + Samples.Self.Replace("-", "", StringComparison.Ordinal), Assert.Single(Searches(fixture)).Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_page_through_the_hits_without_repeating_or_losing_one()
    {
        using var fixture = Searching(GroupPlace);
        Finds(fixture, ChatHit("9", Samples.Direct), ChatHit("1", Samples.Group), ChatHit("8", Samples.Direct), ChatHit("2", Samples.Group), ChatHit("3", Samples.Group));
        foreach (var id in new[] { "1", "2", "3" })
            fixture.Graph.Get(GroupMessages + "/" + id, Samples.Message(id, "m" + id));

        var read = new List<object?>();
        string? cursor = null;
        do
        {
            var result = await fixture.ResultAsync("chat_search", ("text", "x"), ("limit", 1), ("cursor", cursor));
            read.AddRange(ToolResults.Objects(result, "hits").Select(hit => ToolResults.Object(hit, "message")["text"]));
            cursor = (string?)result.GetValueOrDefault("next_cursor");
        }
        while (cursor is not null && read.Count < 10);

        Assert.Equal(["m1", "m2", "m3"], read);
    }

    [Fact]
    public async Task Should_refuse_the_cursor_of_another_search()
    {
        using var fixture = Searching(GroupPlace);
        Finds(fixture, ChatHit("1", Samples.Group), ChatHit("2", Samples.Group));
        fixture.Graph.Get(GroupMessages + "/1", Samples.Message("1", "a")).Get(GroupMessages + "/2", Samples.Message("2", "b"));
        var first = await fixture.ResultAsync("chat_search", ("text", "x"), ("limit", 1));

        Assert.Contains("`cursor`", await fixture.ErrorAsync("chat_search", ("text", "y"), ("limit", 1), ("cursor", first["next_cursor"])), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_stop_after_a_bounded_number_of_requests()
    {
        using var fixture = Searching(GroupPlace);
        fixture.Graph.On(HttpMethod.Post, "search/query", _ => FakeHttpResponse.Json(Answer(true, ChatHit("1", Samples.Direct))));

        Assert.Empty(ToolResults.Objects(await fixture.ResultAsync("chat_search", ("text", "x")), "hits"));
        Assert.Equal(5, Searches(fixture).Count);
    }

    [Theory]
    [InlineData("text", "  ", "one of `text`")]
    [InlineData("text", "\"*\"", "`text` holds no word")]
    [InlineData("author", "ada\" OR x", "`author` must be")]
    [InlineData("author", "Ada", "`author` must be")]
    public async Task Should_refuse_a_wrong_argument(string key, string value, string message)
    {
        using var fixture = Searching("*");

        Assert.Contains(message, await fixture.ErrorAsync("chat_search", (key, value)), StringComparison.Ordinal);
        Assert.Empty(Searches(fixture));
    }

    [Fact]
    public async Task Should_refuse_without_the_right_or_without_a_place()
    {
        using var sending = new ChatFixture(TestChatAccounts.Teams(ChatRights.Send, read: ["*"]));
        using var closed = Searching();

        Assert.Contains("Read", await sending.ErrorAsync("chat_search", ("text", "x")), StringComparison.Ordinal);
        Assert.Contains("Read:AllowedPlaces", await closed.ErrorAsync("chat_search", ("text", "x")), StringComparison.Ordinal);
        Assert.Empty(closed.Handler.Requests);
    }

    [Fact]
    public async Task Should_not_say_a_failed_search_may_have_posted()
    {
        using var fixture = Searching("*");
        fixture.Graph.On(HttpMethod.Post, "search/query", _ => GraphStub.Error(HttpStatusCode.Forbidden, "Forbidden"));

        Assert.DoesNotContain("posted", await fixture.ErrorAsync("chat_search", ("text", "x")), StringComparison.Ordinal);
    }

    private static IReadOnlyList<RecordedRequest> Searches(ChatFixture fixture) => fixture.Graph.To(HttpMethod.Post, "search/query");

    private static void Finds(ChatFixture fixture, params JsonObject[] hits) =>
        fixture.Graph.On(HttpMethod.Post, "search/query", _ => FakeHttpResponse.Json(Answer(false, hits)));

    private static string Answer(bool more, params JsonObject[] hits) => new JsonObject
    {
        ["value"] = new JsonArray(new JsonObject
        {
            ["hitsContainers"] = new JsonArray(new JsonObject
            {
                ["hits"] = new JsonArray(hits.Select(hit => hit.DeepClone()).ToArray()),
                ["moreResultsAvailable"] = more,
            }),
        }),
    }.ToJsonString();

    private static JsonObject ChatHit(string id, string chat) => Hit(new JsonObject { ["id"] = id, ["chatId"] = chat, ["channelIdentity"] = new JsonObject() });

    private static JsonObject ChannelHit(string id, string? link = null) => Hit(new JsonObject
    {
        ["id"] = id,
        ["channelIdentity"] = new JsonObject { ["teamId"] = Samples.Platform, ["channelId"] = Samples.Builds },
        ["webLink"] = link,
    });

    private static JsonObject Hit(JsonObject resource) => new() { ["hitId"] = "h", ["summary"] = Summary, ["resource"] = resource };

    private static ChatFixture Searching(params string[] places) => Searching(false, places);

    private static ChatFixture Searching(bool withhold, params string[] places)
    {
        var fixture = new ChatFixture(TestChatAccounts.Teams(ChatRights.Read, read: places), withhold);
        fixture.Graph.Directory();
        return fixture;
    }
}
