using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Orkeon.Host.Gateway;

/// <summary>The bot's own token for the Bot Connector API (client credentials), cached until shortly before it expires.</summary>
internal sealed class TeamsBotToken : IDisposable
{
    private const string Scope = "https://api.botframework.com/.default";
    private static readonly TimeSpan s_margin = TimeSpan.FromMinutes(5);

    private readonly TeamsChannelOptions _options;
    private readonly HttpClient _http;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _token;
    private DateTimeOffset _renewAt;

    /// <summary>Builds the token source over the transport that reaches the sign-in service.</summary>
    public TeamsBotToken(TeamsChannelOptions options, HttpClient http, TimeProvider time)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    /// <summary>The token, asked for when none is held or the held one is about to expire.</summary>
    /// <exception cref="InvalidOperationException">The sign-in service gave none. The message quotes nothing it answered.</exception>
    public async Task<string> GetAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_token is { } held && _time.GetUtcNow() < _renewAt)
                return held;

            var (token, lifetime) = await RequestAsync(ct).ConfigureAwait(false);
            _renewAt = _time.GetUtcNow() + (lifetime > s_margin * 2 ? lifetime - s_margin : lifetime / 2);
            _token = token;
            return token;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Forgets <paramref name="refused"/>, which the service refused, unless it was already replaced.</summary>
    public async Task ForgetAsync(string refused, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_token == refused)
                _token = null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<(string Token, TimeSpan Lifetime)> RequestAsync(CancellationToken ct)
    {
        var secret = _options.ReadSecret()
            ?? throw new InvalidOperationException($"The environment variable '{_options.SecretEnvironmentVariable}' is empty or unset.");
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = _options.AppId,
            ["client_secret"] = secret,
            ["scope"] = Scope,
        });
        using var response = await _http
            .PostAsync(new Uri($"https://login.microsoftonline.com/{_options.TenantId}/oauth2/v2.0/token"), form, ct)
            .ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
            // At fault, or asking to wait: it refused nothing. The status says so to the caller; the answer is never quoted.
            throw new HttpRequestException($"The sign-in service failed (HTTP {(int)response.StatusCode}).", inner: null, response.StatusCode);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"The sign-in service refused the bot's credentials (HTTP {(int)response.StatusCode}): check Teams:AppId, "
                + $"Teams:TenantId and the secret in '{_options.SecretEnvironmentVariable}'.");

        JsonElement answer;
        try
        {
            answer = await response.Content.ReadFromJsonAsync<JsonElement>(ct).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            // Not the inner exception: it could quote the answer, which may hold a token.
            throw new InvalidOperationException("The sign-in service answered no usable token.");
        }

        return answer.ValueKind == JsonValueKind.Object
            && answer.TryGetProperty("access_token", out var token) && token.ValueKind == JsonValueKind.String
            && token.GetString() is { Length: > 0 } value
            && answer.TryGetProperty("expires_in", out var expires) && expires.ValueKind == JsonValueKind.Number
            && expires.TryGetInt32(out var seconds) && seconds is > 0 and <= 7 * 24 * 3600
                ? (value, TimeSpan.FromSeconds(seconds))
                : throw new InvalidOperationException("The sign-in service answered no usable token.");
    }

    /// <inheritdoc />
    public void Dispose() => _gate.Dispose();
}
