using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orkeon.Host.Gateway;
using Orkeon.Host.Tests.Doubles;
using Orkeon.Tests.Shared.Network;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Host.Tests;

/// <summary>The Teams channel over a real listener (#446): what reaches the gateway, what does not, and where replies go.</summary>
public sealed class TeamsChannelTests : IAsyncLifetime
{
    private readonly FakeBotConnector _microsoft = new();
    private readonly RecordingLoggerProvider _logs = new();
    private readonly ConcurrentQueue<InboundMessage> _received = new();
    private readonly ConcurrentQueue<CommandInvocation> _commands = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly HttpClient _client = new();
    private ILoggerFactory _loggers = null!;
    private TeamsChannel _channel = null!;
    private Task _running = Task.CompletedTask;
    private Uri _address = null!;

    /// <summary>What the gateway does with a message; records it unless a test says otherwise.</summary>
    private Func<InboundMessage, CancellationToken, Task> _onMessage = (_, _) => Task.CompletedTask;

    public async ValueTask InitializeAsync()
    {
        _loggers = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(_logs));
        (_channel, var port) = await LoopbackPorts.StartAsync(Build, (channel, _) =>
        {
            channel.Bind();
            return Task.CompletedTask;
        });
        _running = _channel.RunAsync((message, ct) =>
        {
            _received.Enqueue(message);
            return _onMessage(message, ct);
        }, _stop.Token);
        _address = new Uri(LoopbackPorts.BaseAddress(port), "api/messages");
    }

    private TeamsChannel Build(int port)
    {
        var options = _microsoft.Options(LoopbackPorts.BaseAddress(port).AbsoluteUri);
        var channel = new TeamsChannel(Options.Create(options), _loggers.CreateLogger<TeamsChannel>(), _microsoft.Time, _microsoft.Transport);
        channel.CommandInvoked += invocation =>
        {
            _commands.Enqueue(invocation);
            return Task.FromResult(new CommandReply("Stopping."));
        };
        return channel;
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        await _running;
        await _channel.DisposeAsync();
        _stop.Dispose();
        _client.Dispose();
        _loggers.Dispose();
        _logs.Dispose();
        _microsoft.Dispose();
    }

    private Task<HttpStatusCode> PostAsync(Dictionary<string, object?> activity, string? token = null) =>
        PostAsync(JsonSerializer.Serialize(activity), token ?? _microsoft.Token());

    private async Task<HttpStatusCode> PostAsync(string body, string? token, string contentType = "application/json")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _address);
        request.Content = new StringContent(body, Encoding.UTF8);
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);
        return response.StatusCode;
    }

    /// <summary>Posts a good message and waits for it: whatever came before and was to be handled has been dispatched.</summary>
    private async Task<InboundMessage> NextAsync(Dictionary<string, object?> activity)
    {
        var before = _received.Count;
        Assert.Equal(HttpStatusCode.OK, await PostAsync(activity));
        await Polling.WaitUntilAsync(() => _received.Count > before, Polling.DefaultTimeout);
        return _received.Last();
    }

    /// <summary>Asserts that what was posted so far reached nobody: the next good message is the only one received.</summary>
    private async Task AssertNothingRanAsync()
    {
        var sentinel = await NextAsync(Activities.Message("sentinel", id: "sentinel"));
        Assert.Equal("sentinel", sentinel.Text);
        Assert.Single(_received);
        Assert.Empty(_commands);
    }

    private async Task<TeamsInboundMessage> InboundAsync() =>
        Assert.IsType<TeamsInboundMessage>(await NextAsync(Activities.Message()));

    // ---- What reaches the gateway -------------------------------------------------------

    [Fact]
    public async Task A_message_reaches_the_gateway_as_its_thread_without_the_bots_mention()
    {
        var message = await NextAsync(Activities.Message("<at>Orkeon</at>&nbsp;summarise <at>Ada</at>'s note"));

        Assert.Equal("teams", message.Channel);
        Assert.Equal(Activities.Thread, message.ConversationId);
        Assert.Equal(Activities.Room, message.RoomId);
        Assert.Equal(Activities.UserId, message.SenderId);
        Assert.Equal("summarise <at>Ada</at>'s note", message.Text);
        Assert.Equal(_microsoft.Time.GetUtcNow(), message.ReceivedAt);
    }

    [Fact]
    public async Task Two_threads_of_a_room_are_two_conversations()
    {
        var first = await NextAsync(Activities.Message(id: "1", conversation: Activities.Room + ";messageid=1"));
        var second = await NextAsync(Activities.Message(id: "2", conversation: Activities.Room + ";messageid=2"));

        Assert.NotEqual(first.ConversationId, second.ConversationId);
        Assert.Equal(first.RoomId, second.RoomId);
    }

    [Fact]
    public async Task A_channel_message_without_a_thread_opens_its_own()
    {
        var message = await NextAsync(Activities.Message(id: "42", conversation: Activities.Room));

        Assert.Equal(Activities.Room + ";messageid=42", message.ConversationId);
    }

    [Fact]
    public async Task A_personal_chat_is_one_conversation()
    {
        var activity = Activities.Message(conversation: "a:chat");
        activity["conversation"] = new { id = "a:chat", conversationType = "personal", tenantId = FakeBotConnector.TenantId };

        var message = await NextAsync(activity);

        Assert.Equal(("a:chat", "a:chat"), (message.ConversationId, message.RoomId));
    }

    [Fact]
    public async Task The_sender_is_named_by_the_canonical_form_of_its_object_id()
    {
        var activity = Activities.Message();
        activity["from"] = new { id = "29:user", aadObjectId = "ABCDEFAB-8888-7777-6666-555555555555" };

        Assert.Equal("abcdefab-8888-7777-6666-555555555555", (await NextAsync(activity)).SenderId);
    }

    [Fact]
    public async Task The_request_is_answered_while_the_run_goes_on()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _onMessage = (_, _) => gate.Task;

        await NextAsync(Activities.Message(id: "1"));
        await NextAsync(Activities.Message(id: "2"));
        Assert.False(gate.Task.IsCompleted);

        gate.SetResult();
    }

    [Fact]
    public async Task A_handler_that_fails_does_not_take_the_channel_down()
    {
        _onMessage = (_, _) => throw new InvalidOperationException("boom");
        await NextAsync(Activities.Message(id: "1"));
        await NextAsync(Activities.Message(id: "2"));
    }

    // ---- The door -----------------------------------------------------------------------

    [Fact]
    public async Task A_token_that_is_not_the_bot_connectors_is_refused_and_nothing_runs()
    {
        var body = JsonSerializer.Serialize(Activities.Message());
        string?[] tokens =
        [
            null,
            "not-a-token",
            _microsoft.Token(signedWith: _microsoft.AnotherKey()),
            _microsoft.Token(notBefore: TimeSpan.FromHours(-2), expires: TimeSpan.FromHours(-1)),
            _microsoft.Token(audience: "another-bot"),
            _microsoft.Token(issuer: "https://evil.example"),
            _microsoft.Token(serviceUrl: "https://evil.example/"),
        ];

        foreach (var token in tokens)
            Assert.Equal(HttpStatusCode.Unauthorized, await PostAsync(body, token));

        await AssertNothingRanAsync();
    }

    [Fact]
    public async Task The_token_is_checked_before_the_body_is_read()
    {
        // Each of these bodies has its own refusal once the caller is known; a stranger learns none of them.
        Assert.Equal(HttpStatusCode.Unauthorized, await PostAsync("{", null));
        Assert.Equal(HttpStatusCode.Unauthorized, await PostAsync("{}", null, "text/plain"));
        Assert.Equal(HttpStatusCode.Unauthorized, await PostAsync(new string(' ', TeamsChannel.MaxBodyBytes + 1), null));
    }

    [Fact]
    public async Task A_body_that_is_not_the_one_the_token_vouches_for_is_refused()
    {
        var otherService = Activities.Message();
        otherService["serviceUrl"] = "https://smba.trafficmanager.net/amer/";
        var evilService = Activities.Message();
        evilService["serviceUrl"] = "https://evil.example/";
        var noService = Activities.Message();
        noService.Remove("serviceUrl");
        var otherChannel = Activities.Message();
        otherChannel["channelId"] = "webchat";
        var otherTenant = Activities.Message();
        otherTenant["channelData"] = new { tenant = new { id = Guid.NewGuid().ToString() } };
        var noTenant = Activities.Message();
        noTenant.Remove("channelData");
        noTenant["conversation"] = new { id = Activities.Thread };

        foreach (var activity in new[] { otherService, evilService, noService, otherChannel, otherTenant, noTenant })
            Assert.Equal(HttpStatusCode.Forbidden, await PostAsync(activity));

        await AssertNothingRanAsync();
    }

    [Fact]
    public async Task A_key_not_endorsed_for_teams_is_refused()
    {
        _microsoft.Endorsements = ["webchat"];

        Assert.Equal(HttpStatusCode.Forbidden, await PostAsync(Activities.Message()));
        Assert.Empty(_received);
    }

    [Fact]
    public async Task Keys_that_cannot_be_read_ask_the_service_to_come_back()
    {
        _microsoft.Metadata = new FakeHttpResponse(HttpStatusCode.BadGateway, "down");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, await PostAsync(Activities.Message()));
        Assert.Empty(_received);
    }

    [Fact]
    public async Task The_shape_of_the_request_is_held()
    {
        var token = _microsoft.Token();
        var body = JsonSerializer.Serialize(Activities.Message());

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, await PostAsync(body, token, "text/plain"));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, await PostAsync(body + new string(' ', TeamsChannel.MaxBodyBytes), token));
        Assert.Equal(HttpStatusCode.BadRequest, await PostAsync("{", token));
        Assert.Equal(HttpStatusCode.BadRequest, await PostAsync("[]", token));
        Assert.Equal(HttpStatusCode.BadRequest, await PostAsync("""{"text":"\ud800"}""", token));

        using var get = await _client.GetAsync(_address, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, get.StatusCode);
        using var elsewhere = await _client.GetAsync(new Uri(_address, "/runs"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, elsewhere.StatusCode);

        await AssertNothingRanAsync();
    }

    /// <summary>The answer, to the end of the connection, to a request that announces a body and never sends it whole.</summary>
    private async Task<string> AnnounceBodyAsync(string? token)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(_address.Host, _address.Port, TestContext.Current.CancellationToken);
        var head = $"POST {_address.AbsolutePath} HTTP/1.1\r\nHost: {_address.Authority}\r\nContent-Type: application/json\r\n"
            + (token is null ? "" : $"Authorization: Bearer {token}\r\n") + "Content-Length: 100000\r\n\r\n{";
        await client.GetStream().WriteAsync(Encoding.ASCII.GetBytes(head), TestContext.Current.CancellationToken);
        using var reader = new StreamReader(client.GetStream());
        return await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_refused_request_is_closed_without_waiting_for_the_body_it_announced()
    {
        var answer = await AnnounceBodyAsync(token: null)
            .WaitAsync(Polling.DefaultTimeout, TestContext.Current.CancellationToken);

        Assert.StartsWith("HTTP/1.1 401", answer, StringComparison.Ordinal);
        Assert.Contains("Connection: close", answer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_body_that_does_not_arrive_is_given_up()
    {
        var answer = AnnounceBodyAsync(_microsoft.Token());

        await Polling.WaitUntilAsync(() =>
        {
            _microsoft.Time.Advance(TeamsChannel.BodyDeadline);
            return answer.IsCompleted;
        }, Polling.DefaultTimeout);

        Assert.StartsWith("HTTP/1.1 503", await answer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Someone_off_the_list_has_one_message_handled_at_a_time()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _onMessage = (_, _) => gate.Task;
        var stranger = Activities.Message(id: "a");
        stranger["from"] = new { id = "29:stranger", aadObjectId = Guid.NewGuid().ToString() };
        await NextAsync(stranger);

        for (var i = 0; i < 5; i++)
        {
            stranger["id"] = $"b{i}";
            Assert.Equal(HttpStatusCode.OK, await PostAsync(stranger));
        }

        await NextAsync(Activities.Message(id: "c"));
        Assert.Equal(2, _received.Count);
        gate.SetResult();
    }

    [Fact]
    public async Task Refused_tokens_are_warned_about_once_in_a_while()
    {
        for (var i = 0; i < 5; i++)
            await PostAsync("{}", token: "forged");

        Assert.Single(_logs.Logger.Entries, e => e.ToString().Contains("Refused a request", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_body_of_undeclared_length_is_capped_too()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _address);
        request.Headers.TransferEncodingChunked = true;
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _microsoft.Token());
        request.Content = new StringContent(new string(' ', TeamsChannel.MaxBodyBytes + 1), Encoding.UTF8, "application/json");
        using var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task Other_activities_other_bots_and_empty_messages_are_acknowledged_and_ignored()
    {
        var reaction = Activities.Message(id: "1");
        reaction["type"] = "messageReaction";
        var bot = Activities.Message(id: "2");
        bot["from"] = new { id = "28:other", aadObjectId = Activities.UserId };
        var role = Activities.Message(id: "3");
        role["from"] = new { id = "29:x", role = "bot", aadObjectId = Activities.UserId };
        var itself = Activities.Message(id: "4");
        itself["from"] = new { id = Activities.BotId, aadObjectId = Activities.UserId };
        var anonymous = Activities.Message(id: "5");
        anonymous["from"] = new { id = "29:x" };
        var empty = Activities.Message("<at>Orkeon</at>", id: "6");

        foreach (var activity in new[] { reaction, bot, role, itself, anonymous, empty })
            Assert.Equal(HttpStatusCode.OK, await PostAsync(activity));

        await AssertNothingRanAsync();
    }

    [Fact]
    public async Task A_lone_file_reaches_the_gateway_by_its_name_and_never_its_address()
    {
        var activity = Activities.Message("<at>Orkeon</at>");
        activity["attachments"] = new object[]
        {
            new { contentType = "text/html", content = "<p>hello</p>" },
            new { contentType = "application/vnd.microsoft.card.adaptive", name = "card" },
            new
            {
                contentType = "application/vnd.microsoft.teams.file.download.info",
                name = "march.pdf",
                content = new { uniqueId = "u-1", downloadUrl = "https://contoso.sharepoint.com/secret" },
            },
            new { contentType = "image/png", contentUrl = "https://smba.example/image" },
            "not an object",
        };

        var message = await NextAsync(activity);

        Assert.Equal(string.Empty, message.Text);
        Assert.Equal(["march.pdf", "image"], message.Attachments.Select(a => a.Name));
        Assert.Equal("u-1", message.Attachments[0].Id);
        Assert.Equal("image/png", message.Attachments[1].ContentType);
        Assert.Null(message.AttachmentSource);
        Assert.DoesNotContain("secret", message + string.Concat(message.Attachments), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("..")]
    [InlineData("")]
    [InlineData("a\nb")]
    public async Task An_id_no_reply_could_be_addressed_with_is_refused(string id)
    {
        Assert.Equal(HttpStatusCode.BadRequest, await PostAsync(Activities.Message(id: id)));
        await AssertNothingRanAsync();
    }

    [Fact]
    public async Task A_redelivered_activity_runs_once()
    {
        await NextAsync(Activities.Message(id: "1"));
        Assert.Equal(HttpStatusCode.OK, await PostAsync(Activities.Message(id: "1")));
        await NextAsync(Activities.Message(id: "2"));
        Assert.Equal(2, _received.Count);
    }

    [Fact]
    public void Deliveries_are_remembered_for_a_window_and_up_to_a_capacity()
    {
        var recent = new TeamsChannel.RecentActivities(_microsoft.Time);
        Assert.True(recent.TryAdd("first"));
        Assert.False(recent.TryAdd("first"));

        _microsoft.Time.Advance(TeamsChannel.RecentActivities.Window);
        Assert.True(recent.TryAdd("first"));

        for (var i = 0; i < TeamsChannel.RecentActivities.Capacity; i++)
            Assert.True(recent.TryAdd($"other-{i}"));
        Assert.True(recent.TryAdd("first"));
    }

    // ---- Commands -----------------------------------------------------------------------

    [Theory]
    [InlineData("<at>Orkeon</at> /stop", "stop")]
    [InlineData("/STATUS ", "status")]
    public async Task A_command_is_invoked_and_answered_in_its_thread(string text, string command)
    {
        Assert.Equal(HttpStatusCode.OK, await PostAsync(Activities.Message(text)));
        await Polling.WaitUntilAsync(() => _microsoft.Replies.Count == 1, Polling.DefaultTimeout);

        Assert.Equal(new CommandInvocation(command, Activities.Thread, Activities.UserId) { RoomId = Activities.Room }, Assert.Single(_commands));
        Assert.Empty(_received);
        Assert.Contains("Stopping.", _microsoft.Replies[0].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_sentence_that_speaks_of_a_command_is_a_prompt()
    {
        Assert.Equal("explain /stop", (await NextAsync(Activities.Message("explain /stop"))).Text);
        Assert.Empty(_commands);
    }

    // ---- With the gateway behind it -----------------------------------------------------

    [Fact]
    public async Task An_unlisted_user_is_refused_and_a_listed_one_runs_the_rooms_crew()
    {
        HostedCrewOptions[] crews = [new() { Name = "triage", Path = "/crews/t" }, new() { Name = "billing", Path = "/crews/b" }];
        var options = _microsoft.Options() with { Routes = [new() { Channel = Activities.Room, Crew = "billing" }] };
        var runner = new ScriptedRunner();
        var authorizer = new AllowListChatAuthorizer(options.AllowedUserIds);
        var gateway = new ChatGateway(
            runner,
            new ThreadIsRunRouter(ChatRoutes.From(options, crews)),
            authorizer,
            new CrewHostRegistry(Options.Create(new OrkeonHostOptions { Crews = crews })),
            _loggers.CreateLogger<ChatGateway>());
        var handled = 0;
        _onMessage = async (message, ct) =>
        {
            await gateway.HandleAsync(message, _channel.Responder, ct);
            Interlocked.Increment(ref handled);
        };

        var stranger = Activities.Message(id: "1", conversation: Activities.Room + ";messageid=1");
        stranger["from"] = new { id = "29:other", aadObjectId = Guid.NewGuid().ToString() };
        await PostAsync(stranger);
        await Polling.WaitUntilAsync(() => Volatile.Read(ref handled) == 1, Polling.DefaultTimeout);
        Assert.Empty(runner.Ran);

        await PostAsync(Activities.Message(id: "2"));
        await Polling.WaitUntilAsync(() => Volatile.Read(ref handled) == 2, Polling.DefaultTimeout);
        Assert.Equal(["billing:hello"], runner.Ran);
    }

    // ---- Replies ------------------------------------------------------------------------

    [Fact]
    public async Task A_reply_goes_to_the_thread_on_the_validated_service_with_the_bots_token()
    {
        var message = await InboundAsync();
        await _channel.Responder.AcknowledgeAsync(message, "On it, été.", TestContext.Current.CancellationToken);

        var reply = Assert.Single(_microsoft.Replies);
        Assert.Equal(
            "https://smba.trafficmanager.net/emea/v3/conversations/19%3Aroom%40thread.tacv2%3Bmessageid%3D1700/activities/1701",
            reply.Uri.AbsoluteUri);
        Assert.Equal("Bearer outbound", reply.Authorization);
        Assert.Contains("On it, été.", reply.Body, StringComparison.Ordinal);
        using var body = JsonDocument.Parse(reply.Body!);
        Assert.Equal("message", body.RootElement.GetProperty("type").GetString());
        Assert.Equal("1701", body.RootElement.GetProperty("replyToId").GetString());
        Assert.Equal(Activities.Thread, body.RootElement.GetProperty("conversation").GetProperty("id").GetString());
        Assert.Equal(Activities.BotId, body.RootElement.GetProperty("from").GetProperty("id").GetString());
    }

    [Fact]
    public async Task An_id_cannot_leave_its_segment_of_the_address()
    {
        var message = (TeamsInboundMessage)await NextAsync(Activities.Message(id: "../../x?y#z"));
        await _channel.Responder.AcknowledgeAsync(message, "hi", TestContext.Current.CancellationToken);

        Assert.EndsWith("/activities/..%2F..%2Fx%3Fy%23z", Assert.Single(_microsoft.Replies).Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_bots_token_is_asked_for_once_and_again_before_it_expires()
    {
        var ct = TestContext.Current.CancellationToken;
        var message = await InboundAsync();
        await _channel.Responder.AcknowledgeAsync(message, "1", ct);
        await _channel.Responder.AcknowledgeAsync(message, "2", ct);

        var signIn = Assert.Single(_microsoft.SignIns);
        Assert.Equal($"https://login.microsoftonline.com/{FakeBotConnector.TenantId}/oauth2/v2.0/token", signIn.Uri.AbsoluteUri);
        Assert.Equal("client_credentials", signIn.Form["grant_type"]);
        Assert.Equal(FakeBotConnector.AppId, signIn.Form["client_id"]);
        Assert.Equal(_microsoft.Secret, signIn.Form["client_secret"]);
        Assert.Equal("https://api.botframework.com/.default", signIn.Form["scope"]);

        _microsoft.Time.Advance(TimeSpan.FromMinutes(56));
        await _channel.Responder.AcknowledgeAsync(message, "3", ct);
        Assert.Equal(2, _microsoft.SignIns.Count);
    }

    [Fact]
    public async Task A_refused_token_is_renewed_once()
    {
        _microsoft.Connector = () => new FakeHttpResponse(HttpStatusCode.Unauthorized, "no");
        var message = await InboundAsync();

        await _channel.Responder.CompleteAsync(message, "done", TestContext.Current.CancellationToken);

        Assert.Equal((2, 2), (_microsoft.SignIns.Count, _microsoft.Replies.Count));
    }

    [Fact]
    public async Task A_throttled_answer_is_sent_again_after_the_delay_the_service_asked()
    {
        var answers = new ConcurrentQueue<HttpStatusCode>([HttpStatusCode.TooManyRequests, HttpStatusCode.Created]);
        _microsoft.Connector = () => new FakeHttpResponse(
            answers.TryDequeue(out var status) ? status : HttpStatusCode.Created, "{}", RetryAfter: TimeSpan.FromSeconds(7));
        var message = await InboundAsync();

        var sending = _channel.Responder.CompleteAsync(message, "done", TestContext.Current.CancellationToken);
        await Polling.WaitUntilAsync(() => _microsoft.Time.PendingTimers > 0, Polling.DefaultTimeout);
        Assert.Single(_microsoft.Replies);
        _microsoft.Time.Advance(TimeSpan.FromSeconds(7));
        await sending.WaitAsync(Polling.DefaultTimeout, TestContext.Current.CancellationToken);

        Assert.Equal(2, _microsoft.Replies.Count);
    }

    [Fact]
    public async Task A_conflict_is_sent_again()
    {
        var answers = new ConcurrentQueue<HttpStatusCode>([HttpStatusCode.PreconditionFailed, HttpStatusCode.Created]);
        _microsoft.Connector = () => new FakeHttpResponse(answers.TryDequeue(out var status) ? status : HttpStatusCode.Created, "{}");

        await CompleteAfterOneDelayAsync(() => _microsoft.Replies.Count == 1);

        Assert.Equal(2, _microsoft.Replies.Count);
    }

    [Theory]
    [InlineData(HttpRequestError.ConnectionError, 2)]
    [InlineData(HttpRequestError.NameResolutionError, 2)]
    [InlineData(HttpRequestError.ResponseEnded, 1)]
    public async Task A_final_reply_is_sent_again_when_it_cannot_have_left(HttpRequestError error, int posts)
    {
        var failures = 0;
        _microsoft.Connector = () => failures++ == 0
            ? throw new HttpRequestException(error, "down")
            : new FakeHttpResponse(HttpStatusCode.Created, "{}");

        if (posts == 1)
            await _channel.Responder.CompleteAsync(await InboundAsync(), "done", TestContext.Current.CancellationToken);
        else
            await CompleteAfterOneDelayAsync(() => true);

        Assert.Equal(posts, _microsoft.Replies.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task A_final_reply_waits_for_a_sign_in_service_at_fault(HttpStatusCode status)
    {
        var good = _microsoft.SignIn;
        _microsoft.SignIn = new FakeHttpResponse(status, "later");

        await CompleteAfterOneDelayAsync(() => _microsoft.SignIns.Count == 1, () => _microsoft.SignIn = good);

        Assert.Equal((2, 1), (_microsoft.SignIns.Count, _microsoft.Replies.Count));
    }

    [Fact]
    public async Task A_refused_sign_in_is_not_asked_again()
    {
        _microsoft.SignIn = new FakeHttpResponse(HttpStatusCode.Unauthorized, "no");

        await _channel.Responder.CompleteAsync(await InboundAsync(), "done", TestContext.Current.CancellationToken);

        Assert.Single(_microsoft.SignIns);
    }

    [Fact]
    public async Task An_answer_posts_no_html_and_no_image()
    {
        await _channel.Responder.CompleteAsync(await InboundAsync(), "![a](https://x.test/) <img>", TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(Assert.Single(_microsoft.Replies).Body!);
        Assert.Equal("[a](https://x.test/) &lt;img>", body.RootElement.GetProperty("text").GetString());
    }

    [Fact]
    public void The_channels_own_transport_follows_no_redirect()
    {
        using var handler = TeamsChannel.CreateHandler();

        Assert.False(handler.AllowAutoRedirect);
    }

    private async Task CompleteAfterOneDelayAsync(Func<bool> firstAttemptMade, Action? before = null)
    {
        var message = await InboundAsync();
        var sending = _channel.Responder.CompleteAsync(message, "done", TestContext.Current.CancellationToken);
        await Polling.WaitUntilAsync(() => _microsoft.Time.PendingTimers > 0 && firstAttemptMade(), Polling.DefaultTimeout);
        before?.Invoke();
        _microsoft.Time.Advance(TimeSpan.FromSeconds(2));
        await sending.WaitAsync(Polling.DefaultTimeout, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_redirect_is_not_followed_and_a_refusal_fails_no_run()
    {
        _microsoft.Connector = () => new FakeHttpResponse(HttpStatusCode.TemporaryRedirect, "", Location: new Uri("https://evil.example/"));
        var message = await InboundAsync();

        await _channel.Responder.CompleteAsync(message, "done", TestContext.Current.CancellationToken);

        Assert.DoesNotContain(_microsoft.Transport.Requests, r => r.Uri.Host == "evil.example");
    }

    [Fact]
    public async Task Neither_the_secret_nor_a_token_nor_an_answer_reaches_the_logs()
    {
        var ct = TestContext.Current.CancellationToken;
        var forged = _microsoft.Token(signedWith: _microsoft.AnotherKey());
        await PostAsync("{}", forged);
        var message = await InboundAsync();

        _microsoft.SignIn = new FakeHttpResponse(HttpStatusCode.BadRequest, $"echo {_microsoft.Secret}");
        await _channel.Responder.CompleteAsync(message, "done", ct);
        _microsoft.SignIn = FakeHttpResponse.Json($"not json {_microsoft.Secret}");
        await _channel.Responder.CompleteAsync(message, "done", ct);
        _microsoft.SignIn = FakeHttpResponse.Json("""{"expires_in":3600,"access_token":"outbound"}""");
        _microsoft.Connector = () => new FakeHttpResponse(HttpStatusCode.Forbidden, "echo outbound");
        await _channel.Responder.CompleteAsync(message, "done", ct);

        var logs = string.Join('\n', _logs.Logger.Entries.Select(e => e.ToString()));
        Assert.Contains("HTTP 400", logs, StringComparison.Ordinal);
        Assert.Contains("HTTP 403", logs, StringComparison.Ordinal);
        Assert.DoesNotContain(_microsoft.Secret, logs, StringComparison.Ordinal);
        Assert.DoesNotContain("outbound", logs, StringComparison.Ordinal);
        Assert.DoesNotContain(forged, logs, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_message_of_another_channel_has_nowhere_to_go()
    {
        var message = new InboundMessage
        {
            Channel = "discord", ConversationId = "1", SenderId = "2", Text = "x", ReceivedAt = _microsoft.Time.GetUtcNow(),
        };

        await _channel.Responder.CompleteAsync(message, "done", TestContext.Current.CancellationToken);

        Assert.Empty(_microsoft.Transport.Requests);
    }
}
