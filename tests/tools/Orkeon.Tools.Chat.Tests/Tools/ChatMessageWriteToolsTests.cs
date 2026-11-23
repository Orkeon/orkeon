using System.Net;
using System.Text.Json.Nodes;
using Orkeon.Tools.Chat.Configuration;
using Orkeon.Tools.Chat.Tests.Fixtures;

namespace Orkeon.Tools.Chat.Tests.Tools;

/// <summary>chat_edit, chat_delete and chat_react: whose message, in which place, and once.</summary>
public sealed class ChatMessageWriteToolsTests
{
    private const string GroupPlace = "chat:" + Samples.Group;
    private const string GroupMessage = "chats/" + Samples.Group + "/messages/1";
    private const string GroupDelete = "users/" + Samples.Self + "/" + GroupMessage + "/softDelete";
    private const string ChannelMessage = Samples.BuildsMessages + "/1";

    [Fact]
    public async Task Should_rewrite_its_own_message_of_a_chat()
    {
        using var fixture = Changing(GroupPlace);
        Own(fixture, GroupMessage);
        fixture.Graph.On(HttpMethod.Patch, GroupMessage, Done);

        var result = await fixture.ResultAsync("chat_edit", ("place", GroupPlace), ("message_id", "1"), ("text", "**new** <b>x</b>"), ("mentions", new[] { Samples.Ada }));

        Assert.Equal(("work", GroupPlace, "1"), (result["account"], result["place"], result["message_id"]));
        var body = JsonNode.Parse(Assert.Single(fixture.Graph.Writes).Body!)!;
        Assert.Contains("<strong>new</strong> &lt;b&gt;x", (string)body["body"]!["content"]!, StringComparison.Ordinal);
        Assert.Single(body["mentions"]!.AsArray());
    }

    [Fact]
    public async Task Should_delete_its_own_message_of_a_chat_under_its_user()
    {
        using var fixture = Changing(GroupPlace);
        Own(fixture, GroupMessage);
        fixture.Graph.On(HttpMethod.Post, GroupDelete, Done);

        await fixture.ResultAsync("chat_delete", ("place", GroupPlace), ("message_id", "1"));

        Assert.Single(fixture.Graph.To(HttpMethod.Post, GroupDelete));
        Assert.Null(Assert.Single(fixture.Graph.Writes).Body);
    }

    [Theory]
    [InlineData("chat_edit", null, "PATCH", "")]
    [InlineData("chat_edit", "5", "PATCH", "")]
    [InlineData("chat_delete", null, "POST", "/softDelete")]
    [InlineData("chat_delete", "5", "POST", "/softDelete")]
    public async Task Should_change_its_own_message_of_a_channel_once_granted(string tool, string? thread, string method, string suffix)
    {
        using var fixture = Changing(["ChannelEdit", "ChannelMessages"], "team:Platform/channel:builds");
        var path = thread is null ? ChannelMessage : Samples.BuildsMessages + "/5/replies/1";
        Own(fixture, path);
        fixture.Graph.On(new HttpMethod(method), path + suffix, Done);

        await fixture.ResultAsync(tool, Arguments(tool, Samples.BuildsPlace, ("thread", thread)));

        Assert.Single(fixture.Graph.To(new HttpMethod(method), path + suffix));
    }

    [Theory]
    [InlineData("chat_edit")]
    [InlineData("chat_delete")]
    [InlineData("chat_react")]
    public async Task Should_not_reach_a_channel_as_a_chat(string tool)
    {
        using var fixture = Changing(["ChannelEdit", "ChannelMessages"], "*");

        Assert.Contains("`place`", await fixture.ErrorAsync(tool, Arguments(tool, "chat:" + Samples.Builds)), StringComparison.Ordinal);
        Assert.Empty(fixture.Handler.Requests);
    }

    [Theory]
    [InlineData("chat_edit", "", "ChannelMessage.ReadWrite")]
    [InlineData("chat_delete", "", "ChannelMessage.ReadWrite")]
    [InlineData("chat_edit", "ChannelEdit", "ChannelMessage.Read.All")]
    [InlineData("chat_delete", "ChannelMessages", "ChannelMessage.ReadWrite")]
    public async Task Should_need_the_administrators_grant_in_a_channel_before_any_request(string tool, string granted, string permission)
    {
        using var fixture = Changing(granted.Split(',', StringSplitOptions.RemoveEmptyEntries), "*");

        Assert.Contains(permission, await fixture.ErrorAsync(tool, Arguments(tool, Samples.BuildsPlace)), StringComparison.Ordinal);
        Assert.Empty(fixture.Handler.Requests);
    }

    [Theory]
    [InlineData("chat_edit")]
    [InlineData("chat_delete")]
    public async Task Should_never_change_a_message_of_someone_else(string tool)
    {
        using var fixture = Changing(GroupPlace);
        fixture.Graph.Get(GroupMessage, Samples.Message("1", "by ada"));

        Assert.Contains("was not sent by chat account 'work'", await fixture.ErrorAsync(tool, Arguments(tool, GroupPlace)), StringComparison.Ordinal);
        Assert.Empty(fixture.Graph.Writes);
    }

    [Theory]
    [InlineData("chat_edit")]
    [InlineData("chat_delete")]
    public async Task Should_never_change_a_message_without_a_user_for_author(string tool)
    {
        using var fixture = Changing(GroupPlace);
        fixture.Graph.Get(GroupMessage, Samples.Message("1", "bot", edit: message =>
            message["from"] = new JsonObject { ["application"] = new JsonObject { ["id"] = Samples.Self, ["displayName"] = "bot" }, ["user"] = null }));

        Assert.Contains("was not sent by", await fixture.ErrorAsync(tool, Arguments(tool, GroupPlace)), StringComparison.Ordinal);
        Assert.Empty(fixture.Graph.Writes);
    }

    [Theory]
    [InlineData("chat_edit")]
    [InlineData("chat_delete")]
    [InlineData("chat_react")]
    public async Task Should_refuse_a_place_it_may_not_post_in(string tool)
    {
        using var fixture = Changing("chat:" + Samples.Direct);
        Own(fixture, GroupMessage);

        Assert.Contains(":Send:AllowedPlaces", await fixture.ErrorAsync(tool, Arguments(tool, GroupPlace)), StringComparison.Ordinal);
        Assert.Empty(fixture.Graph.Writes);
    }

    [Theory]
    [InlineData("chat_edit", "Edit")]
    [InlineData("chat_delete", "Delete")]
    [InlineData("chat_react", "React")]
    public async Task Should_need_its_own_right(string tool, string right)
    {
        using var fixture = new ChatFixture(TestChatAccounts.Teams(ChatRights.Read | ChatRights.Send, send: ["*"]));

        Assert.Contains(right, await fixture.ErrorAsync(tool, Arguments(tool, GroupPlace)), StringComparison.Ordinal);
        Assert.Empty(fixture.Handler.Requests);
    }

    [Fact]
    public async Task Should_not_rewrite_a_message_whose_attachments_it_would_drop()
    {
        using var fixture = Changing(GroupPlace);
        Own(fixture, GroupMessage, message => message["attachments"] = new JsonArray(new JsonObject { ["id"] = "a", ["contentType"] = "reference", ["name"] = "plan.docx" }));

        Assert.Contains("a rewrite would drop", await fixture.ErrorAsync("chat_edit", Arguments("chat_edit", GroupPlace)), StringComparison.Ordinal);
        Assert.Empty(fixture.Graph.Writes);
    }

    [Fact]
    public async Task Should_not_rewrite_a_message_whose_images_it_would_drop()
    {
        using var fixture = Changing(GroupPlace);
        Own(fixture, GroupMessage, message => message["body"] = new JsonObject { ["contentType"] = "html", ["content"] = "<p>x <img src=\"a\"></p>" });

        Assert.Contains("a rewrite would drop", await fixture.ErrorAsync("chat_edit", Arguments("chat_edit", GroupPlace)), StringComparison.Ordinal);
        Assert.Empty(fixture.Graph.Writes);
    }

    [Theory]
    [InlineData("chat_edit")]
    [InlineData("chat_delete")]
    public async Task Should_leave_a_deleted_message_alone(string tool)
    {
        using var fixture = Changing(GroupPlace);
        Own(fixture, GroupMessage, message => message["deletedDateTime"] = "2026-09-30T11:00:00Z");

        Assert.Contains("already deleted", await fixture.ErrorAsync(tool, Arguments(tool, GroupPlace)), StringComparison.Ordinal);
        Assert.Empty(fixture.Graph.Writes);
    }

    [Fact]
    public async Task Should_edit_in_the_worldwide_cloud_alone()
    {
        var options = TestChatAccounts.Teams(ChatRights.Edit, send: ["*"]);
        options.Cloud = ChatCloud.UsGov;
        using var fixture = new ChatFixture(options);

        Assert.Contains("worldwide cloud alone", await fixture.ErrorAsync("chat_edit", Arguments("chat_edit", GroupPlace)), StringComparison.Ordinal);
        Assert.Empty(fixture.Handler.Requests);
    }

    [Theory]
    [InlineData(false, "setReaction")]
    [InlineData(true, "unsetReaction")]
    public async Task Should_set_and_remove_a_reaction_in_a_chat(bool remove, string action)
    {
        using var fixture = Changing(GroupPlace);
        fixture.Graph.On(HttpMethod.Post, GroupMessage + "/" + action, Done);

        await fixture.ResultAsync("chat_react", ("place", GroupPlace), ("message_id", "1"), ("reaction", "👍🏽"), ("remove", remove));

        Assert.Equal("👍🏽", (string)JsonNode.Parse(Assert.Single(fixture.Graph.To(HttpMethod.Post, GroupMessage + "/" + action)).Body!)!["reactionType"]!);
    }

    [Fact]
    public async Task Should_not_react_in_a_channel()
    {
        using var fixture = Changing(["ChannelEdit", "ChannelMessages"], "*");

        Assert.Contains("A reaction in a channel is not supported", await fixture.ErrorAsync("chat_react", Arguments("chat_react", Samples.BuildsPlace)), StringComparison.Ordinal);
        Assert.Empty(fixture.Handler.Requests);
    }

    [Theory]
    [InlineData("like")]
    [InlineData("\u202e")]
    [InlineData("\u0301")]
    [InlineData("é")]
    [InlineData("👍👍")]
    [InlineData("1")]
    [InlineData("")]
    public async Task Should_refuse_what_is_not_one_emoji(string reaction)
    {
        using var fixture = Changing(GroupPlace);

        Assert.Contains("`reaction`", await fixture.ErrorAsync("chat_react", ("place", GroupPlace), ("message_id", "1"), ("reaction", reaction)), StringComparison.Ordinal);
        Assert.Empty(fixture.Handler.Requests);
    }

    [Theory]
    [InlineData("chat_edit", "rewritten")]
    [InlineData("chat_delete", "deleted")]
    [InlineData("chat_react", "changed")]
    public async Task Should_not_try_again_what_may_have_been_done(string tool, string doubt)
    {
        using var fixture = Changing(GroupPlace);
        Own(fixture, GroupMessage);
        foreach (var (method, path) in Routes)
            fixture.Graph.On(method, path, _ => GraphStub.Error(HttpStatusCode.GatewayTimeout, "timeout"));

        Assert.Contains($"may have {(tool == "chat_react" ? "" : "been ")}{doubt}", await fixture.ErrorAsync(tool, Arguments(tool, GroupPlace)), StringComparison.Ordinal);
        Assert.Single(fixture.Graph.Writes);
    }

    [Fact]
    public async Task Should_count_every_change_in_the_hourly_cap()
    {
        var options = TestChatAccounts.Teams(ChatRights.Edit | ChatRights.Delete | ChatRights.React, send: [GroupPlace]);
        options.Send.MaxPerHour = 1;
        using var fixture = new ChatFixture(options);
        fixture.Graph.Directory();
        Own(fixture, GroupMessage);
        foreach (var (method, path) in Routes)
            fixture.Graph.On(method, path, Done);
        await fixture.ResultAsync("chat_react", Arguments("chat_react", GroupPlace));

        foreach (var tool in new[] { "chat_edit", "chat_delete", "chat_react" })
            Assert.Contains("Send:MaxPerHour", await fixture.ErrorAsync(tool, Arguments(tool, GroupPlace)), StringComparison.Ordinal);

        Assert.Single(fixture.Graph.Writes);
    }

    [Fact]
    public async Task Should_share_the_pace_of_a_place()
    {
        using var fixture = Changing(GroupPlace);
        Own(fixture, GroupMessage);
        foreach (var (method, path) in Routes)
            fixture.Graph.On(method, path, Done);
        await fixture.ResultAsync("chat_react", Arguments("chat_react", GroupPlace));

        var pending = fixture.ResultAsync("chat_edit", Arguments("chat_edit", GroupPlace));
        await fixture.UntilWaitingAsync(pending);
        Assert.Single(fixture.Graph.Writes);
        fixture.Time.Advance(TimeSpan.FromSeconds(1));
        await pending;

        Assert.Equal(2, fixture.Graph.Writes.Count);
    }

    [Theory]
    [InlineData("chat_edit", "message_id", "../x")]
    [InlineData("chat_delete", "message_id", "../x")]
    [InlineData("chat_react", "message_id", "../x")]
    [InlineData("chat_delete", "thread", "../x")]
    [InlineData("chat_delete", "place", " ")]
    [InlineData("chat_edit", "text", " ")]
    [InlineData("chat_edit", "format", "html")]
    public async Task Should_refuse_a_wrong_argument(string tool, string key, string value)
    {
        using var fixture = Changing(GroupPlace);

        var arguments = Arguments(tool, GroupPlace).Where(argument => argument.Key != key).Append((key, value)).ToArray();

        Assert.Contains($"`{key}`", await fixture.ErrorAsync(tool, arguments), StringComparison.Ordinal);
        Assert.Empty(fixture.Handler.Requests);
    }

    private static IEnumerable<(HttpMethod Method, string Path)> Routes =>
        [(HttpMethod.Patch, GroupMessage), (HttpMethod.Post, GroupDelete), (HttpMethod.Post, GroupMessage + "/setReaction")];

    private static (string Key, object? Value)[] Arguments(string tool, string place, params (string Key, object? Value)[] more)
    {
        (string Key, object? Value)[] own = tool switch
        {
            "chat_edit" => [("text", "new")],
            "chat_react" => [("reaction", "👍")],
            _ => [],
        };
        return [("place", place), ("message_id", "1"), .. own, .. more.Where(argument => argument.Value is not null)];
    }

    private static FakeHttpResponse Done(RecordedRequest request) => new(HttpStatusCode.NoContent, "");

    private static void Own(ChatFixture fixture, string path, Action<JsonObject>? edit = null) =>
        fixture.Graph.Get(path, Samples.Message("1", "mine", edit: message =>
        {
            message["from"]!["user"]!["id"] = Samples.Self;
            edit?.Invoke(message);
        }));

    private static ChatFixture Changing(params string[] places) => Changing([], places);

    private static ChatFixture Changing(string[] granted, params string[] places)
    {
        var fixture = new ChatFixture(TestChatAccounts.Teams(ChatRights.Edit | ChatRights.Delete | ChatRights.React, send: places, granted: granted));
        fixture.Graph.Directory();
        return fixture;
    }
}
