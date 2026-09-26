using System.Net;
using Orkeon.Tools.Email.Auth;
using Orkeon.Tools.Email.Mailboxes.Graph;
using Orkeon.Tools.Email.Tests.Doubles;
using Orkeon.Tools.Email.Tests.Fixtures;

namespace Orkeon.Tools.Email.Tests.Mailboxes.Graph;

/// <summary>The Graph HTTP client: where the token may go, and what Graph failures become.</summary>
public sealed class GraphClientTests
{
    private const string Base = GraphFixture.Base;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "AuthenticationFailed", "Microsoft Graph refused the token of e-mail account 'hotmail': run `orkeon email login hotmail` again (InvalidAuthenticationToken: Lifetime validation failed).")]
    [InlineData(HttpStatusCode.Forbidden, "AuthenticationFailed", "Microsoft Graph denied the operation for account 'hotmail': check that the application holds the delegated permissions Mail.ReadWrite and Mail.Send (ErrorAccessDenied: Access is denied.).")]
    [InlineData(HttpStatusCode.NotFound, "MessageNotFound", "Microsoft Graph found no such item (moved or deleted): search again (ErrorItemNotFound: The specified object was not found in the store.).")]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, "TooLarge", "Microsoft Graph refused the request as too large (ErrorMessageSizeExceeded: Too big.).")]
    [InlineData(HttpStatusCode.InternalServerError, "ServerError", "Microsoft Graph answered HTTP 500 (ErrorInternalServerError: Boom.).")]
    public async Task Should_turn_Graph_failures_into_actionable_errors(HttpStatusCode status, string code, string message)
    {
        using var graph = new GraphFixture();
        var (errorCode, errorMessage) = status switch
        {
            HttpStatusCode.Unauthorized => ("InvalidAuthenticationToken", "Lifetime validation failed"),
            HttpStatusCode.Forbidden => ("ErrorAccessDenied", "Access is denied."),
            HttpStatusCode.NotFound => ("ErrorItemNotFound", "The specified object was not found in the store."),
            HttpStatusCode.RequestEntityTooLarge => ("ErrorMessageSizeExceeded", "Too big."),
            _ => ("ErrorInternalServerError", "Boom."),
        };
        graph.Http.Map(HttpMethod.Get, Base, GraphFixture.Error(status, errorCode, errorMessage));

        var error = await Assert.ThrowsAsync<EmailToolException>(async () => await graph.Client.GetJsonAsync(new Uri($"{Base}me/messages/x"), Token));

        Assert.Equal((Enum.Parse<EmailErrorCode>(code), message), (error.Code, error.Message));
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, 30, "retry in 30 s")]
    [InlineData(HttpStatusCode.ServiceUnavailable, null, "retry later")]
    [InlineData(HttpStatusCode.GatewayTimeout, 5, "retry in 5 s")]
    public async Task Should_say_when_to_retry_When_Graph_throttles(HttpStatusCode status, int? retryAfter, string hint)
    {
        using var graph = new GraphFixture();
        graph.Http.Map(HttpMethod.Get, Base, GraphFixture.Error(status, "TooManyRequests", "Slow down",
            retryAfter is { } seconds ? TimeSpan.FromSeconds(seconds) : null));

        var error = await Assert.ThrowsAsync<EmailToolException>(async () => await graph.Client.GetJsonAsync(new Uri($"{Base}me/messages"), Token));

        Assert.Equal(EmailErrorCode.ServerError, error.Code);
        Assert.Equal($"Microsoft Graph is throttling or unavailable; {hint} (TooManyRequests: Slow down).", error.Message);
    }

    [Fact]
    public async Task Should_quote_a_short_excerpt_of_an_error_that_is_not_JSON()
    {
        using var graph = new GraphFixture();
        graph.Http.Map(HttpMethod.Get, Base, new FakeHttpResponse(HttpStatusCode.BadGateway, new string('x', 500), "text/html"));

        var error = await Assert.ThrowsAsync<EmailToolException>(async () => await graph.Client.GetJsonAsync(new Uri($"{Base}me"), Token));

        Assert.Equal($"Microsoft Graph answered HTTP 502 ({new string('x', 200)}).", error.Message);
    }

    [Theory]
    [InlineData("https://graph.microsoft.com/v1.0/me", true)]
    [InlineData("https://GRAPH.microsoft.com/beta/me", true)]
    [InlineData("http://graph.microsoft.com/v1.0/me", false)]
    [InlineData("https://graph.microsoft.com.evil.example/v1.0/me", false)]
    [InlineData("https://evil.example/graph.microsoft.com", false)]
    public void Should_let_the_token_go_to_Graph_over_HTTPS_only(string address, bool allowed)
    {
        Assert.Equal(allowed, GraphClient.IsGraphAddress(new Uri(address)));
    }

    [Fact]
    public async Task Should_refuse_an_address_outside_Graph_before_even_reading_the_token()
    {
        using var graph = new GraphFixture();

        var error = await Assert.ThrowsAsync<EmailToolException>(async () =>
            await graph.Client.GetJsonAsync(new Uri("https://evil.example/v1.0/me/messages"), Token));

        Assert.Equal(EmailErrorCode.InvalidRequest, error.Code);
        Assert.Equal("Refusing to send the account's token to an address outside Microsoft Graph.", error.Message);
        Assert.Empty(graph.Http.Requests);
        Assert.Equal(0, graph.Credentials.Store.Reads);
    }

    [Fact]
    public async Task Should_refuse_a_MIME_message_above_four_megabytes_once_encoded_before_sending()
    {
        using var graph = new GraphFixture();
        using var message = MimeSamples.Load(MimeSamples.Plain(body: new string('a', 3_300_000)));

        var error = await Assert.ThrowsAsync<EmailToolException>(async () =>
            await graph.Client.PostMimeAsync(GraphClient_Resolve("me/sendMail"), message, Token));

        Assert.Equal(EmailErrorCode.TooLarge, error.Code);
        Assert.Contains("Microsoft Graph accepts at most 4096 KB per request", error.Message, StringComparison.Ordinal);
        Assert.Empty(graph.Http.Requests);
    }

    [Fact]
    public async Task Should_report_Graph_unreachable()
    {
        using var graph = new GraphFixture();
        graph.Http.Enqueue(_ => throw new HttpRequestException("No such host is known."));

        var error = await Assert.ThrowsAsync<EmailToolException>(async () => await graph.Client.GetJsonAsync(new Uri($"{Base}me"), Token));

        Assert.Equal((EmailErrorCode.ServerError, "Microsoft Graph could not be reached: No such host is known."), (error.Code, error.Message));
    }

    [Fact]
    public async Task Should_report_a_Graph_timeout_as_a_server_error_not_a_cancellation()
    {
        using var graph = new GraphFixture();
        graph.Http.Enqueue(_ => throw new TaskCanceledException("timeout", new TimeoutException()));

        var error = await Assert.ThrowsAsync<EmailToolException>(async () => await graph.Client.GetJsonAsync(new Uri($"{Base}me"), Token));

        Assert.Equal((EmailErrorCode.ServerError, "Microsoft Graph did not answer in time; retry later."), (error.Code, error.Message));
    }

    [Fact]
    public async Task Should_refresh_an_expiring_token_before_calling_Graph()
    {
        using var graph = new GraphFixture();
        graph.Credentials.Store.With(EmailCredentialProvider.TokenKey(graph.Account), new EmailTokenSet
        {
            AccessToken = "stale",
            RefreshToken = "rt",
            ExpiresAt = graph.Credentials.Time.GetUtcNow().AddSeconds(30),
        });
        graph.Http.Map(HttpMethod.Post, "https://login.microsoftonline.com/", FakeHttpResponse.Json("""{"access_token":"renewed","expires_in":3600}"""));
        graph.Http.Map(HttpMethod.Get, $"{Base}me", FakeHttpResponse.Json("""{"mail":"someone@outlook.com"}"""));

        using var document = await graph.Client.GetJsonAsync(new Uri($"{Base}me"), Token);

        Assert.Equal("someone@outlook.com", document.RootElement.GetProperty("mail").GetString());
        Assert.Equal("Bearer renewed", graph.Http.Requests[^1].Authorization);
    }

    private static Uri GraphClient_Resolve(string relative) => GraphClient.Resolve(relative);
}
