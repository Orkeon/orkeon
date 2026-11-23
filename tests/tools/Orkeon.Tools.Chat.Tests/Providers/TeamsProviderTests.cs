using System.Text.Json;
using System.Text.Json.Nodes;
using Orkeon.Tools.Chat.Providers;
using Orkeon.Tools.Chat.Providers.Teams;
using Orkeon.Tools.Chat.Tests.Fixtures;

namespace Orkeon.Tools.Chat.Tests.Providers;

/// <summary>Each call of the provider: its path, its query and its body.</summary>
public sealed class TeamsProviderTests : IDisposable
{
    private static readonly ChatTarget Channel = new() { TeamId = Samples.Platform, ChannelId = Samples.Builds };
    private static readonly ChatTarget Chat = new() { ChatId = Samples.Group };

    private readonly ChatFixture _fixture = new(TestChatAccounts.Teams(read: ["*"], send: ["*"]));

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private IChatProvider Provider => _fixture.Session.Provider;

    [Fact]
    public async Task Should_read_the_signed_in_user_once()
    {
        Assert.Equal(Samples.Self, (await Provider.GetSelfAsync(Token)).Id);
        await Provider.GetSelfAsync(Token);

        Assert.Equal("/v1.0/me?$select=id,displayName,userPrincipalName,mail", Sent());
    }

    [Fact]
    public async Task Should_list_every_page_of_teams_and_channels()
    {
        _fixture.Graph
            .Get("me/joinedTeams", Samples.Page("https://graph.microsoft.com/v1.0/me/joinedTeams?$skiptoken=two", Samples.Named(Samples.Platform, "Platform")))
            .Get("me/joinedTeams", Samples.List(Samples.Named(Samples.Sales, "Sales")))
            .Get($"teams/{Samples.Platform}/channels", Samples.List(Samples.Channel(Samples.Builds, "builds", "private")));

        Assert.Equal([Samples.Platform, Samples.Sales], (await Provider.ListTeamsAsync(Token)).Select(team => team.Id));
        Assert.Equal(new ChatChannel(Samples.Builds, "builds", "private"), Assert.Single(await Provider.ListChannelsAsync(Samples.Platform, Token)));
        Assert.Equal(
            ["/v1.0/me/joinedTeams", "/v1.0/me/joinedTeams?$skiptoken=two", $"/v1.0/teams/{Samples.Platform}/channels?$select=id,displayName,membershipType"],
            _fixture.Handler.Requests.Select(Address));
    }

    [Fact]
    public async Task Should_list_and_read_chats_with_their_members()
    {
        _fixture.Graph.Get("me/chats", Samples.Page("https://graph.microsoft.com/v1.0/me/chats?$top=7&$skiptoken=n", Samples.DirectChat)).Get($"chats/{Samples.Group}", Samples.GroupChat);

        var page = await Provider.ListChatsAsync(7, "$skiptoken=p", Token);
        var chat = await Provider.GetChatAsync(Samples.Group, Token);

        Assert.Equal("$skiptoken=n", page.Next);
        Assert.True(Assert.Single(page.Items).IsOneOnOne);
        Assert.Equal(("group", "Weekly", 2), (chat.Kind, chat.Topic, chat.Members.Count));
        Assert.Equal(["/v1.0/me/chats?$expand=members&$top=7&$skiptoken=p", $"/v1.0/chats/{Samples.Group}?$expand=members"], _fixture.Handler.Requests.Select(Address));
        Assert.Contains("chats/19%3A", _fixture.Handler.Requests[1].Uri.AbsoluteUri, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Should_list_the_members_of_a_place()
    {
        var channel = $"teams/{Samples.Platform}/channels/{Samples.Builds}/members";
        _fixture.Graph.Get(channel, Samples.List(Samples.Member(Samples.Ada, "Ada", "ada@example.org", "owner"))).Get($"chats/{Samples.Group}/members", Samples.List());

        var member = Assert.Single(await Provider.ListMembersAsync(Channel, Token));

        Assert.Equal((Samples.Ada, "Ada", "ada@example.org"), (member.UserId, member.Name, member.Email));
        Assert.Equal(["owner"], member.Roles);
        Assert.Empty(await Provider.ListMembersAsync(Chat, Token));
    }

    [Fact]
    public async Task Should_find_a_person_by_sign_in_name_then_by_mail()
    {
        _fixture.Graph.Get("users/ada@example.org", Samples.AdaPerson)
            .Get("users", Samples.List(Samples.GuestPerson(), Samples.Person(Samples.Ada, "Other", "other@example.org", "other@example.org")));

        Assert.Equal(Samples.Ada, Assert.Single(await Provider.FindByAddressAsync("ada@example.org", Token)).Id);
        Assert.Equal(Samples.Guest, Assert.Single(await Provider.FindByAddressAsync("gus@partner.test", Token)).Id);
        Assert.Equal(
            "/v1.0/users?$filter=mail eq 'gus@partner.test'&$select=id,displayName,userPrincipalName,mail&$top=5",
            Address(_fixture.Handler.Requests[^1]));
    }

    [Fact]
    public async Task Should_quote_an_address_and_a_name()
    {
        _fixture.Graph.Get("users", Samples.List());

        await Provider.FindByAddressAsync("o'hara@example.org", Token);
        Assert.Contains("mail eq 'o''hara@example.org'", Address(_fixture.Handler.Requests[^1]), StringComparison.Ordinal);
        await Provider.FindByNameAsync("a\" OR \"mail:x&$top=999", 20, Token);

        var uri = _fixture.Handler.Requests[^1].Uri;
        Assert.Equal(3, uri.Query.Split('&').Length);
        Assert.Contains("$search=\"displayName:a\\\" OR \\\"mail:x&$top=999\"", Uri.UnescapeDataString(uri.Query), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_answer_null_for_a_person_the_directory_does_not_have() => Assert.Null(await Provider.GetPersonAsync(Samples.Ada, Token));

    [Fact]
    public async Task Should_find_the_direct_chat_over_the_pages()
    {
        _fixture.Graph
            .Get("me/chats", Samples.Page("https://graph.microsoft.com/v1.0/me/chats?$skiptoken=2", Samples.GroupChat))
            .Get("me/chats", Samples.List(Samples.DirectChat));

        Assert.Equal(Samples.Direct, await Provider.FindDirectChatAsync(Samples.Ada, Token));
        Assert.Null(await Provider.FindDirectChatAsync(Samples.Guest, Token));
    }

    [Fact]
    public async Task Should_doubt_the_opening_of_a_chat_not_a_post()
    {
        _fixture.Graph.Directory().On(HttpMethod.Post, "chats", _ => throw new TaskCanceledException("timed out"));

        var error = await Assert.ThrowsAsync<ChatToolException>(() => Provider.OpenDirectChatAsync(Samples.Ada, Token));

        Assert.Contains("The chat may have been opened all the same", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("posted all the same", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Samples.Ada, "owner")]
    [InlineData(Samples.Guest, "guest")]
    public async Task Should_open_a_direct_chat(string person, string role)
    {
        _fixture.Graph.Directory().Post("chats", Samples.DirectChat);

        Assert.Equal(Samples.Direct, await Provider.OpenDirectChatAsync(person, Token));

        var body = Body();
        Assert.Equal("oneOnOne", (string?)body["chatType"]);
        Assert.Equal($"https://graph.microsoft.com/v1.0/users('{Samples.Self}')", (string?)body["members"]![0]!["user@odata.bind"]);
        Assert.Equal($"https://graph.microsoft.com/v1.0/users('{person}')", (string?)body["members"]![1]!["user@odata.bind"]);
        Assert.Equal(role, (string?)body["members"]![1]!["roles"]![0]);
    }

    [Fact]
    public async Task Should_read_a_channel_a_thread_and_a_chat()
    {
        _fixture.Graph.Get(Samples.BuildsMessages, Samples.List(Samples.Message("1", "hello")))
            .Get(Samples.BuildsMessages + "/1/replies", Samples.List())
            .Get($"chats/{Samples.Group}/messages", Samples.List());

        Assert.Equal("hello", Assert.Single((await Provider.ReadAsync(Channel, new ChatReadQuery(9, null, null, null), Token)).Items).Text);
        Assert.Equal($"/v1.0/{Samples.BuildsMessages}?$top=9&$expand=replies", Sent());
        await Provider.ReadAsync(Channel, new ChatReadQuery(9, "$skiptoken=x", null, "1"), Token);
        Assert.Equal($"/v1.0/{Samples.BuildsMessages}/1/replies?$top=9&$skiptoken=x", Address(_fixture.Handler.Requests[^1]));
        await Provider.ReadAsync(Chat, new ChatReadQuery(9, null, null, null), Token);
        Assert.Equal($"/v1.0/chats/{Samples.Group}/messages?$top=9&$orderby=createdDateTime desc", Address(_fixture.Handler.Requests[^1]));
        await Provider.ReadAsync(Chat, new ChatReadQuery(9, null, new DateTimeOffset(2026, 10, 1, 2, 0, 0, TimeSpan.FromHours(2)), null), Token);
        Assert.EndsWith("&$filter=createdDateTime lt 2026-10-01T00:00:00.000Z", Address(_fixture.Handler.Requests[^1]), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, null, Samples.BuildsMessages)]
    [InlineData(true, "17", Samples.BuildsMessages + "/17/replies")]
    [InlineData(false, null, "chats/" + Samples.Group + "/messages")]
    [InlineData(false, "17", "chats/" + Samples.Group + "/messages/replyWithQuote")]
    public async Task Should_post_and_reply(bool channel, string? reply, string path)
    {
        _fixture.Graph.Post(path, Samples.Message("99", "x", "2026-10-01T09:00:00Z"));

        var sent = await Provider.SendAsync(channel ? Channel : Chat, new ChatOutgoing("<p>x</p>", [], reply), Token);

        Assert.Equal(("99", new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero)), (sent.Id, sent.Created));
        var body = Body();
        if (!channel && reply is not null)
        {
            Assert.Equal("17", (string?)body["messageIds"]![0]);
            body = body["replyMessage"]!.AsObject();
        }

        Assert.Equal("<p>x</p>", (string?)body["body"]!["content"]);
        Assert.Equal("html", (string?)body["body"]!["contentType"]);
        Assert.False(body.ContainsKey("mentions"));
    }

    [Fact]
    public void Should_write_mentions_the_text_cannot_forge()
    {
        var body = GraphMessages.Write(new ChatOutgoing("<p>hi</p>", [new ChatPerson(Samples.Ada, "<at> & Ada", "ada@example.org", null), new ChatPerson(Samples.Guest, " ", null, null)], null));

        Assert.Equal("<at id=\"0\">&lt;at&gt; &amp; Ada</at> <at id=\"1\">someone</at> <p>hi</p>", (string?)body["body"]!["content"]);
        Assert.Equal((0, Samples.Ada), ((int)body["mentions"]![0]!["id"]!, (string?)body["mentions"]![0]!["mentioned"]!["user"]!["id"]));
    }

    [Fact]
    public void Should_read_everything_a_message_carries()
    {
        var message = Read(Samples.Message("1", "body", edit: json =>
        {
            json["subject"] = "Title";
            json["lastEditedDateTime"] = "2026-09-30T11:00:00Z";
            json["attachments"] = new JsonArray(new JsonObject { ["name"] = "plan.docx" }, new JsonObject { ["contentType"] = "messageReference" });
            json["mentions"] = new JsonArray(new JsonObject { ["mentionText"] = "Ada", ["mentioned"] = new JsonObject { ["user"] = new JsonObject { ["id"] = Samples.Ada } } });
            json["reactions"] = new JsonArray(new JsonObject { ["reactionType"] = "like" }, new JsonObject { ["reactionType"] = "like" });
            json["replies"] = new JsonArray(new JsonObject(), new JsonObject());
            json["replies@odata.nextLink"] = "https://graph.microsoft.com/v1.0/x";
        }));

        Assert.Equal("Title\nbody", message.Text);
        Assert.NotNull(message.Edited);
        Assert.Equal(["plan.docx", "messageReference"], message.Attachments);
        Assert.Equal(new ChatMention(Samples.Ada, "Ada"), Assert.Single(message.Mentions));
        Assert.Equal(2, message.Reactions["like"]);
        Assert.Equal((2, true), (message.Replies, message.MoreReplies));
        Assert.False(message.IsDeleted || message.ViolatesPolicy || message.HadHiddenText);
    }

    [Theory]
    [InlineData("federatedUser", true)]
    [InlineData("anonymousGuest", true)]
    [InlineData("aadUser", false)]
    public void Should_tell_an_external_author(string kind, bool external) => Assert.Equal(
        external,
        Read(Samples.Message("1", "x", edit: json => json["from"]!["user"]!["userIdentityType"] = kind)).Author?.IsExternal);

    [Fact]
    public void Should_read_an_application_a_system_event_and_a_message_of_no_one()
    {
        var application = Read(Samples.Message("1", "x", edit: json => json["from"] = new JsonObject { ["user"] = null, ["application"] = new JsonObject { ["id"] = "app", ["displayName"] = "Bot" } }));

        Assert.Equal(new ChatAuthor("app", "Bot", IsApplication: true, IsExternal: false), application.Author);
        Assert.Null(Read(Samples.Message("1", "x", edit: json => json["from"] = null)).Author);
        Assert.False(Read(Samples.Message("1", "x", edit: json => json["messageType"] = "systemEventMessage")).IsMessage);
        Assert.Equal("a < b", Read(Samples.Message("1", "x", edit: json => json["body"] = new JsonObject { ["contentType"] = "text", ["content"] = "a < b" })).Text);
        Assert.Equal(string.Empty, Read("{}").Id);
    }

    /// <inheritdoc />
    public void Dispose() => _fixture.Dispose();

    private static ChatMessage Read(string json) => GraphMessages.Read(JsonElement.Parse(json), isChannel: true);

    private static string Address(RecordedRequest request) => Uri.UnescapeDataString(request.Uri.PathAndQuery);

    private string Sent() => Address(Assert.Single(_fixture.Handler.Requests));

    private JsonObject Body() => JsonNode.Parse(Assert.Single(_fixture.Graph.Writes).Body!)!.AsObject();
}
