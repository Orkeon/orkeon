using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MimeKit;
using Orkeon.Tools.Email.Auth;
using Orkeon.Tools.Email.Configuration;
using Orkeon.Tools.Email.Constants;

namespace Orkeon.Tools.Email.Mailboxes.Graph;

/// <summary>
/// Microsoft Graph over plain HTTP for one account: bearer token from the credential provider,
/// immutable ids on every request (an id survives a move), Graph errors turned into actionable
/// messages. A paging link is followed only if it still points at Graph, so the token never
/// leaves for another host.
/// </summary>
internal sealed class GraphClient
{
    private readonly HttpClient _http;
    private readonly ResolvedEmailAccount _account;
    private readonly EmailCredentialProvider _credentials;

    /// <summary>Creates the client of <paramref name="account"/>.</summary>
    public GraphClient(HttpClient http, ResolvedEmailAccount account, EmailCredentialProvider credentials)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(credentials);
        _http = http;
        _account = account;
        _credentials = credentials;
    }

    /// <summary>The address of <paramref name="relative"/> under the Graph base.</summary>
    public static Uri Resolve(string relative) => new(EmailDefaults.GraphBaseUri, relative);

    /// <summary>Whether <paramref name="link"/> is a Graph address the token may be sent to.</summary>
    public static bool IsGraphAddress(Uri link) =>
        link.IsAbsoluteUri
        && link.Scheme == Uri.UriSchemeHttps
        && string.Equals(link.Host, EmailDefaults.GraphHost, StringComparison.OrdinalIgnoreCase);

    /// <summary>GETs JSON.</summary>
    public async Task<JsonDocument> GetJsonAsync(Uri address, CancellationToken cancellationToken)
    {
        using var request = await CreateAsync(HttpMethod.Get, address, cancellationToken).ConfigureAwait(false);
        return await SendForJsonAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Sends a JSON body; returns the response JSON (empty object when none).</summary>
    public async Task<JsonDocument> SendJsonAsync(HttpMethod method, Uri address, JsonNode? body, CancellationToken cancellationToken)
    {
        using var request = await CreateAsync(method, address, cancellationToken).ConfigureAwait(false);
        if (body is not null)
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        return await SendForJsonAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>POSTs a MIME message, base64-encoded as Graph wants it (sendMail, draft creation).</summary>
    public async Task<JsonDocument> PostMimeAsync(Uri address, MimeMessage message, CancellationToken cancellationToken)
    {
        string payload;
        using (var buffer = new MemoryStream())
        {
            await message.WriteToAsync(buffer, cancellationToken).ConfigureAwait(false);
            payload = Convert.ToBase64String(buffer.GetBuffer(), 0, (int)buffer.Length);
        }

        if (payload.Length > EmailDefaults.GraphMaxRequestBytes)
        {
            throw new EmailToolException(
                EmailErrorCode.TooLarge,
                string.Create(CultureInfo.InvariantCulture,
                    $"The message is {payload.Length / 1024} KB once encoded; Microsoft Graph accepts at most {EmailDefaults.GraphMaxRequestBytes / 1024} KB per request (about 3 MB of attachments)."));
        }

        using var request = await CreateAsync(HttpMethod.Post, address, cancellationToken).ConfigureAwait(false);
        request.Content = new StringContent(payload, Encoding.ASCII, "text/plain");
        return await SendForJsonAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>GETs a MIME message (<c>$value</c>).</summary>
    public async Task<MimeMessage> GetMimeAsync(Uri address, CancellationToken cancellationToken)
    {
        using var request = await CreateAsync(HttpMethod.Get, address, cancellationToken).ConfigureAwait(false);
        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            return await MimeMessage.LoadAsync(stream, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<HttpRequestMessage> CreateAsync(HttpMethod method, Uri address, CancellationToken cancellationToken)
    {
        if (!IsGraphAddress(address))
            throw new EmailToolException(EmailErrorCode.InvalidRequest, "Refusing to send the account's token to an address outside Microsoft Graph.");

        var token = await _credentials.GetAccessTokenAsync(_account, cancellationToken).ConfigureAwait(false);
        var request = new HttpRequestMessage(method, address);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.TryAddWithoutValidation("Prefer", "IdType=\"ImmutableId\"");
        return request;
    }

    private async Task<JsonDocument> SendForJsonAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new EmailToolException(EmailErrorCode.ServerError, $"Microsoft Graph could not be reached: {ex.Message}", ex);
        }

        if (response.IsSuccessStatusCode)
            return response;

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw Describe(response, body);
        }
    }

    private EmailToolException Describe(HttpResponseMessage response, string body)
    {
        var (code, message) = ReadError(body);
        var detail = string.IsNullOrEmpty(code) ? message : $"{code}: {message}";
        return response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => new EmailToolException(
                EmailErrorCode.AuthenticationFailed,
                $"Microsoft Graph refused the token of e-mail account '{_account.Name}': run `orkeon email login {_account.Name}` again ({detail})."),
            HttpStatusCode.Forbidden => new EmailToolException(
                EmailErrorCode.AuthenticationFailed,
                $"Microsoft Graph denied the operation for account '{_account.Name}': check that the application holds the delegated permissions Mail.ReadWrite and Mail.Send ({detail})."),
            HttpStatusCode.NotFound => new EmailToolException(
                EmailErrorCode.MessageNotFound,
                $"Microsoft Graph found no such item (moved or deleted): search again ({detail})."),
            HttpStatusCode.RequestEntityTooLarge => new EmailToolException(
                EmailErrorCode.TooLarge,
                $"Microsoft Graph refused the request as too large ({detail})."),
            HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout => new EmailToolException(
                EmailErrorCode.ServerError,
                $"Microsoft Graph is throttling or unavailable; retry {RetryHint(response)} ({detail})."),
            _ => new EmailToolException(
                EmailErrorCode.ServerError,
                string.Create(CultureInfo.InvariantCulture, $"Microsoft Graph answered HTTP {(int)response.StatusCode} ({detail}).")),
        };
    }

    private static string RetryHint(HttpResponseMessage response) =>
        response.Headers.RetryAfter?.Delta is { } delay
            ? string.Create(CultureInfo.InvariantCulture, $"in {Math.Ceiling(delay.TotalSeconds)} s")
            : "later";

    private static (string Code, string Message) ReadError(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
            {
                var code = error.TryGetProperty("code", out var c) ? c.GetString() ?? string.Empty : string.Empty;
                var message = error.TryGetProperty("message", out var m) ? m.GetString() ?? string.Empty : string.Empty;
                return (code, message);
            }
        }
        catch (JsonException)
        {
            // Not JSON: fall through to the raw text.
        }

        return (string.Empty, body.Length > 200 ? body[..200] : body);
    }
}
