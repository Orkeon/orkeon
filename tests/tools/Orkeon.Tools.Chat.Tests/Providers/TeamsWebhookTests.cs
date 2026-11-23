using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Orkeon.Tools.Chat.Accounts;
using Orkeon.Tools.Chat.Auth;
using Orkeon.Tools.Chat.Configuration;
using Orkeon.Tools.Chat.Constants;
using Orkeon.Tools.Chat.Providers.TeamsWebhook;
using Orkeon.Tools.Chat.Tests.Doubles;
using Orkeon.Tools.Chat.Tests.Fixtures;

namespace Orkeon.Tools.Chat.Tests.Providers;

/// <summary>The webhook account: one place, one attempt, and an address nobody reads.</summary>
public sealed class TeamsWebhookTests
{
    private const string Secret = "s1gnature";
    private const string Address = "https://hooks.example.org/workflows/abc/triggers/manual/paths/invoke?sig=" + Secret;
    private const string Doubt = "may have been posted";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Should_post_markdown_in_the_envelope_the_workflow_takes()
    {
        using var fixture = Create();
        fixture.Hook.Enqueue(new FakeHttpResponse(HttpStatusCode.Accepted, ""));

        var result = await fixture.ResultAsync("chat_send", ("text", "**fait** é \"x\""));

        var request = Assert.Single(fixture.Hook.Requests);
        Assert.Equal((HttpMethod.Post, Address, "application/json"), (request.Method, request.Uri.AbsoluteUri, request.ContentType));
        Assert.Null(request.Authorization);
        var body = JsonNode.Parse(request.Body!)!;
        Assert.Equal("message", (string?)body["type"]);
        var attachment = Assert.Single(body["attachments"]!.AsArray())!;
        Assert.Equal("application/vnd.microsoft.card.adaptive", (string?)attachment["contentType"]);
        Assert.Equal("AdaptiveCard", (string?)attachment["content"]!["type"]);
        var block = Assert.Single(attachment["content"]!["body"]!.AsArray())!;
        Assert.Equal(("TextBlock", "**fait** é \"x\"", true), ((string?)block["type"], (string?)block["text"], (bool?)block["wrap"]));
        Assert.Equal("work", result["account"]);
        Assert.Null(result.GetValueOrDefault("place"));
        Assert.Null(result.GetValueOrDefault("message_id"));
        Assert.Contains("returns no id", Assert.IsType<string>(result["warning"]), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_post_plain_text_where_nothing_is_formatted()
    {
        using var fixture = Create();
        fixture.Hook.EnqueueJson("{}");

        await fixture.ResultAsync("chat_send", ("text", "**a**"), ("format", "text"));

        var block = JsonNode.Parse(Assert.Single(fixture.Hook.Requests).Body!)!["attachments"]![0]!["content"]!["body"]![0]!;
        Assert.Equal(("RichTextBlock", "**a**"), ((string?)block["type"], (string?)block["inlines"]![0]!["text"]));
    }

    [Theory]
    [InlineData("place", "team:Platform")]
    [InlineData("place", "*")]
    [InlineData("reply_to", "17")]
    [InlineData("mentions", Samples.Ada)]
    public async Task Should_refuse_what_a_webhook_cannot_do(string key, string value)
    {
        using var fixture = Create();

        var error = await fixture.ErrorAsync("chat_send", ("text", "x"), (key, key == "mentions" ? ToolResults.Of(value) : value));

        Assert.Contains($"`{key}`", error, StringComparison.Ordinal);
        Assert.Contains("is a webhook", error, StringComparison.Ordinal);
        Assert.Empty(fixture.Hook.Requests);
    }

    [Fact]
    public async Task Should_measure_the_payload_that_is_sent()
    {
        var overhead = TeamsWebhookProvider.Payload("", markdown: true).Length;
        var room = ChatDefaults.MaxWebhookBytes - overhead;
        Assert.Equal(28 * 1024, TeamsWebhookProvider.Payload(new string('a', room), markdown: true).Length);
        Assert.Equal(28 * 1024, TeamsWebhookProvider.Payload(new string('é', room / 2) + new string('a', room % 2), markdown: true).Length);

        using var fixture = Create(maxPerHour: 1);
        fixture.Hook.EnqueueJson("{}");
        // A quote is sent as two bytes: the text alone would fit.
        Assert.Contains("over 28 KB", await fixture.ErrorAsync("chat_send", ("text", new string('"', (room / 2) + 1))), StringComparison.Ordinal);

        Assert.Empty(fixture.Hook.Requests);
        // What was refused took nothing from the cap.
        await fixture.ResultAsync("chat_send", ("text", "x"));
    }

    [Fact]
    public async Task Should_cap_the_attempts_of_an_hour()
    {
        using var fixture = Create(maxPerHour: 1);
        fixture.Hook.Enqueue(new FakeHttpResponse(HttpStatusCode.InternalServerError, ""));
        await fixture.ErrorAsync("chat_send", ("text", "x"));

        Assert.Contains("Send:MaxPerHour", await fixture.ErrorAsync("chat_send", ("text", "x")), StringComparison.Ordinal);
        Assert.Single(fixture.Hook.Requests);
    }

    [Theory]
    [InlineData(400, "InvalidRequest", false)]
    [InlineData(413, "InvalidRequest", false)]
    [InlineData(401, "InvalidConfiguration", false)]
    [InlineData(404, "InvalidConfiguration", false)]
    [InlineData(302, "InvalidConfiguration", false)]
    [InlineData(429, "Throttled", false)]
    [InlineData(408, "ServerError", true)]
    [InlineData(500, "ServerError", true)]
    [InlineData(504, "ServerError", true)]
    public async Task Should_try_once_and_say_what_is_known(int status, string code, bool doubt)
    {
        using var fixture = Create();
        fixture.Hook.Enqueue(new FakeHttpResponse((HttpStatusCode)status, "see " + Address, RetryAfter: TimeSpan.FromSeconds(7), Location: new Uri("https://elsewhere.example.org/")));

        var refusal = await Assert.ThrowsAsync<ChatToolException>(() => Provider(fixture).PostAsync([1], Token));
        var error = refusal.ToString();

        Assert.Equal(code, refusal.Code.ToString());
        Assert.Equal(doubt, error.Contains(Doubt, StringComparison.Ordinal));
        Assert.Equal(status == 429, error.Contains("Retry in 7 s", StringComparison.Ordinal));
        AssertNothingLeaks(error);
        Assert.Single(fixture.Hook.Requests);
    }

    [Theory]
    [InlineData(HttpRequestError.ConnectionError, false)]
    [InlineData(HttpRequestError.NameResolutionError, false)]
    [InlineData(HttpRequestError.SecureConnectionError, false)]
    [InlineData(HttpRequestError.ResponseEnded, true)]
    [InlineData(HttpRequestError.Unknown, true)]
    public async Task Should_doubt_a_failure_after_which_the_message_may_be_there(HttpRequestError cause, bool doubt)
    {
        using var fixture = Create();
        fixture.Hook.Enqueue(_ => throw new HttpRequestException(cause, "cannot reach " + Address));

        var error = await fixture.ErrorAsync("chat_send", ("text", "x"));

        Assert.Equal(doubt, error.Contains(Doubt, StringComparison.Ordinal));
        AssertNothingLeaks(error);
        Assert.Single(fixture.Hook.Requests);
    }

    [Fact]
    public async Task Should_doubt_a_post_that_is_not_answered_in_time()
    {
        using var fixture = Create();
        fixture.Hook.Enqueue(_ => throw new TaskCanceledException("timeout of " + Address));

        var error = await fixture.ErrorAsync("chat_send", ("text", "x"));

        Assert.Contains(Doubt, error, StringComparison.Ordinal);
        AssertNothingLeaks(error);
        Assert.Single(fixture.Hook.Requests);
    }

    [Fact]
    public async Task Should_carry_no_cause_that_quotes_the_address()
    {
        using var fixture = Create();
        fixture.Hook.Enqueue(_ => throw new HttpRequestException(HttpRequestError.Unknown, Address));

        var error = await Assert.ThrowsAsync<ChatToolException>(() => Provider(fixture).PostAsync([1], Token));

        Assert.Null(error.InnerException);
        AssertNothingLeaks(error.ToString());
    }

    [Theory]
    [InlineData(null, "is not set")]
    [InlineData(" ", "is not set")]
    [InlineData("http://hooks.example.org/?sig=" + Secret, "is not an https address")]
    [InlineData("https://user:" + Secret + "@hooks.example.org/", "is not an https address")]
    [InlineData(Secret, "is not an https address")]
    [InlineData("/" + Secret, "is not an https address")]
    public async Task Should_read_the_address_from_the_variable_alone(string? value, string problem)
    {
        using var fixture = Create();
        fixture.Variables.Remove(TestChatAccounts.WebhookVariable);
        if (value is not null)
            fixture.Variables[TestChatAccounts.WebhookVariable] = value;

        var error = await fixture.ErrorAsync("chat_send", ("text", "x"));
        var status = Assert.Single(await fixture.Administration.ListAsync(Token));
        var check = await fixture.Administration.CheckAsync("work", Token);

        foreach (var text in new[] { error, status.Problem!, Assert.Single(Assert.Single(check.Rights).Missing) })
        {
            Assert.Contains(problem, text, StringComparison.Ordinal);
            Assert.Contains(TestChatAccounts.WebhookVariable, text, StringComparison.Ordinal);
            AssertNothingLeaks(text);
        }

        Assert.False(status.Ready);
        Assert.False(check.Ready);
        Assert.Empty(fixture.Hook.Requests);
    }

    [Fact]
    public async Task Should_count_nothing_while_the_variable_holds_no_address()
    {
        using var fixture = Create(maxPerHour: 1);
        fixture.Variables.Remove(TestChatAccounts.WebhookVariable);

        for (var attempt = 0; attempt < 2; attempt++)
            Assert.Contains(TestChatAccounts.WebhookVariable, await fixture.ErrorAsync("chat_send", ("text", "x")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_read_a_changed_address_at_the_next_post()
    {
        using var fixture = Create();
        fixture.Hook.EnqueueJson("{}");
        fixture.Variables[TestChatAccounts.WebhookVariable] = "https://new.example.org/hook";

        await fixture.ResultAsync("chat_send", ("text", "x"));

        Assert.Equal("new.example.org", Assert.Single(fixture.Hook.Requests).Uri.Host);
    }

    [Fact]
    public async Task Should_list_check_and_sign_out_without_a_request()
    {
        using var fixture = Create();

        var status = Assert.Single(await fixture.Administration.ListAsync(Token));
        var check = await fixture.Administration.CheckAsync("work", Token);
        var logout = await fixture.Administration.LogoutAsync("work", Token);
        var login = await Assert.ThrowsAsync<ChatToolException>(
            () => fixture.Administration.LoginAsync("work", deviceCode: true, new FakeOAuthLoginInteraction(), Token));

        Assert.True(status.Ready);
        Assert.Equal(("TeamsWebhook", "Send", 0, 1), (status.Provider, status.Rights, status.ReadPlaces, status.SendPlaces));
        Assert.Null(status.Cloud);
        Assert.Null(status.TokenEndpoint);
        Assert.True(check.Ready);
        Assert.False(check.Contacted);
        Assert.Contains("nothing was sent", Assert.Single(Assert.Single(check.Rights).Missing), StringComparison.Ordinal);
        Assert.False(logout.Deleted);
        Assert.Equal(ChatErrorCode.NotSupportedByAccount, login.Code);
        Assert.Empty(fixture.Hook.Requests);
    }

    [Theory]
    [InlineData("chat_list")]
    [InlineData("chat_read")]
    [InlineData("chat_members")]
    public async Task Should_only_post(string tool)
    {
        using var fixture = Create();

        await fixture.ErrorAsync(tool, ("place", "chat:" + Samples.Group));

        Assert.Empty(fixture.Hook.Requests);
        Assert.Equal(ChatErrorCode.NotSupportedByAccount,
            (await Assert.ThrowsAsync<ChatToolException>(() => Provider(fixture).GetSelfAsync(Token))).Code);
    }

    private static TeamsWebhookProvider Provider(ChatFixture fixture) => Assert.IsType<TeamsWebhookProvider>(fixture.Session.Provider);

    private static void AssertNothingLeaks(string text)
    {
        Assert.DoesNotContain(Secret, text, StringComparison.Ordinal);
        Assert.DoesNotContain("hooks.example.org", text, StringComparison.Ordinal);
        Assert.DoesNotContain("elsewhere", text, StringComparison.Ordinal);
    }

    private static ChatFixture Create(int? maxPerHour = null)
    {
        var fixture = new ChatFixture(TestChatAccounts.Webhook(maxPerHour));
        fixture.Variables[TestChatAccounts.WebhookVariable] = Address;
        return fixture;
    }
}
