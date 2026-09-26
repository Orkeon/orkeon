using System.Net;
using System.Security.Cryptography;
using System.Text;
using Orkeon.Tools.Email.Auth;
using Orkeon.Tools.Email.Configuration;
using Orkeon.Tools.Email.Tests.Doubles;
using Orkeon.Tools.Email.Tests.Fixtures;

namespace Orkeon.Tools.Email.Tests.Auth;

/// <summary>The hand-written OAuth 2.0 client: device code, PKCE authorization, refresh.</summary>
public sealed class OAuth2ClientTests : IDisposable
{
    private readonly FakeHttpMessageHandler _handler = new();
    private readonly HttpClient _http;
    private readonly FakeTimeProvider _time = new();
    private readonly List<TimeSpan> _waits = [];
    private readonly OAuth2Client _client;

    public OAuth2ClientTests()
    {
        _http = new HttpClient(_handler, disposeHandler: false);
        _client = new OAuth2Client(_http, _time, (wait, _) =>
        {
            _waits.Add(wait);
            _time.Advance(wait);
            return Task.CompletedTask;
        });
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static OAuthSettings Microsoft => TestAccounts.Resolve("hotmail", TestAccounts.Outlook()).Auth.OAuth!;

    private static OAuthSettings Google => TestAccounts.Resolve("perso", TestAccounts.GmailOAuth()).Auth.OAuth!;

    [Fact]
    public async Task Should_start_a_device_authorization_with_the_client_and_its_scopes()
    {
        _handler.EnqueueJson("""{"device_code":"dev-123","user_code":"ABCD-EFGH","verification_uri":"https://microsoft.com/devicelogin","expires_in":900,"interval":5}""");

        var grant = await _client.RequestDeviceCodeAsync(Microsoft, Token);

        Assert.Equal(new DeviceCodeGrant("dev-123", "ABCD-EFGH", new Uri("https://microsoft.com/devicelogin"), TimeSpan.FromMinutes(15), TimeSpan.FromSeconds(5)), grant);
        var request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(Microsoft.DeviceCodeEndpoint, request.Uri);
        Assert.Equal("application/x-www-form-urlencoded", request.ContentType);
        Assert.Equal("00000000-1111-2222-3333-444444444444", request.Form["client_id"]);
        Assert.Equal("https://graph.microsoft.com/Mail.ReadWrite https://graph.microsoft.com/Mail.Send offline_access", request.Form["scope"]);
    }

    [Fact]
    public async Task Should_accept_the_verification_url_spelling_and_default_or_floor_the_timings()
    {
        _handler.EnqueueJson("""{"device_code":"d","user_code":"u","verification_url":"https://www.google.com/device","interval":0}""");

        var grant = await _client.RequestDeviceCodeAsync(Microsoft, Token);

        Assert.Equal(new Uri("https://www.google.com/device"), grant.VerificationUri);
        Assert.Equal(TimeSpan.FromSeconds(900), grant.ExpiresIn);
        Assert.Equal(TimeSpan.FromSeconds(1), grant.Interval);
    }

    [Fact]
    public async Task Should_report_a_refused_device_authorization()
    {
        _handler.EnqueueJson("""{"error":"invalid_client","error_description":"AADSTS700016: Application not found."}""");

        var error = await Assert.ThrowsAsync<EmailToolException>(async () => await _client.RequestDeviceCodeAsync(Microsoft, Token));

        Assert.Equal(EmailErrorCode.AuthenticationFailed, error.Code);
        Assert.Equal("The device authorization was refused: invalid_client AADSTS700016: Application not found.", error.Message);
    }

    [Fact]
    public async Task Should_report_a_device_authorization_answer_missing_its_codes()
    {
        _handler.EnqueueJson("""{"device_code":"d","verification_uri":"https://microsoft.com/devicelogin"}""");

        var error = await Assert.ThrowsAsync<EmailToolException>(async () => await _client.RequestDeviceCodeAsync(Microsoft, Token));

        Assert.Equal(EmailErrorCode.ServerError, error.Code);
    }

    [Fact]
    public async Task Should_refuse_a_device_authorization_for_a_provider_without_that_endpoint()
    {
        var error = await Assert.ThrowsAsync<EmailToolException>(async () => await _client.RequestDeviceCodeAsync(Google, Token));

        Assert.Equal(EmailErrorCode.InvalidConfiguration, error.Code);
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task Should_poll_through_pending_and_slow_down_until_the_user_signs_in()
    {
        _handler.EnqueueJson("""{"error":"authorization_pending"}""");
        _handler.EnqueueJson("""{"error":"slow_down"}""");
        _handler.EnqueueJson("""{"access_token":"at-1","refresh_token":"rt-1","expires_in":3599,"scope":"Mail.ReadWrite offline_access"}""");
        var grant = new DeviceCodeGrant("dev-123", "ABCD", new Uri("https://microsoft.com/devicelogin"), TimeSpan.FromMinutes(15), TimeSpan.FromSeconds(5));

        var tokens = await _client.PollDeviceCodeAsync(Microsoft, clientSecret: null, grant, Token);

        Assert.Equal([TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)], _waits);
        Assert.Equal("at-1", tokens.AccessToken);
        Assert.Equal("rt-1", tokens.RefreshToken);
        Assert.Equal(_time.GetUtcNow().AddSeconds(3599), tokens.ExpiresAt);
        Assert.Equal(["Mail.ReadWrite", "offline_access"], tokens.Scopes);
        Assert.All(_handler.Requests, request =>
        {
            Assert.Equal(Microsoft.TokenEndpoint, request.Uri);
            Assert.Equal("urn:ietf:params:oauth:grant-type:device_code", request.Form["grant_type"]);
            Assert.Equal("dev-123", request.Form["device_code"]);
            Assert.Equal("00000000-1111-2222-3333-444444444444", request.Form["client_id"]);
            Assert.False(request.Form.ContainsKey("client_secret"));
        });
    }

    [Fact]
    public async Task Should_send_the_client_secret_only_when_there_is_one()
    {
        _handler.EnqueueJson("""{"access_token":"at","refresh_token":"rt"}""");
        var grant = new DeviceCodeGrant("d", "u", new Uri("https://microsoft.com/devicelogin"), TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1));

        var tokens = await _client.PollDeviceCodeAsync(Microsoft, "s3cret", grant, Token);

        Assert.Equal("s3cret", Assert.Single(_handler.Requests).Form["client_secret"]);
        Assert.Equal(Microsoft.Scopes, tokens.Scopes);
        Assert.Equal(_time.GetUtcNow().AddSeconds(3600), tokens.ExpiresAt);
    }

    [Theory]
    [InlineData("expired_token")]
    [InlineData("access_denied")]
    public async Task Should_stop_polling_When_the_sign_in_expires_or_is_declined(string error)
    {
        _handler.EnqueueJson($$"""{"error":"{{error}}","error_description":"The user said no."}""");
        var grant = new DeviceCodeGrant("d", "u", new Uri("https://microsoft.com/devicelogin"), TimeSpan.FromMinutes(15), TimeSpan.FromSeconds(5));

        var failure = await Assert.ThrowsAsync<EmailToolException>(async () => await _client.PollDeviceCodeAsync(Microsoft, null, grant, Token));

        Assert.Equal(EmailErrorCode.LoginRequired, failure.Code);
        Assert.Equal($"The sign-in did not complete: {error} The user said no.", failure.Message);
    }

    [Fact]
    public async Task Should_give_up_When_the_code_expires_while_the_user_is_still_away()
    {
        _handler.EnqueueJson("""{"error":"authorization_pending"}""");
        _handler.EnqueueJson("""{"error":"authorization_pending"}""");
        var grant = new DeviceCodeGrant("d", "u", new Uri("https://microsoft.com/devicelogin"), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5));

        var failure = await Assert.ThrowsAsync<EmailToolException>(async () => await _client.PollDeviceCodeAsync(Microsoft, null, grant, Token));

        Assert.Equal(EmailErrorCode.LoginRequired, failure.Code);
        Assert.Equal("The sign-in code expired before the sign-in completed; run the login again.", failure.Message);
        Assert.Equal(2, _handler.Requests.Count);
    }

    [Fact]
    public async Task Should_keep_the_refresh_token_When_the_provider_does_not_rotate_it()
    {
        _handler.EnqueueJson("""{"access_token":"at-2","expires_in":"1800"}""");

        var tokens = await _client.RefreshAsync(Google, "google-secret", "rt-old", Token);

        Assert.Equal("at-2", tokens.AccessToken);
        Assert.Equal("rt-old", tokens.RefreshToken);
        Assert.Equal(_time.GetUtcNow().AddSeconds(1800), tokens.ExpiresAt);
        var request = Assert.Single(_handler.Requests);
        Assert.Equal(new Uri("https://oauth2.googleapis.com/token"), request.Uri);
        Assert.Equal("refresh_token", request.Form["grant_type"]);
        Assert.Equal("rt-old", request.Form["refresh_token"]);
        Assert.False(request.Form.ContainsKey("scope"));
        Assert.Equal("google-secret", request.Form["client_secret"]);
    }

    [Fact]
    public async Task Should_repeat_the_scopes_on_a_Microsoft_refresh_only()
    {
        _handler.EnqueueJson("""{"access_token":"at","expires_in":3600}""");

        await _client.RefreshAsync(Microsoft, null, "rt", Token);

        Assert.True(Microsoft.ScopesOnRefresh);
        Assert.False(Google.ScopesOnRefresh);
        Assert.Equal(
            "https://graph.microsoft.com/Mail.ReadWrite https://graph.microsoft.com/Mail.Send offline_access",
            Assert.Single(_handler.Requests).Form["scope"]);
    }

    [Fact]
    public async Task Should_take_a_rotated_refresh_token()
    {
        _handler.EnqueueJson("""{"access_token":"at-3","refresh_token":"rt-new","expires_in":3600}""");

        var tokens = await _client.RefreshAsync(Microsoft, null, "rt-old", Token);

        Assert.Equal("rt-new", tokens.RefreshToken);
    }

    [Theory]
    [InlineData("invalid_grant", "LoginRequired")]
    [InlineData("interaction_required", "LoginRequired")]
    [InlineData("invalid_client", "LoginRequired")]
    [InlineData("unauthorized_client", "LoginRequired")]
    [InlineData("temporarily_unavailable", "AuthenticationFailed")]
    public async Task Should_ask_for_a_new_sign_in_only_When_the_grant_itself_is_refused(string error, string code)
    {
        _handler.Enqueue(new FakeHttpResponse(HttpStatusCode.BadRequest, $$"""{"error":"{{error}}","error_description":"AADSTS70008"}"""));

        var failure = await Assert.ThrowsAsync<EmailToolException>(async () => await _client.RefreshAsync(Microsoft, null, "rt", Token));

        Assert.Equal(Enum.Parse<EmailErrorCode>(code), failure.Code);
        Assert.Equal($"The token refresh was refused: {error} AADSTS70008", failure.Message);
    }

    [Fact]
    public async Task Should_report_an_answer_that_is_not_JSON()
    {
        _handler.Enqueue(new FakeHttpResponse(HttpStatusCode.BadGateway, "<html>Bad gateway</html>", "text/html"));

        var failure = await Assert.ThrowsAsync<EmailToolException>(async () => await _client.RefreshAsync(Microsoft, null, "rt", Token));

        Assert.Equal(EmailErrorCode.ServerError, failure.Code);
        Assert.Equal("The identity provider at login.microsoftonline.com answered HTTP 502 without JSON.", failure.Message);
    }

    [Fact]
    public async Task Should_report_a_token_answer_without_an_access_token()
    {
        _handler.Enqueue(new FakeHttpResponse(HttpStatusCode.OK, string.Empty));

        var failure = await Assert.ThrowsAsync<EmailToolException>(async () => await _client.RefreshAsync(Microsoft, null, "rt", Token));

        Assert.Equal(EmailErrorCode.ServerError, failure.Code);
        Assert.Equal("The token response has no access token.", failure.Message);
    }

    [Fact]
    public async Task Should_report_an_identity_provider_that_cannot_be_reached()
    {
        _handler.Enqueue(_ => throw new HttpRequestException("Name or service not known"));

        var failure = await Assert.ThrowsAsync<EmailToolException>(async () => await _client.RefreshAsync(Google, null, "rt", Token));

        Assert.Equal(EmailErrorCode.ServerError, failure.Code);
        Assert.Equal("The identity provider at oauth2.googleapis.com could not be reached: Name or service not known", failure.Message);
    }

    [Fact]
    public async Task Should_report_an_identity_provider_that_does_not_answer_in_time_as_a_server_error()
    {
        _handler.Enqueue(_ => throw new TaskCanceledException("timeout", new TimeoutException()));

        var failure = await Assert.ThrowsAsync<EmailToolException>(async () => await _client.RefreshAsync(Google, null, "rt", Token));

        Assert.Equal(EmailErrorCode.ServerError, failure.Code);
        Assert.Equal("The identity provider at oauth2.googleapis.com did not answer in time; retry later.", failure.Message);
    }

    [Fact]
    public async Task Should_exchange_an_authorization_code_with_the_PKCE_verifier()
    {
        _handler.EnqueueJson("""{"access_token":"at","refresh_token":"rt","expires_in":3600}""");
        var session = PkceSession.Create();
        var redirect = new Uri("http://127.0.0.1:50123/");

        var tokens = await _client.ExchangeCodeAsync(Google, "google-secret", "4/0AbCd", redirect, session, Token);

        Assert.Equal("rt", tokens.RefreshToken);
        var form = Assert.Single(_handler.Requests).Form;
        Assert.Equal("authorization_code", form["grant_type"]);
        Assert.Equal("4/0AbCd", form["code"]);
        Assert.Equal("http://127.0.0.1:50123/", form["redirect_uri"]);
        Assert.Equal(session.Verifier, form["code_verifier"]);
        Assert.Equal("google-client.apps.googleusercontent.com", form["client_id"]);
        Assert.Equal("google-secret", form["client_secret"]);
    }

    [Fact]
    public async Task Should_report_a_refused_authorization_code()
    {
        _handler.Enqueue(new FakeHttpResponse(HttpStatusCode.BadRequest, """{"error":"invalid_grant","error_description":"Bad Request"}"""));

        var failure = await Assert.ThrowsAsync<EmailToolException>(async () =>
            await _client.ExchangeCodeAsync(Google, null, "code", new Uri("http://127.0.0.1:1/"), PkceSession.Create(), Token));

        Assert.Equal(EmailErrorCode.AuthenticationFailed, failure.Code);
        Assert.Equal("The authorization code was refused: invalid_grant Bad Request", failure.Message);
    }

    [Fact]
    public void Should_build_the_authorization_address_with_an_S256_challenge_and_offline_access()
    {
        var session = PkceSession.Create();

        var uri = OAuth2Client.BuildAuthorizationUri(Google, new Uri("http://127.0.0.1:50123/"), session, "someone@gmail.com");

        Assert.StartsWith("https://accounts.google.com/o/oauth2/v2/auth?", uri.AbsoluteUri, StringComparison.Ordinal);
        var query = AuthorizationQuery(uri);
        Assert.Equal("google-client.apps.googleusercontent.com", query["client_id"]);
        Assert.Equal("http://127.0.0.1:50123/", query["redirect_uri"]);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("https://mail.google.com/", query["scope"]);
        Assert.Equal(session.Challenge, query["code_challenge"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal(session.State, query["state"]);
        Assert.Equal("offline", query["access_type"]);
        Assert.Equal("consent", query["prompt"]);
        Assert.Equal("someone@gmail.com", query["login_hint"]);
    }

    [Fact]
    public void Should_leave_out_an_empty_login_hint_and_refuse_a_provider_without_an_authorization_endpoint()
    {
        var uri = OAuth2Client.BuildAuthorizationUri(Google, new Uri("http://127.0.0.1:1/"), PkceSession.Create(), " ");
        var settings = Google with { AuthorizationEndpoint = null };

        Assert.False(AuthorizationQuery(uri).ContainsKey("login_hint"));
        Assert.Equal(EmailErrorCode.InvalidConfiguration, Assert.Throws<EmailToolException>(() =>
            OAuth2Client.BuildAuthorizationUri(settings, new Uri("http://127.0.0.1:1/"), PkceSession.Create(), null)).Code);
    }

    [Fact]
    public void Should_derive_the_challenge_as_the_base64url_SHA256_of_a_random_verifier()
    {
        var session = PkceSession.Create();
        var other = PkceSession.Create();

        Assert.Equal(43, session.Verifier.Length);
        Assert.Equal(22, session.State.Length);
        Assert.All(session.Verifier + session.Challenge + session.State, c => Assert.True(char.IsAsciiLetterOrDigit(c) || c is '-' or '_'));
        var expected = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(session.Verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.Equal(expected, session.Challenge);
        Assert.NotEqual(session.Verifier, other.Verifier);
        Assert.NotEqual(session.State, other.State);
    }

    [Theory]
    [InlineData(180, true)]
    [InlineData(121, true)]
    [InlineData(120, false)]
    [InlineData(60, false)]
    [InlineData(-60, false)]
    public void Should_call_a_token_fresh_only_When_it_outlives_the_two_minute_margin(int secondsLeft, bool fresh)
    {
        var tokens = new EmailTokenSet { AccessToken = "at", ExpiresAt = _time.GetUtcNow().AddSeconds(secondsLeft) };

        Assert.Equal(fresh, _client.IsFresh(tokens));
    }

    [Fact]
    public void Should_parse_the_outcome_of_an_authorization_redirect()
    {
        var success = AuthorizationRedirect.Parse(new Uri("http://127.0.0.1:1/?state=s%20t&code=4%2F0Ab+c&bare&=x"));
        var refused = AuthorizationRedirect.Parse(new Uri("http://127.0.0.1:1/?error=access_denied&state=s"));
        var nothing = AuthorizationRedirect.Parse(new Uri("http://127.0.0.1:1/favicon.ico"));

        Assert.Equal(new AuthorizationRedirect("4/0Ab c", "s t", null), success);
        Assert.True(success.IsOutcome);
        Assert.Equal("access_denied", refused.Error);
        Assert.True(refused.IsOutcome);
        Assert.False(nothing.IsOutcome);
    }

    public void Dispose()
    {
        _http.Dispose();
        _handler.Dispose();
        GC.SuppressFinalize(this);
    }

    private static Dictionary<string, string> AuthorizationQuery(Uri uri) =>
        uri.Query.TrimStart('?').Split('&')
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => Uri.UnescapeDataString(pair[1]), StringComparer.Ordinal);
}
