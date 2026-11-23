using System.Net;
using System.Text.Json.Nodes;
using Orkeon.Tests.Shared.Timing;
using Orkeon.Tools.Chat.Configuration;
using Orkeon.Tools.Chat.Security;
using Orkeon.Tools.Chat.Tests.Fixtures;

namespace Orkeon.Tools.Chat.Tests.Tools;

/// <summary>chat_send: nothing is written before everything is checked, and nothing is written twice.</summary>
public sealed class ChatSendToolTests
{
    private const string GroupPlace = "chat:" + Samples.Group;
    private const string GroupMessages = "chats/" + Samples.Group + "/messages";
    private const string DirectMessages = "chats/" + Samples.Direct + "/messages";

    private static readonly string Posted = Samples.Message("99", "x", "2026-10-01T09:00:00Z");

    [Fact]
    public async Task Should_post_markdown_in_a_channel()
    {
        using var fixture = Sending("team:Platform/channel:builds");
        fixture.Graph.Post(Samples.BuildsMessages, Posted);

        var result = await fixture.ResultAsync("chat_send", ("place", "team:Platform/channel:builds"), ("text", "**done** <at id=\"0\">all</at>"));

        Assert.Equal(("work", Samples.BuildsPlace, "99", "2026-10-01T09:00:00Z"), (result["account"], result["place"], result["message_id"], result["sent_at"]));
        Assert.Null(result.GetValueOrDefault("warning"));
        var content = Content(fixture);
        Assert.Contains("<strong>done</strong>", content, StringComparison.Ordinal);
        Assert.DoesNotContain("<at", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_post_plain_text_as_it_is()
    {
        using var fixture = Sending(GroupPlace);
        fixture.Graph.Post(GroupMessages, Posted);

        await fixture.ResultAsync("chat_send", ("place", GroupPlace), ("text", "**a** <b>"), ("format", " TEXT "));

        Assert.Contains("**a** &lt;b&gt;", Content(fixture), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("place", "", "`place` is required")]
    [InlineData("text", " ", "`text` is required")]
    [InlineData("format", "html", "`format` must be markdown or text")]
    [InlineData("reply_to", "1/../2", "`reply_to` must be the id of a message from chat_read")]
    [InlineData("mentions", "Ada", "`mentions` holds ids of people from chat_members, not names")]
    [InlineData("place", "Platform", "place")]
    public async Task Should_refuse_a_wrong_argument_without_any_request(string key, string value, string message)
    {
        using var fixture = Sending("*");
        var arguments = new Dictionary<string, object?> { ["place"] = GroupPlace, ["text"] = "x" };
        arguments[key] = key == "mentions" ? ToolResults.Of(value) : value;

        var error = await fixture.ErrorAsync("chat_send", [.. arguments.Select(pair => (pair.Key, pair.Value))]);

        Assert.Contains(message, error, StringComparison.Ordinal);
        Assert.Empty(fixture.Handler.Requests);
    }

    [Fact]
    public async Task Should_bound_the_mentions()
    {
        using var fixture = Sending("*");

        Assert.Contains("at most 20 ids", await fixture.ErrorAsync("chat_send",
            ("place", GroupPlace), ("text", "x"), ("mentions", Enumerable.Repeat(Samples.Ada, 21).ToList())), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Samples.BuildsPlace, null)]
    [InlineData(Samples.BuildsPlace, "17")]
    [InlineData("chat:" + Samples.Group, null)]
    [InlineData("chat:" + Samples.Group, "17")]
    [InlineData("chat:" + Samples.GuestDirect, null)]
    [InlineData("user:" + Samples.Guest, null)]
    [InlineData("user:" + Samples.Guest, "17")]
    public async Task Should_write_nothing_in_a_place_that_is_not_allowed(string place, string? reply)
    {
        using var fixture = Sending("team:Sales", "user:ada@example.org");

        var error = await fixture.ErrorAsync("chat_send", ("place", place), ("text", "x"), ("reply_to", reply));

        Assert.Contains("Send:AllowedPlaces", error, StringComparison.Ordinal);
        Assert.Empty(fixture.Graph.Writes);
    }

    [Fact]
    public async Task Should_not_post_where_it_may_only_read()
    {
        using var fixture = new ChatFixture(TestChatAccounts.Teams(read: ["*"], send: []));
        fixture.Graph.Directory();

        Assert.Contains("Send:AllowedPlaces", await fixture.ErrorAsync("chat_send", ("place", GroupPlace), ("text", "x")), StringComparison.Ordinal);
        Assert.Empty(fixture.Graph.Writes);
    }

    [Fact]
    public async Task Should_need_the_right_to_send()
    {
        using var fixture = new ChatFixture(TestChatAccounts.Teams(ChatRights.Read, read: ["*"], send: ["*"]));

        Assert.Contains("Send", await fixture.ErrorAsync("chat_send", ("place", GroupPlace), ("text", "x")), StringComparison.Ordinal);
        Assert.Empty(fixture.Handler.Requests);
    }

    [Theory]
    [InlineData(Samples.BuildsPlace, Samples.BuildsMessages + "/17/replies")]
    [InlineData(GroupPlace, GroupMessages + "/replyWithQuote")]
    public async Task Should_reply(string place, string path)
    {
        using var fixture = Sending("*");
        fixture.Graph.Post(path, Posted);

        await fixture.ResultAsync("chat_send", ("place", place), ("text", "x"), ("reply_to", "17"));

        Assert.Single(fixture.Graph.To(HttpMethod.Post, path));
    }

    [Fact]
    public async Task Should_open_the_chat_with_an_allowed_person_then_post_in_it()
    {
        using var fixture = Sending("user:*@example.org");
        fixture.Graph.Post("chats", Samples.DirectChat).Post(DirectMessages, Posted);

        var result = await fixture.ResultAsync("chat_send", ("place", "user:ada@example.org"), ("text", "x"));

        Assert.Equal("chat:" + Samples.Direct, result["place"]);
        Assert.Equal(["/v1.0/chats", "/v1.0/" + DirectMessages], fixture.Graph.Writes.Select(request => Uri.UnescapeDataString(request.Uri.AbsolutePath)));
    }

    [Fact]
    public async Task Should_mention_people_of_the_directory_once_each()
    {
        using var fixture = Sending(GroupPlace);
        fixture.Graph.Post(GroupMessages, Posted);

        await fixture.ResultAsync("chat_send", ("place", GroupPlace), ("text", "x"), ("mentions", ToolResults.Of(Samples.Ada, Samples.Guest, Samples.Ada)));

        var body = JsonNode.Parse(Assert.Single(fixture.Graph.Writes).Body!)!;
        Assert.Equal([Samples.Ada, Samples.Guest], body["mentions"]!.AsArray().Select(mention => (string?)mention!["mentioned"]!["user"]!["id"]));
        Assert.StartsWith("<at id=\"0\">Ada</at> <at id=\"1\">Gus</at> ", Content(fixture), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_post_nothing_When_a_mention_is_of_no_one()
    {
        using var fixture = Sending("*");

        var error = await fixture.ErrorAsync("chat_send", ("place", "user:ada@example.org"), ("text", "x"), ("mentions", ToolResults.Of(Samples.Sales)));

        Assert.Contains("who is not in the directory; nothing was posted.", error, StringComparison.Ordinal);
        Assert.Empty(fixture.Graph.Writes);
    }

    [Fact]
    public async Task Should_count_every_attempt_in_the_hourly_cap()
    {
        using var fixture = Sending(1, GroupPlace);
        fixture.Graph.On(HttpMethod.Post, GroupMessages, _ => GraphStub.Error(HttpStatusCode.BadRequest, "x"));
        await fixture.ErrorAsync("chat_send", ("place", GroupPlace), ("text", "x"));

        Assert.Contains("already attempted 1 messages in the last hour (Send:MaxPerHour); nothing was posted.",
            await fixture.ErrorAsync("chat_send", ("place", GroupPlace), ("text", "x")), StringComparison.Ordinal);
        Assert.Single(fixture.Graph.Writes);
    }

    [Fact]
    public async Task Should_not_count_a_refused_place_in_the_hourly_cap()
    {
        using var fixture = Sending(1, GroupPlace);
        fixture.Graph.Post(GroupMessages, Posted);
        await fixture.ErrorAsync("chat_send", ("place", Samples.BuildsPlace), ("text", "x"));

        await fixture.ResultAsync("chat_send", ("place", GroupPlace), ("text", "x"));
        Assert.Single(fixture.Graph.Writes);
    }

    [Fact]
    public async Task Should_post_once_a_second_in_one_place()
    {
        using var fixture = Sending("*");
        fixture.Graph.Post(GroupMessages, Posted).Post(Samples.BuildsMessages, Posted);
        await fixture.ResultAsync("chat_send", ("place", GroupPlace), ("text", "1"));
        await fixture.ResultAsync("chat_send", ("place", Samples.BuildsPlace), ("text", "elsewhere"));

        var pending = fixture.ResultAsync("chat_send", ("place", GroupPlace), ("text", "2"));
        await fixture.UntilWaitingAsync(pending);
        Assert.Single(fixture.Graph.To(HttpMethod.Post, GroupMessages));
        fixture.Time.Advance(TimeSpan.FromSeconds(1));
        await pending;

        Assert.Equal(2, fixture.Graph.To(HttpMethod.Post, GroupMessages).Count);
    }

    [Theory]
    [InlineData("x", 81921)]
    [InlineData("<", 25000)]
    [InlineData("€", 30000)]
    public async Task Should_refuse_a_message_too_large_once_rendered(string text, int times)
    {
        using var fixture = Sending(GroupPlace);

        var error = await fixture.ErrorAsync("chat_send", ("place", GroupPlace), ("text", string.Concat(Enumerable.Repeat(text, times))), ("format", "text"));

        Assert.Contains("The message is over 80 KB once rendered; nothing was posted.", error, StringComparison.Ordinal);
        Assert.Empty(fixture.Handler.Requests);
    }

    [Fact]
    public async Task Should_refuse_a_message_nested_without_end()
    {
        using var fixture = Sending(GroupPlace);

        var error = await fixture.ErrorAsync("chat_send", ("place", GroupPlace), ("text", new string('>', 5000) + "x"));

        Assert.Contains("nothing was posted", error, StringComparison.Ordinal);
        Assert.Empty(fixture.Handler.Requests);
    }

    [Fact]
    public async Task Should_not_send_again_a_message_that_may_be_posted()
    {
        using var fixture = Sending(GroupPlace);
        fixture.Graph.On(HttpMethod.Post, GroupMessages, _ => GraphStub.Error(HttpStatusCode.GatewayTimeout, "timeout"));

        Assert.Contains("may have been posted", await fixture.ErrorAsync("chat_send", ("place", GroupPlace), ("text", "x")), StringComparison.Ordinal);
        Assert.Single(fixture.Graph.Writes);
    }

    [Fact]
    public async Task Should_take_no_unit_of_the_cap_for_a_place_that_is_too_busy()
    {
        using var fixture = Sending(32, GroupPlace);
        fixture.Graph.Post(GroupMessages, """{"id":"1"}""");
        var queued = Enumerable.Range(0, 31).Select(_ => fixture.ResultAsync("chat_send", ("place", GroupPlace), ("text", "x"))).ToList();
        while (fixture.Time.PendingTimers < 30)
        {
            TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
        }

        Assert.Contains("nothing was posted", await fixture.ErrorAsync("chat_send", ("place", GroupPlace), ("text", "x")), StringComparison.Ordinal);

        fixture.Time.Advance(TimeSpan.FromSeconds(31));
        await Task.WhenAll(queued);
        await fixture.ResultAsync("chat_send", ("place", GroupPlace), ("text", "x"));
    }

    [Fact]
    public async Task Should_take_no_unit_of_the_cap_for_a_person_whose_chat_is_too_busy()
    {
        using var fixture = Sending(32, GroupPlace, "chat:" + Samples.Direct, "user:*@example.org");
        fixture.Graph.Post("chats", Samples.DirectChat).Post(DirectMessages, Posted).Post(GroupMessages, Posted);
        var queued = Enumerable.Range(0, 31).Select(_ => fixture.ResultAsync("chat_send", ("place", "user:ada@example.org"), ("text", "x"))).ToList();
        while (fixture.Time.PendingTimers < 30)
        {
            TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
        }

        Assert.Contains("nothing was posted", await fixture.ErrorAsync("chat_send", ("place", "user:ada@example.org"), ("text", "x")), StringComparison.Ordinal);
        await fixture.ResultAsync("chat_send", ("place", GroupPlace), ("text", "x"));
        Assert.Contains("Send:MaxPerHour", await fixture.ErrorAsync("chat_send", ("place", GroupPlace), ("text", "x")), StringComparison.Ordinal);

        fixture.Time.Advance(TimeSpan.FromSeconds(31));
        await Task.WhenAll(queued);
    }

    [Theory]
    [InlineData(GroupPlace)]
    [InlineData("user:ada@example.org")]
    public async Task Should_count_no_message_the_service_throttled(string place)
    {
        using var fixture = Sending(1, GroupPlace, "chat:" + Samples.Direct, "user:*@example.org");
        fixture.Graph.Post("chats", Samples.DirectChat);
        foreach (var path in new[] { GroupMessages, DirectMessages })
        {
            fixture.Graph.On(HttpMethod.Post, path, _ => GraphStub.Error(HttpStatusCode.TooManyRequests, "x", retryAfter: TimeSpan.FromSeconds(30)))
                .Post(path, Posted);
        }

        Assert.Contains("retry in 30 s", await fixture.ErrorAsync("chat_send", ("place", place), ("text", "x")), StringComparison.Ordinal);
        fixture.Time.Advance(TimeSpan.FromSeconds(2));
        // Refused by the pause, with no request: nothing done either.
        Assert.Contains("limits the rate", await fixture.ErrorAsync("chat_send", ("place", place), ("text", "x")), StringComparison.Ordinal);
        fixture.Time.Advance(TimeSpan.FromSeconds(31));
        await fixture.ResultAsync("chat_send", ("place", place), ("text", "x"));
        Assert.Contains("Send:MaxPerHour", await fixture.ErrorAsync("chat_send", ("place", place), ("text", "x")), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Should_count_a_message_in_doubt(HttpStatusCode status)
    {
        using var fixture = Sending(1, GroupPlace);
        fixture.Graph.On(HttpMethod.Post, GroupMessages, _ => GraphStub.Error(status, "x"));

        Assert.Contains("may have been posted", await fixture.ErrorAsync("chat_send", ("place", GroupPlace), ("text", "x")), StringComparison.Ordinal);
        Assert.Contains("Send:MaxPerHour", await fixture.ErrorAsync("chat_send", ("place", GroupPlace), ("text", "x")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_keep_the_turn_of_a_place_for_who_fits_in_the_cap()
    {
        using var fixture = Sending(1, GroupPlace);
        fixture.Graph.Post(GroupMessages, Posted);
        await fixture.ResultAsync("chat_send", ("place", GroupPlace), ("text", "x"));
        for (var refused = 0; refused < 40; refused++)
            Assert.Contains("Send:MaxPerHour", await fixture.ErrorAsync("chat_send", ("place", GroupPlace), ("text", "x")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_pace_a_channel_whatever_the_case_of_its_team_id()
    {
        var pacer = new PostPacer(new ManualTimeProvider());
        await pacer.WaitAsync("team:ABCDEF/channel:19:x", TestContext.Current.CancellationToken);

        Assert.False(pacer.WaitAsync("team:abcdef/channel:19:x", TestContext.Current.CancellationToken).IsCompleted);
    }

    [Fact]
    public async Task Should_say_a_message_is_posted_even_without_its_id()
    {
        using var fixture = Sending(GroupPlace);
        fixture.Graph.Post(GroupMessages, "{}");

        Assert.Contains("do not send it again", (string)(await fixture.ResultAsync("chat_send", ("place", GroupPlace), ("text", "x")))["warning"]!, StringComparison.Ordinal);
    }

    private static string Content(ChatFixture fixture) => (string)JsonNode.Parse(Assert.Single(fixture.Graph.Writes).Body!)!["body"]!["content"]!;

    private static ChatFixture Sending(params string[] places) => Sending(null, places);

    private static ChatFixture Sending(int? cap, params string[] places)
    {
        var options = TestChatAccounts.Teams(send: places);
        options.Send.MaxPerHour = cap;
        var fixture = new ChatFixture(options);
        fixture.Graph.Directory();
        return fixture;
    }
}
