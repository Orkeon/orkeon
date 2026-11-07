using System.Net.Http.Headers;
using Orkeon.Tools.Abstractions.Auth;

namespace Orkeon.Infrastructure.MCP.Auth;

/// <summary>
/// Signs an MCP server's requests with an OAuth 2.0 access token (FWK-16): client credentials, or the device
/// flow followed by refreshes. The token lives in memory only and is renewed 60 seconds before it expires. When
/// the server answers 401, <see cref="HandleUnauthorizedAsync"/> renews it once however many requests were refused
/// together. The client secret and the tokens never appear in an error: the provider's messages are scrubbed.
/// </summary>
internal sealed class OAuthMcpAuthenticator : IMcpRequestAuthenticator, IDisposable
{
    private static readonly TimeSpan RenewMargin = TimeSpan.FromSeconds(60);

    private readonly string _serverId;
    private readonly string _secretKey;
    private readonly OAuth2Client _client;
    private readonly OAuthSettings _settings;
    private readonly string? _secretVariable;
    private readonly Func<string, string?> _environment;
    private readonly IOAuthLoginInteraction? _interaction;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private OAuthTokenSet? _tokens;
    private int _waiting;
    private int _failureEpoch;
    private string? _failureRejected;
    private OAuthException? _failure;

    public OAuthMcpAuthenticator(
        string serverId, OAuth2Client client, OAuthSettings settings, string? clientSecretEnv,
        Func<string, string?> environment, IOAuthLoginInteraction? interaction, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(time);
        _serverId = serverId;
        _secretKey = $"MCP:Servers:{serverId}:Auth:ClientSecretEnv";
        _client = client;
        _settings = settings;
        _secretVariable = clientSecretEnv;
        _environment = environment;
        _interaction = interaction;
        _time = time;
    }

    public async ValueTask ApplyAsync(HttpRequestMessage request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var token = await CurrentAsync(rejected: null, ct).ConfigureAwait(false);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public async ValueTask<bool> HandleUnauthorizedAsync(string? rejectedCredential, CancellationToken ct = default)
    {
        const string prefix = "Bearer ";
        var rejected = rejectedCredential is not null && rejectedCredential.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? rejectedCredential[prefix.Length..]
            : null;
        if (rejected is null)
            return false;

        await CurrentAsync(rejected, ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>How many callers are inside a token request or waiting for one: tests read it to know all are queued.</summary>
    internal int WaitingCount => Volatile.Read(ref _waiting);

    public void Dispose() => _gate.Dispose();

    /// <summary>
    /// The access token to send. Under the lock, so that callers meeting an expired or refused token wait for the
    /// one renewal instead of making their own: <paramref name="rejected"/> is the token the server refused, and a
    /// cache that already holds another one has been renewed by someone else.
    /// </summary>
    private async Task<string> CurrentAsync(string? rejected, CancellationToken ct)
    {
        Interlocked.Increment(ref _waiting);
        var epoch = Volatile.Read(ref _failureEpoch);
        try
        {
            await _gate.WaitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            Interlocked.Decrement(ref _waiting);
            throw;
        }

        try
        {
            // Callers that queued while a renewal of this very token failed get its error, not a request each.
            if (rejected is not null && _failure is { } failed && _failureRejected == rejected && _failureEpoch != epoch)
                throw new OAuthException(failed.Failure, failed.Message);

            var previous = _tokens;
            if (previous is not null)
            {
                if (rejected is null && previous.ExpiresAt - _time.GetUtcNow() > RenewMargin)
                    return previous.AccessToken;
                if (rejected is not null && previous.AccessToken != rejected)
                    return previous.AccessToken;
            }

            try
            {
                _tokens = await ObtainAsync(previous, ct).ConfigureAwait(false);
                _failure = null;
            }
            catch (OAuthException ex)
            {
                var scrubbed = new OAuthException(ex.Failure, Scrub($"MCP server '{_serverId}': {ex.Message}", previous));
                _failure = scrubbed;
                _failureRejected = rejected;
                Interlocked.Increment(ref _failureEpoch);
                throw scrubbed;
            }

            return _tokens.AccessToken;
        }
        finally
        {
            Interlocked.Decrement(ref _waiting);
            _gate.Release();
        }
    }

    private async Task<OAuthTokenSet> ObtainAsync(OAuthTokenSet? previous, CancellationToken ct)
    {
        var secret = ReadSecret();
        if (_settings.Flow == OAuthFlow.ClientCredentials)
            return await _client.RequestClientCredentialsAsync(_settings, secret ?? "", ct).ConfigureAwait(false);

        if (previous?.RefreshToken is { } refreshToken)
        {
            try
            {
                return await _client.RefreshAsync(_settings, secret, refreshToken, ct).ConfigureAwait(false);
            }
            catch (OAuthException ex) when (ex.Failure == OAuthFailure.LoginRequired)
            {
                // The refresh token is spent or revoked: a person signs in again.
            }
        }

        var interaction = _interaction
            ?? throw new McpServerConfigurationException($"MCP:Servers:{_serverId}:Auth:Type",
                $"MCP:Servers:{_serverId}:Auth:Type oauth2-device needs a host that can show the sign-in code to a person.");
        var grant = await _client.RequestDeviceCodeAsync(_settings, ct).ConfigureAwait(false);
        await interaction.ShowDeviceCodeAsync(_serverId, grant.VerificationUri, grant.UserCode, grant.ExpiresIn, ct).ConfigureAwait(false);
        return await _client.PollDeviceCodeAsync(_settings, secret, grant, ct).ConfigureAwait(false);
    }

    private string? ReadSecret()
    {
        if (string.IsNullOrWhiteSpace(_secretVariable))
            return null;
        var value = _environment(_secretVariable);
        if (string.IsNullOrEmpty(value))
        {
            throw new McpServerConfigurationException(_secretKey,
                $"{_secretKey} reads the environment variable '{_secretVariable}', which is empty: the request was not sent.");
        }

        return value;
    }

    /// <summary>The message without the client secret or the tokens: a provider may echo what it was sent.</summary>
    private string Scrub(string message, OAuthTokenSet? tokens) =>
        OAuthRedaction.Redact(
            message,
            _secretVariable is null ? null : _environment(_secretVariable),
            tokens?.AccessToken,
            tokens?.RefreshToken);
}
