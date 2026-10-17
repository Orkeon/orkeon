using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Orkeon.Tools.Drive.Auth;
using Orkeon.Tools.Drive.Configuration;

namespace Orkeon.Tools.Drive.Drives;

/// <summary>
/// One account's HTTP conversation with its provider's API: bearer token from the credential
/// provider, one more try with another token on a 401, the account's timeout, provider errors
/// turned into actionable messages. The token is only ever sent to the API's own host over
/// HTTPS; a pre-authenticated address (a download link, an upload session) is called without it.
/// </summary>
internal sealed class DriveApiClient
{
    private readonly HttpClient _http;
    private readonly ResolvedDriveAccount _account;
    private readonly DriveCredentialProvider _credentials;
    private readonly string _apiHost;
    private readonly string _apiName;

    /// <summary>Creates the client of <paramref name="account"/>.</summary>
    /// <param name="http">The HTTP client.</param>
    /// <param name="account">The account.</param>
    /// <param name="credentials">Where its token comes from.</param>
    /// <param name="apiHost">The one host the token may be sent to.</param>
    /// <param name="apiName">How messages name the API ("Google Drive", "Microsoft Graph").</param>
    public DriveApiClient(HttpClient http, ResolvedDriveAccount account, DriveCredentialProvider credentials, string apiHost, string apiName)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(credentials);
        _http = http;
        _account = account;
        _credentials = credentials;
        _apiHost = apiHost;
        _apiName = apiName;
    }

    /// <summary>The account.</summary>
    public ResolvedDriveAccount Account => _account;

    /// <summary>Whether <paramref name="address"/> is an address of the API the token may be sent to.</summary>
    public bool IsApiAddress(Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);
        return address.IsAbsoluteUri
            && address.Scheme == Uri.UriSchemeHttps
            && string.Equals(address.Host, _apiHost, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>GETs JSON.</summary>
    public Task<JsonDocument> GetJsonAsync(Uri address, CancellationToken cancellationToken) =>
        SendForJsonAsync(HttpMethod.Get, address, () => null, cancellationToken);

    /// <summary>Sends a JSON body (or none); returns the response JSON (an empty object when none).</summary>
    public Task<JsonDocument> SendJsonAsync(HttpMethod method, Uri address, JsonNode? body, CancellationToken cancellationToken)
    {
        var json = body?.ToJsonString();
        return SendForJsonAsync(method, address, () => json is null ? null : new StringContent(json, Encoding.UTF8, "application/json"), cancellationToken);
    }

    /// <summary>
    /// Sends one request with the account's token and returns the successful response, headers
    /// read and body still to stream. The caller disposes it.
    /// </summary>
    public async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, Uri address, Func<HttpContent?> content, CancellationToken cancellationToken,
        Action<HttpRequestHeaders>? headers = null)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (!IsApiAddress(address))
            throw new DriveToolException(DriveErrorCode.InvalidRequest, $"Refusing to send the account's token to an address outside {_apiName}.");

        string? refused = null;
        while (true)
        {
            var token = await _credentials.GetAccessTokenAsync(_account, cancellationToken, refused).ConfigureAwait(false);
            var response = await SendOnceAsync(method, address, content, token, headers, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
                return response;

            // A 401 is answered once more with another token: the refused one may have been
            // revoked, or replaced in the store by a new sign-in, so it is read again and refreshed.
            if (response.StatusCode == HttpStatusCode.Unauthorized && refused is null)
            {
                response.Dispose();
                refused = token;
                continue;
            }

            throw await DescribeAsync(response, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Sends one request to a pre-authenticated address (a download link, an upload session),
    /// without the token. Only HTTPS is accepted. Returns the response whatever its status when
    /// <paramref name="acceptAnyStatus"/>; otherwise a failure is described and thrown.
    /// </summary>
    public async Task<HttpResponseMessage> SendPreauthenticatedAsync(
        HttpMethod method, Uri address, Func<HttpContent?> content, CancellationToken cancellationToken, bool acceptAnyStatus = false)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (!address.IsAbsoluteUri || address.Scheme != Uri.UriSchemeHttps)
            throw new DriveToolException(DriveErrorCode.ServerError, $"{_apiName} handed out an address that is not HTTPS; refusing it.");

        var response = await SendOnceAsync(method, address, content, token: null, headers: null, cancellationToken).ConfigureAwait(false);
        if (acceptAnyStatus || response.IsSuccessStatusCode)
            return response;
        throw await DescribeAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads a response body as JSON (an empty object when there is none) and disposes the response.</summary>
    public static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);
        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
        }
    }

    private async Task<JsonDocument> SendForJsonAsync(HttpMethod method, Uri address, Func<HttpContent?> content, CancellationToken cancellationToken)
    {
        var response = await SendAsync(method, address, content, cancellationToken).ConfigureAwait(false);
        return await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendOnceAsync(
        HttpMethod method, Uri address, Func<HttpContent?> content, string? token, Action<HttpRequestHeaders>? headers, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, address) { Content = content() };
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        headers?.Invoke(request.Headers);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (_account.Timeout is { } limit)
            timeout.CancelAfter(limit);
        try
        {
            return await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new DriveToolException(DriveErrorCode.ServerError, $"{_apiName} could not be reached: {ex.Message}", ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // The account's timeout or the HTTP client's own, not a cancellation by the caller.
            throw new DriveToolException(DriveErrorCode.ServerError, $"{_apiName} did not answer in time; retry later.", ex);
        }
    }

    private async Task<DriveToolException> DescribeAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var (code, message) = ReadError(body);
            var detail = string.IsNullOrEmpty(code) ? message : $"{code}: {message}";
            return response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => new DriveToolException(
                    DriveErrorCode.AuthenticationFailed,
                    $"{_apiName} refused the token of drive account '{_account.Name}': run `orkeon drive login {_account.Name}` again ({detail})."),
                HttpStatusCode.Forbidden when IsThrottling(code) => Throttled(response, detail),
                HttpStatusCode.Forbidden => new DriveToolException(
                    DriveErrorCode.AuthenticationFailed,
                    $"{_apiName} denied the operation for account '{_account.Name}': the signed-in user may not change this item, or the application lacks the permission ({detail})."),
                HttpStatusCode.NotFound or HttpStatusCode.Gone => new DriveToolException(
                    DriveErrorCode.ItemNotFound,
                    $"{_apiName} found no such item (moved, deleted, or not shared with this account): list or search again ({detail})."),
                HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed => new DriveToolException(
                    DriveErrorCode.Conflict,
                    $"{_apiName} refused the change: an item of that name is already there, or the item changed meanwhile ({detail})."),
                HttpStatusCode.RequestEntityTooLarge => new DriveToolException(
                    DriveErrorCode.TooLarge,
                    $"{_apiName} refused the request as too large ({detail})."),
                HttpStatusCode.BadRequest => new DriveToolException(
                    DriveErrorCode.InvalidRequest,
                    $"{_apiName} refused the request as malformed ({detail})."),
                HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout => Throttled(response, detail),
                _ => new DriveToolException(
                    DriveErrorCode.ServerError,
                    string.Create(CultureInfo.InvariantCulture, $"{_apiName} answered HTTP {(int)response.StatusCode} ({detail}).")),
            };
        }
    }

    private DriveToolException Throttled(HttpResponseMessage response, string detail) => new(
        DriveErrorCode.ServerError,
        $"{_apiName} is throttling or unavailable; retry {RetryHint(response)} ({detail}).");

    // Google reports its rate limits as 403 with one of these reasons.
    private static bool IsThrottling(string code) =>
        code is "rateLimitExceeded" or "userRateLimitExceeded" or "quotaExceeded" or "sharingRateLimitExceeded";

    private static string RetryHint(HttpResponseMessage response) =>
        response.Headers.RetryAfter?.Delta is { } delay
            ? string.Create(CultureInfo.InvariantCulture, $"in {Math.Ceiling(delay.TotalSeconds)} s")
            : "later";

    /// <summary>
    /// The code and message of an error body: Graph writes <c>{"error":{"code":"itemNotFound",…}}</c>,
    /// Google <c>{"error":{"code":404,"errors":[{"reason":"notFound"}],…}}</c> — its reason is the code.
    /// </summary>
    internal static (string Code, string Message) ReadError(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.Object)
            {
                var code = error.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() ?? string.Empty : string.Empty;
                if (error.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array
                    && errors.GetArrayLength() > 0 && errors[0].TryGetProperty("reason", out var reason) && reason.ValueKind == JsonValueKind.String)
                {
                    code = reason.GetString() ?? code;
                }

                var message = error.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() ?? string.Empty : string.Empty;
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
