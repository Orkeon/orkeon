using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Orkeon.Tools.Chat.Auth;
using Orkeon.Tools.Chat.Configuration;
using Orkeon.Tools.Chat.Constants;

namespace Orkeon.Tools.Chat.Providers.Teams;

/// <summary>
/// One account's HTTP conversation with Microsoft Graph. The token goes to the account's Graph
/// host over HTTPS and nowhere else; addresses are built here from a relative path, and a paging
/// link only ever contributes its paging token. A 401 is answered once with another token, a 429
/// waits for <c>Retry-After</c> when it is short. A write is never repeated once it may have been done.
/// </summary>
internal sealed class GraphClient
{
    private const string ApiName = "Microsoft Graph";
    private const string MayHaveBeenPosted =
        " The message may have been posted all the same: read the place before sending it again.";

    private static readonly string[] PagingNames = ["$skiptoken", "$skip"];

    private readonly Func<HttpClient> _http;
    private readonly ResolvedChatAccount _account;
    private readonly ChatCredentialProvider _credentials;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _pauses = new(StringComparer.Ordinal);

    /// <summary>Creates the client of <paramref name="account"/>.</summary>
    public GraphClient(Func<HttpClient> http, ResolvedChatAccount account, ChatCredentialProvider credentials, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(time);
        _http = http;
        _account = account;
        _credentials = credentials;
        _time = time;
    }

    /// <summary>The account.</summary>
    public ResolvedChatAccount Account => _account;

    /// <summary>One id as a path segment.</summary>
    public static string Segment(string id)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        return Uri.EscapeDataString(id);
    }

    /// <summary>GETs a JSON object. <paramref name="permission"/> is what a 403 answer names.</summary>
    public Task<JsonElement> GetAsync(string path, string permission, CancellationToken cancellationToken, bool eventual = false) =>
        SendAsync(HttpMethod.Get, path, body: null, permission, eventual, cancellationToken);

    /// <summary>
    /// POSTs a JSON body. An answer that cannot be read as JSON is an empty object: the request was
    /// accepted, and reporting a failure would invite a second one.
    /// </summary>
    /// <remarks><paramref name="doubt"/> is what a failure says when the write may have been done.</remarks>
    public Task<JsonElement> PostAsync(string path, JsonNode body, string permission, CancellationToken cancellationToken, string doubt = MayHaveBeenPosted)
    {
        ArgumentNullException.ThrowIfNull(body);
        return SendAsync(HttpMethod.Post, path, body.ToJsonString(), permission, eventual: false, cancellationToken, doubt);
    }

    /// <summary>
    /// The paging parameters of a page's next link, percent-encoded as a query fragment, or null on
    /// the last page. Nothing else of the link is kept, and a link to another host is refused.
    /// </summary>
    public string? NextPage(JsonElement page)
    {
        if (GraphJson.Text(page, "@odata.nextLink") is not { Length: > 0 } link)
            return null;
        if (!Uri.TryCreate(link, UriKind.Absolute, out var address) || !IsApiAddress(address))
            throw new ChatToolException(ChatErrorCode.ServerError, $"{ApiName} handed out a paging link to another host; refusing it.");
        return PagingFragment(address.Query)
            ?? throw new ChatToolException(ChatErrorCode.ServerError, $"{ApiName} handed out a paging link without a paging token.");
    }

    /// <summary>The paging parameters of <paramref name="query"/>, encoded again; null when it has none.</summary>
    internal static string? PagingFragment(string query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var kept = new List<string>();
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var cut = pair.IndexOf('=', StringComparison.Ordinal);
            if (cut <= 0)
                continue;
            var written = Uri.UnescapeDataString(pair[..cut]);
            var name = PagingNames.FirstOrDefault(known => string.Equals(known, written, StringComparison.OrdinalIgnoreCase));
            if (name is not null)
                kept.Add($"{name}={Uri.EscapeDataString(Uri.UnescapeDataString(pair[(cut + 1)..].Replace('+', ' ')))}");
        }

        return kept.Count == 0 ? null : string.Join('&', kept);
    }

    private bool IsApiAddress(Uri address) =>
        address.IsAbsoluteUri
        && address.Scheme == Uri.UriSchemeHttps
        && address.IsDefaultPort
        && string.Equals(address.Host, _account.ApiBase.Host, StringComparison.OrdinalIgnoreCase);

    private async Task<JsonElement> SendAsync(
        HttpMethod method, string path, string? body, string permission, bool eventual, CancellationToken cancellationToken, string doubt = "")
    {
        var address = new Uri(_account.ApiBase, path);
        if (!IsApiAddress(address) || !address.AbsolutePath.StartsWith(_account.ApiBase.AbsolutePath, StringComparison.Ordinal))
            throw new ChatToolException(ChatErrorCode.InvalidRequest, $"Refusing to send the account's token to an address outside {ApiName}.");

        var key = $"{method} {address.AbsolutePath}";
        RefuseWhilePaused(key);
        string? refused = null;
        var waited = false;
        while (true)
        {
            var token = await _credentials.GetAccessTokenAsync(_account, cancellationToken, refused).ConfigureAwait(false);
            using var response = await SendOnceAsync(method, address, body, token, eventual, doubt, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
                return await ReadAsync(response, isWrite: body is not null, cancellationToken).ConfigureAwait(false);

            // The refused token may have been revoked, or replaced in the store by a new sign-in.
            if (response.StatusCode == HttpStatusCode.Unauthorized && refused is null)
            {
                refused = token;
                continue;
            }

            // A 429 refuses the request before anything is done: a write may be repeated too.
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var pause = RetryAfter(response);
                _pauses[key] = _time.GetUtcNow() + pause;
                if (!waited && pause <= ChatDefaults.MaxThrottleWait)
                {
                    waited = true;
                    await Task.Delay(pause, _time, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                throw Throttled(pause);
            }

            throw await DescribeAsync(response, doubt, permission, cancellationToken).ConfigureAwait(false);
        }
    }

    private void RefuseWhilePaused(string key)
    {
        if (!_pauses.TryGetValue(key, out var until))
            return;
        var left = until - _time.GetUtcNow();
        if (left > TimeSpan.Zero)
            throw Throttled(left);
        _pauses.TryRemove(new KeyValuePair<string, DateTimeOffset>(key, until));
    }

    private static ChatToolException Throttled(TimeSpan pause) => new(
        ChatErrorCode.Throttled,
        string.Create(CultureInfo.InvariantCulture, $"{ApiName} limits the rate of requests here: retry in {Math.Ceiling(pause.TotalSeconds)} s. Nothing was done."));

    private TimeSpan RetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        var pause = header?.Delta ?? (header?.Date - _time.GetUtcNow()) ?? ChatDefaults.DefaultThrottleWait;
        return pause < TimeSpan.Zero ? TimeSpan.Zero : pause;
    }

    private async Task<HttpResponseMessage> SendOnceAsync(
        HttpMethod method, Uri address, string? body, string token, bool eventual, string doubt, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, address);
        if (body is not null)
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (eventual)
            request.Headers.Add("ConsistencyLevel", "eventual");

        try
        {
            return await _http().SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            // Only these failures come before a byte of the request has left.
            var unsent = ex.HttpRequestError is HttpRequestError.NameResolutionError or HttpRequestError.ConnectionError or HttpRequestError.SecureConnectionError;
            throw new ChatToolException(
                ChatErrorCode.ServerError,
                $"{ApiName} could not be reached: {ex.Message}.{(unsent ? string.Empty : doubt)}",
                ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ChatToolException(
                ChatErrorCode.ServerError,
                body is null ? $"{ApiName} did not answer in time; retry later." : $"{ApiName} did not answer in time.{doubt}",
                ex);
        }
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response, bool isWrite, CancellationToken cancellationToken)
    {
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var json = JsonElement.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
            if (json.ValueKind == JsonValueKind.Object)
                return json;
        }
        catch (JsonException) when (isWrite)
        {
            // Accepted all the same: the caller reports a message without its id.
        }
        catch (JsonException ex)
        {
            throw new ChatToolException(ChatErrorCode.ServerError, $"{ApiName} answered something that is not JSON.", ex);
        }

        return JsonElement.Parse("{}");
    }

    private async Task<ChatToolException> DescribeAsync(
        HttpResponseMessage response, string doubt, string permission, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var detail = GraphJson.Error(body);
        var status = (int)response.StatusCode;
        return response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => new ChatToolException(
                ChatErrorCode.SignInRequired,
                $"{ApiName} refused the token. {ChatCredentialProvider.LoginHint(_account)} ({detail})"),
            HttpStatusCode.Forbidden => new ChatToolException(
                ChatErrorCode.ConsentMissing,
                _account.IsApplication
                    ? $"{ApiName} denied this to chat account '{_account.Name}': its application lacks the {ChatScopes.OfApplication(permission)} application permission, which a tenant administrator grants ({detail})."
                    : $"{ApiName} denied this to chat account '{_account.Name}': the signed-in user may not do it, or the application lacks the {permission} permission, which {Grantor(permission)} ({detail})."),
            HttpStatusCode.NotFound or HttpStatusCode.Gone => new ChatToolException(
                ChatErrorCode.NotFound,
                $"{ApiName} found no such place, message or person, or the account cannot see it: list again ({detail})."),
            HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge => new ChatToolException(
                ChatErrorCode.InvalidRequest, $"{ApiName} refused the request as malformed ({detail})."),
            _ => new ChatToolException(
                ChatErrorCode.ServerError,
                string.Create(CultureInfo.InvariantCulture, $"{ApiName} answered HTTP {status} ({detail}).{(status >= 500 ? doubt : string.Empty)}")),
        };
    }

    private string Grantor(string permission) => ChatScopes.NeedsAdministrator(permission)
        ? "a tenant administrator grants"
        : $"the user grants by signing in again (`orkeon chat login {_account.Name}`)";
}
