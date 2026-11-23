using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Tools.Chat.DependencyInjection;
using Orkeon.Tools.Chat.Providers.Teams;
using Orkeon.Tools.Chat.Tests.Fixtures;

namespace Orkeon.Tools.Chat.Tests.Providers;

/// <summary>The conversation with Graph: where the token goes, what is repeated and what is not.</summary>
public sealed class GraphClientTests : IDisposable
{
    private const string NewTokens = """{"access_token":"second-access","refresh_token":"second-refresh","expires_in":3600}""";

    private readonly ChatFixture _fixture = new(TestChatAccounts.Teams(read: ["*"], send: ["*"]));
    private readonly HttpClient _http;
    private readonly GraphClient _graph;

    public GraphClientTests()
    {
        _http = new HttpClient(_fixture.Handler, disposeHandler: false);
        _graph = new GraphClient(() => _http, _fixture.Account, _fixture.Credentials, _fixture.Time);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Should_send_the_token_to_the_host_of_the_account()
    {
        _fixture.Graph.Get("me/joinedTeams", Samples.List());

        await _graph.GetAsync("me/joinedTeams", "Team.ReadBasic.All", Token);

        var request = Assert.Single(_fixture.Handler.Requests);
        Assert.Equal(new Uri("https://graph.microsoft.com/v1.0/me/joinedTeams"), request.Uri);
        Assert.Equal("Bearer " + ChatFixture.FirstAccess, request.Authorization);
    }

    [Theory]
    [InlineData("https://evil.test/v1.0/me")]
    [InlineData("//evil.test/v1.0/me")]
    [InlineData("http://graph.microsoft.com/v1.0/me")]
    [InlineData("https://graph.microsoft.com:8443/v1.0/me")]
    [InlineData("../beta/me")]
    [InlineData("/beta/me")]
    public async Task Should_send_nothing_outside_the_API(string path)
    {
        var error = await Assert.ThrowsAsync<ChatToolException>(() => _graph.GetAsync(path, "x", Token));

        Assert.Equal(ChatErrorCode.InvalidRequest, error.Code);
        Assert.Empty(_fixture.Handler.Requests);
    }

    [Theory]
    [InlineData("https://evil.test/v1.0/me?$skiptoken=a")]
    [InlineData("https://graph.microsoft.com.evil.test/v1.0/me?$skiptoken=a")]
    [InlineData("http://graph.microsoft.com/v1.0/me?$skiptoken=a")]
    [InlineData("/v1.0/me?$skiptoken=a")]
    [InlineData("https://graph.microsoft.com/v1.0/me?$top=3")]
    public void Should_refuse_a_paging_link_it_cannot_trust(string link)
    {
        var error = Assert.Throws<ChatToolException>(() => _graph.NextPage(JsonElement.Parse(Samples.Page(link))));

        Assert.Equal(ChatErrorCode.ServerError, error.Code);
    }

    [Fact]
    public void Should_keep_only_the_paging_token_of_a_link()
    {
        var page = JsonElement.Parse(Samples.Page("https://graph.microsoft.com/v1.0/teams/x/channels?$filter=y&$top=9&%24skiptoken=a%2Fb%3D&$skip=4"));

        Assert.Equal("$skiptoken=a%2Fb%3D&$skip=4", _graph.NextPage(page));
        Assert.Null(_graph.NextPage(JsonElement.Parse(Samples.List())));
    }

    [Fact]
    public async Task Should_not_follow_a_redirect()
    {
        _fixture.Graph.On(HttpMethod.Get, "me/chats", _ => new FakeHttpResponse(HttpStatusCode.Found, string.Empty, Location: new Uri("https://evil.test/")));

        var error = await Assert.ThrowsAsync<ChatToolException>(() => _graph.GetAsync("me/chats", "x", Token));

        Assert.Equal(ChatErrorCode.ServerError, error.Code);
        Assert.Single(_fixture.Handler.Requests);
    }

    [Fact]
    public void Should_be_given_a_client_that_follows_no_redirect()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOrkeonChatTools(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();

        HttpMessageHandler? handler = provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler("orkeon.chat");
        while (handler is DelegatingHandler delegating)
            handler = delegating.InnerHandler;

        Assert.False(handler switch
        {
            SocketsHttpHandler sockets => sockets.AllowAutoRedirect,
            HttpClientHandler client => client.AllowAutoRedirect,
            _ => true,
        });
    }

    [Fact]
    public async Task Should_refresh_once_after_a_401_and_keep_the_new_tokens()
    {
        _fixture.Graph.Identity("token", NewTokens)
            .On(HttpMethod.Post, "chats", _ => GraphStub.Error(HttpStatusCode.Unauthorized, "InvalidAuthenticationToken"))
            .Post("chats", "{\"id\":\"1\"}");

        await _graph.PostAsync("chats", new JsonObject(), "x", Token);

        Assert.Equal(["Bearer first-access", "Bearer second-access"], _fixture.Graph.Writes.Select(request => request.Authorization));
        Assert.Equal("second-refresh", Assert.Single(_fixture.Store.Tokens.Values).RefreshToken);
    }

    [Fact]
    public async Task Should_ask_for_a_sign_in_after_a_second_401()
    {
        _fixture.Graph.Identity("token", NewTokens).On(HttpMethod.Get, "me/chats", _ => GraphStub.Error(HttpStatusCode.Unauthorized, "InvalidAuthenticationToken"));

        var error = await Assert.ThrowsAsync<ChatToolException>(() => _graph.GetAsync("me/chats", "x", Token));

        Assert.Equal(ChatErrorCode.SignInRequired, error.Code);
        Assert.Contains("orkeon chat login work", error.Message, StringComparison.Ordinal);
        Assert.Equal(2, _fixture.Graph.To(HttpMethod.Get, "me/chats").Count);
        Assert.Single(_fixture.Graph.TokenRequests);
    }

    [Fact]
    public async Task Should_wait_a_short_Retry_After_and_send_again()
    {
        _fixture.Graph
            .On(HttpMethod.Post, "chats", _ => GraphStub.Error(HttpStatusCode.TooManyRequests, "TooManyRequests", retryAfter: TimeSpan.FromSeconds(4)))
            .Post("chats", "{\"id\":\"1\"}");

        var pending = _graph.PostAsync("chats", new JsonObject(), "x", Token);
        await _fixture.UntilWaitingAsync(pending);
        Assert.Single(_fixture.Graph.Writes);
        _fixture.Time.Advance(TimeSpan.FromSeconds(4));
        await pending;

        Assert.Equal(2, _fixture.Graph.Writes.Count);
    }

    [Fact]
    public async Task Should_give_up_after_a_second_429()
    {
        _fixture.Graph.On(HttpMethod.Get, "me/chats", _ => GraphStub.Error(HttpStatusCode.TooManyRequests, "TooManyRequests", retryAfter: TimeSpan.FromSeconds(1)));

        var pending = _graph.GetAsync("me/chats", "x", Token);
        await _fixture.UntilWaitingAsync(pending);
        _fixture.Time.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(ChatErrorCode.Throttled, (await Assert.ThrowsAsync<ChatToolException>(() => pending)).Code);
        Assert.Equal(2, _fixture.Handler.Requests.Count);
    }

    [Fact]
    public async Task Should_send_nothing_during_a_long_pause_and_resume_after_it()
    {
        _fixture.Graph
            .On(HttpMethod.Get, "me/chats", _ => GraphStub.Error(HttpStatusCode.TooManyRequests, "TooManyRequests", retryAfter: TimeSpan.FromSeconds(30)))
            .Get("me/chats", Samples.List());

        var first = await Assert.ThrowsAsync<ChatToolException>(() => _graph.GetAsync("me/chats", "x", Token));
        _fixture.Time.Advance(TimeSpan.FromSeconds(10));
        var second = await Assert.ThrowsAsync<ChatToolException>(() => _graph.GetAsync("me/chats", "x", Token));

        Assert.Equal((ChatErrorCode.Throttled, ChatErrorCode.Throttled), (first.Code, second.Code));
        Assert.Contains("retry in 30 s. Nothing was done.", first.Message, StringComparison.Ordinal);
        Assert.Contains("retry in 20 s", second.Message, StringComparison.Ordinal);
        Assert.Single(_fixture.Handler.Requests);
        _fixture.Time.Advance(TimeSpan.FromSeconds(20));
        await _graph.GetAsync("me/chats", "x", Token);
        Assert.Equal(2, _fixture.Handler.Requests.Count);
    }

    [Fact]
    public async Task Should_pause_a_team_whatever_the_case_of_its_id()
    {
        _fixture.Graph.On(HttpMethod.Get, "teams/ABCDEF/channels", _ => GraphStub.Error(HttpStatusCode.TooManyRequests, "TooManyRequests", retryAfter: TimeSpan.FromSeconds(30)));
        await Assert.ThrowsAsync<ChatToolException>(() => _fixture.Session.Provider.ListChannelsAsync("ABCDEF", Token));
        var sent = _fixture.Handler.Requests.Count;

        var error = await Assert.ThrowsAsync<ChatToolException>(() => _fixture.Session.Provider.ListChannelsAsync("abcdef", Token));

        Assert.Equal(ChatErrorCode.Throttled, error.Code);
        Assert.Equal(sent, _fixture.Handler.Requests.Count);
    }

    [Fact]
    public async Task Should_assume_ten_seconds_without_Retry_After()
    {
        _fixture.Graph.On(HttpMethod.Get, "me/chats", _ => GraphStub.Error(HttpStatusCode.TooManyRequests, "TooManyRequests"));

        var error = await Assert.ThrowsAsync<ChatToolException>(() => _graph.GetAsync("me/chats", "x", Token));

        Assert.Contains("retry in 10 s", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Chat.Read", "the user grants by signing in again (`orkeon chat login work`)")]
    [InlineData("ChannelMessage.Read.All", "a tenant administrator grants")]
    public async Task Should_name_the_permission_a_403_lacks_and_who_grants_it(string permission, string who)
    {
        _fixture.Graph.On(HttpMethod.Get, "me/chats", _ => GraphStub.Error(HttpStatusCode.Forbidden, "Forbidden", "missing scope"));

        var error = await Assert.ThrowsAsync<ChatToolException>(() => _graph.GetAsync("me/chats", permission, Token));

        Assert.Equal(ChatErrorCode.ConsentMissing, error.Code);
        Assert.Contains($"lacks the {permission} permission, which {who} (Forbidden: missing scope)", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "NotFound")]
    [InlineData(HttpStatusCode.Gone, "NotFound")]
    [InlineData(HttpStatusCode.BadRequest, "InvalidRequest")]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, "InvalidRequest")]
    [InlineData(HttpStatusCode.Conflict, "ServerError")]
    public async Task Should_code_a_refusal_that_posted_nothing(HttpStatusCode status, string code)
    {
        _fixture.Graph.On(HttpMethod.Post, "chats", _ => GraphStub.Error(status, "x"));

        var error = await Assert.ThrowsAsync<ChatToolException>(() => _graph.PostAsync("chats", new JsonObject(), "x", Token));

        Assert.Equal(Enum.Parse<ChatErrorCode>(code), error.Code);
        Assert.DoesNotContain("may have been posted", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("500")]
    [InlineData("503")]
    [InlineData("cut")]
    [InlineData("timeout")]
    public async Task Should_neither_repeat_a_write_that_may_be_done_nor_hide_it(string failure)
    {
        _fixture.Graph.On(HttpMethod.Post, "chats", _ => failure switch
        {
            "500" => GraphStub.Error(HttpStatusCode.InternalServerError, "x"),
            "503" => GraphStub.Error(HttpStatusCode.ServiceUnavailable, "x", retryAfter: TimeSpan.FromSeconds(1)),
            "cut" => throw new HttpRequestException(HttpRequestError.ResponseEnded, "cut"),
            _ => throw new TaskCanceledException("timed out"),
        });

        var error = await Assert.ThrowsAsync<ChatToolException>(() => _graph.PostAsync("chats", new JsonObject(), "x", Token));

        Assert.Equal(ChatErrorCode.ServerError, error.Code);
        Assert.Contains("may have been posted", error.Message, StringComparison.Ordinal);
        Assert.Single(_fixture.Graph.Writes);
    }

    [Fact]
    public async Task Should_not_doubt_a_write_that_never_left()
    {
        _fixture.Graph.On(HttpMethod.Post, "chats", _ => throw new HttpRequestException(HttpRequestError.NameResolutionError, "no host"));

        var error = await Assert.ThrowsAsync<ChatToolException>(() => _graph.PostAsync("chats", new JsonObject(), "x", Token));

        Assert.DoesNotContain("may have been posted", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_not_doubt_a_read()
    {
        _fixture.Graph.On(HttpMethod.Get, "me/chats", _ => throw new TaskCanceledException("timed out"));

        var error = await Assert.ThrowsAsync<ChatToolException>(() => _graph.GetAsync("me/chats", "x", Token));

        Assert.Equal(ChatErrorCode.ServerError, error.Code);
        Assert.DoesNotContain("posted", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_take_an_accepted_write_whose_answer_is_unreadable_for_done()
    {
        _fixture.Graph.Post("chats", "<html>").Get("me/chats", "<html>");

        Assert.Equal(JsonValueKind.Object, (await _graph.PostAsync("chats", new JsonObject(), "x", Token)).ValueKind);
        Assert.Equal(ChatErrorCode.ServerError, (await Assert.ThrowsAsync<ChatToolException>(() => _graph.GetAsync("me/chats", "x", Token))).Code);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _http.Dispose();
        _fixture.Dispose();
    }
}
