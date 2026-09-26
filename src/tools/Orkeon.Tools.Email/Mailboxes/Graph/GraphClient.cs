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
    public Task<JsonDocument> GetJsonAsync(Uri address, CancellationToken cancellationToken) =>
        SendForJsonAsync(HttpMethod.Get, address, () => null, cancellationToken);

    /// <summary>Sends a JSON body; returns the response JSON (empty object when none).</summary>
    public Task<JsonDocument> SendJsonAsync(HttpMethod method, Uri address, JsonNode? body, CancellationToken cancellationToken)
    {
        var json = body?.ToJsonString();
        return SendForJsonAsync(method, address, () => json is null ? null : new StringContent(json, Encoding.UTF8, "application/json"), cancellationToken);
    }

    /// <summary>POSTs a MIME message, base64-encoded as Graph wants it (sendMail, draft creation).</summary>
    public async Task<JsonDocument> PostMimeAsync(Uri address, MimeMessage message, CancellationToken cancellationToken)
    {
        string payload;
        using (var buffer = new MemoryStream())
        {
            await message.WriteToAsync(MessageSizes.Wire, buffer, cancellationToken).ConfigureAwait(false);

            // Measured before encoding: an oversized message is refused without its base64 copy.
            var encodedLength = (buffer.Length + 2) / 3 * 4;
            if (encodedLength > EmailDefaults.GraphMaxRequestBytes)
            {
                throw new EmailToolException(
                    EmailErrorCode.TooLarge,
                    string.Create(CultureInfo.InvariantCulture,
                        $"The message is {encodedLength / 1024} KB once encoded; Microsoft Graph accepts at most {EmailDefaults.GraphMaxRequestBytes / 1024} KB per request (about 3 MB of attachments)."));
            }

            payload = Convert.ToBase64String(buffer.GetBuffer(), 0, (int)buffer.Length);
        }

        return await SendForJsonAsync(HttpMethod.Post, address, () => new StringContent(payload, Encoding.ASCII, "text/plain"), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>GETs a MIME message (<c>$value</c>).</summary>
    public async Task<MimeMessage> GetMimeAsync(Uri address, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, address, () => null, cancellationToken).ConfigureAwait(false);
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            return await MimeMessage.LoadAsync(stream, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<JsonDocument> SendForJsonAsync(HttpMethod method, Uri address, Func<HttpContent?> content, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(method, address, content, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
    }

    /// <summary>
    /// Sends one request with the account's token, building it with <paramref name="content"/>.
    /// A 401 is answered once more with another token: the refused one may have been revoked,
    /// or replaced in the store by a new sign-in, so it is read again and refreshed.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, Uri address, Func<HttpContent?> content, CancellationToken cancellationToken)
    {
        if (!IsGraphAddress(address))
            throw new EmailToolException(EmailErrorCode.InvalidRequest, "Refusing to send the account's token to an address outside Microsoft Graph.");

        string? refused = null;
        while (true)
        {
            var token = await _credentials.GetAccessTokenAsync(_account, cancellationToken, refused).ConfigureAwait(false);
            using var request = new HttpRequestMessage(method, address) { Content = content() };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.TryAddWithoutValidation("Prefer", "IdType=\"ImmutableId\"");

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                throw new EmailToolException(EmailErrorCode.ServerError, $"Microsoft Graph could not be reached: {ex.Message}", ex);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                // The HTTP client's own timeout, not a cancellation by the caller.
                throw new EmailToolException(EmailErrorCode.ServerError, "Microsoft Graph did not answer in time; retry later.", ex);
            }

            if (response.IsSuccessStatusCode)
                return response;

            if (response.StatusCode == HttpStatusCode.Unauthorized && refused is null)
            {
                response.Dispose();
                refused = token;
                continue;
            }

            using (response)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                throw Describe(response, body);
            }
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
