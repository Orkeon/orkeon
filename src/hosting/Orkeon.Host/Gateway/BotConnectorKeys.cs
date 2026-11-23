using System.Collections.Frozen;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Orkeon.Host.Gateway;

/// <summary>
/// The signing keys of the Bot Connector service, read from its OpenID metadata over HTTPS at a fixed address and cached.
/// <para>
/// Unauthenticated requests can ask for a refresh (a token naming an unknown key), so refreshes are spaced out: a caller
/// cannot turn this endpoint into a stream of requests to Microsoft's.
/// </para>
/// </summary>
internal sealed partial class BotConnectorKeys : IDisposable
{
    /// <summary>The fixed address of the Bot Connector's OpenID metadata.</summary>
    internal const string MetadataAddress = "https://login.botframework.com/v1/.well-known/openidconfiguration";

    /// <summary>The issuer of the Bot Connector's tokens.</summary>
    internal const string Issuer = "https://api.botframework.com";

    /// <summary>Microsoft asks for a refresh at least once every 24 hours.</summary>
    internal static readonly TimeSpan RefreshAfter = TimeSpan.FromHours(12);

    /// <summary>Beyond this age, keys that could not be refreshed are no longer trusted.</summary>
    internal static readonly TimeSpan ExpireAfter = TimeSpan.FromHours(24);

    /// <summary>The least time between two refreshes asked for by an unknown key.</summary>
    internal static readonly TimeSpan UnknownKeyInterval = TimeSpan.FromMinutes(5);

    /// <summary>The least time between two attempts when no fresh keys are held.</summary>
    internal static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(10);

    /// <summary>How long a request waits for a refresh before it is asked to come back.</summary>
    internal static readonly TimeSpan RefreshWait = TimeSpan.FromSeconds(5);

    private sealed record Snapshot(FrozenDictionary<string, JsonWebKey> Keys, DateTimeOffset ReadAt);

    private readonly HttpClient _http;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly Lock _lock = new();
    private readonly CancellationTokenSource _stopping = new();
    private readonly CancellationToken _stopped;
    private Task _refreshing = Task.CompletedTask;
    private volatile Snapshot? _snapshot;
    private DateTimeOffset _lastAttempt = DateTimeOffset.MinValue;

    /// <summary>Builds the store over the transport that reads the metadata.</summary>
    public BotConnectorKeys(HttpClient http, TimeProvider time, ILogger logger)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _stopped = _stopping.Token;
    }

    /// <summary>The key named <paramref name="keyId"/>, null when the service does not list it.</summary>
    /// <returns>Whether keys are held at all, and the key.</returns>
    public async Task<(bool Available, JsonWebKey? Key)> FindAsync(string keyId, CancellationToken ct)
    {
        var held = Lookup(keyId);
        if (held is { Usable: true, Key: not null })
        {
            // A known key answers at once; an old set is read again behind it.
            if (!held.Fresh)
                _ = RefreshIfDue(RetryInterval);
            return (true, held.Key);
        }

        try
        {
            await RefreshIfDue(held.Fresh ? UnknownKeyInterval : RetryInterval).WaitAsync(RefreshWait, _time, ct).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            LogRefreshSlow();
        }

        held = Lookup(keyId);
        return (held.Usable, held.Usable ? held.Key : null);
    }

    /// <summary>The refresh under way, or a new one when the last ended <paramref name="spacing"/> ago; one at a time, shared.</summary>
    private Task RefreshIfDue(TimeSpan spacing)
    {
        lock (_lock)
        {
            if (!_refreshing.IsCompleted || _time.GetUtcNow() - _lastAttempt < spacing)
                return _refreshing;

            return _refreshing = Task.Run(AttemptAsync);
        }
    }

    private async Task AttemptAsync()
    {
        try
        {
            await RefreshAsync(_stopped).ConfigureAwait(false);
        }
        finally
        {
            lock (_lock)
                _lastAttempt = _time.GetUtcNow();
        }
    }

    private (bool Usable, bool Fresh, JsonWebKey? Key) Lookup(string keyId)
    {
        if (_snapshot is not { } snapshot)
            return (false, false, null);

        var age = _time.GetUtcNow() - snapshot.ReadAt;
        return (age < ExpireAfter, age < RefreshAfter, snapshot.Keys.GetValueOrDefault(keyId));
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        try
        {
            using var metadata = JsonDocument.Parse(await ReadAsync(new Uri(MetadataAddress), ct).ConfigureAwait(false));
            if (KeysAddressOf(metadata.RootElement) is not { } address)
            {
                LogKeysRefused();
                return;
            }

            var set = new JsonWebKeySet(await ReadAsync(address, ct).ConfigureAwait(false));
            var keys = set.Keys
                .Where(k => k.Kty == JsonWebAlgorithmsKeyTypes.RSA && !string.IsNullOrEmpty(k.Kid) && (string.IsNullOrEmpty(k.Use) || k.Use == "sig"))
                .GroupBy(k => k.Kid, StringComparer.Ordinal)
                .ToFrozenDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            if (keys.Count == 0)
            {
                LogKeysRefused();
                return;
            }

            _snapshot = new Snapshot(keys, _time.GetUtcNow());
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or ArgumentException or InvalidOperationException or OperationCanceledException)
        {
            LogKeysNotRead(ex);
        }
    }

    /// <summary>Where the metadata says the keys are: on its own host, over HTTPS, or nowhere.</summary>
    private static Uri? KeysAddressOf(JsonElement metadata) =>
        metadata.ValueKind == JsonValueKind.Object
        && metadata.TryGetProperty("issuer", out var issuer) && issuer.ValueKind == JsonValueKind.String && issuer.GetString() == Issuer
        && metadata.TryGetProperty("jwks_uri", out var jwks) && jwks.ValueKind == JsonValueKind.String
        && Uri.TryCreate(jwks.GetString(), UriKind.Absolute, out var address)
        && address.Scheme == Uri.UriSchemeHttps
        && string.Equals(address.Authority, new Uri(MetadataAddress).Authority, StringComparison.OrdinalIgnoreCase)
            ? address
            : null;

    private async Task<string> ReadAsync(Uri address, CancellationToken ct)
    {
        using var response = await _http.GetAsync(address, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_stopped.IsCancellationRequested)
            return;

        _stopping.Cancel();
        _stopping.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Bot Framework's signing keys are slow to come: the request is asked to come back")]
    private partial void LogRefreshSlow();

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Bot Connector's signing keys could not be read; Teams activities are refused while none are held")]
    private partial void LogKeysNotRead(Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Bot Connector's OpenID metadata is not the expected one; it was not used")]
    private partial void LogKeysRefused();
}
