using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Orkeon.Tools.Email.Configuration;

namespace Orkeon.Tools.Email.Auth;

/// <summary>A pending RFC 8628 device authorization.</summary>
/// <param name="DeviceCode">Code the client polls the token endpoint with.</param>
/// <param name="UserCode">Code the user types at <paramref name="VerificationUri"/>.</param>
/// <param name="VerificationUri">Where the user signs in.</param>
/// <param name="ExpiresIn">How long the codes stay valid.</param>
/// <param name="Interval">How long to wait between polls.</param>
internal sealed record DeviceCodeGrant(string DeviceCode, string UserCode, Uri VerificationUri, TimeSpan ExpiresIn, TimeSpan Interval);

/// <summary>A PKCE pair (RFC 7636) and the anti-forgery state of one authorization request.</summary>
/// <param name="Verifier">The secret kept by the client.</param>
/// <param name="Challenge">Its S256 digest, sent in the authorization request.</param>
/// <param name="State">Opaque value the redirect must echo.</param>
internal sealed record PkceSession(string Verifier, string Challenge, string State)
{
    /// <summary>A fresh session from the system's cryptographic random source.</summary>
    public static PkceSession Create()
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return new PkceSession(verifier, challenge, Base64Url(RandomNumberGenerator.GetBytes(16)));
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>
/// The OAuth 2.0 client of the e-mail family, over plain HTTP: device authorization
/// (RFC 8628), authorization code with PKCE (RFC 7636, RFC 8252) and refresh (RFC 6749 §6).
/// Written by hand so that neither MSAL nor the Google SDK enters the dependency graph.
/// </summary>
internal sealed class OAuth2Client
{
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan SlowDownStep = TimeSpan.FromSeconds(5);

    private readonly HttpClient _http;
    private readonly TimeProvider _time;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    /// <summary>Creates the client.</summary>
    /// <param name="http">HTTP client used for every endpoint.</param>
    /// <param name="time">Clock used to stamp token expiry.</param>
    /// <param name="delay">Wait between device-code polls; tests pass one that returns at once.</param>
    public OAuth2Client(HttpClient http, TimeProvider time, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(time);
        _http = http;
        _time = time;
        _delay = delay ?? ((wait, token) => Task.Delay(wait, time, token));
    }

    /// <summary>Whether <paramref name="tokens"/> still has an access token that outlives the refresh margin.</summary>
    public bool IsFresh(EmailTokenSet tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        return tokens.ExpiresAt - _time.GetUtcNow() > RefreshMargin;
    }

    /// <summary>Starts a device authorization.</summary>
    public async Task<DeviceCodeGrant> RequestDeviceCodeAsync(OAuthSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var endpoint = settings.DeviceCodeEndpoint
            ?? throw new EmailToolException(EmailErrorCode.InvalidConfiguration, "This account's provider has no device authorization endpoint.");

        using var document = await PostAsync(endpoint,
        [
            new("client_id", settings.ClientId),
            new("scope", string.Join(' ', settings.Scopes)),
        ], cancellationToken).ConfigureAwait(false);

        var root = document.RootElement;
        if (TryReadError(root, out var error, out var description))
            throw new EmailToolException(EmailErrorCode.AuthenticationFailed, $"The device authorization was refused: {error} {description}".Trim());

        var verification = ReadString(root, "verification_uri") ?? ReadString(root, "verification_url");
        if (ReadString(root, "device_code") is not { } deviceCode
            || ReadString(root, "user_code") is not { } userCode
            || verification is null
            || !Uri.TryCreate(verification, UriKind.Absolute, out var verificationUri))
        {
            throw new EmailToolException(EmailErrorCode.ServerError, "The device authorization response lacks a device code, a user code or a verification address.");
        }

        return new DeviceCodeGrant(
            deviceCode,
            userCode,
            verificationUri,
            TimeSpan.FromSeconds(ReadInt(root, "expires_in") ?? 900),
            TimeSpan.FromSeconds(Math.Max(ReadInt(root, "interval") ?? 5, 1)));
    }

    /// <summary>Polls the token endpoint until the user completes (or refuses) the device sign-in.</summary>
    public async Task<EmailTokenSet> PollDeviceCodeAsync(
        OAuthSettings settings, string? clientSecret, DeviceCodeGrant grant, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(grant);
        var deadline = _time.GetUtcNow() + grant.ExpiresIn;
        var interval = grant.Interval;

        while (_time.GetUtcNow() < deadline)
        {
            await _delay(interval, cancellationToken).ConfigureAwait(false);
            var form = new List<KeyValuePair<string, string>>
            {
                new("grant_type", "urn:ietf:params:oauth:grant-type:device_code"),
                new("client_id", settings.ClientId),
                new("device_code", grant.DeviceCode),
            };
            AddSecret(form, clientSecret);

            using var document = await PostAsync(settings.TokenEndpoint, form, cancellationToken).ConfigureAwait(false);
            var root = document.RootElement;
            if (!TryReadError(root, out var error, out var description))
                return ReadTokens(root, previousRefreshToken: null, settings.Scopes);

            switch (error)
            {
                case "authorization_pending":
                    continue;
                case "slow_down":
                    interval += SlowDownStep;
                    continue;
                default:
                    throw new EmailToolException(EmailErrorCode.LoginRequired, $"The sign-in did not complete: {error} {description}".Trim());
            }
        }

        throw new EmailToolException(EmailErrorCode.LoginRequired, "The sign-in code expired before the sign-in completed; run the login again.");
    }

    /// <summary>The address the user opens to authorize the loopback flow.</summary>
    public static Uri BuildAuthorizationUri(OAuthSettings settings, Uri redirectUri, PkceSession session, string? loginHint)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(redirectUri);
        ArgumentNullException.ThrowIfNull(session);
        var endpoint = settings.AuthorizationEndpoint
            ?? throw new EmailToolException(EmailErrorCode.InvalidConfiguration, "This account's provider has no authorization endpoint.");

        var query = new List<KeyValuePair<string, string>>
        {
            new("client_id", settings.ClientId),
            new("redirect_uri", redirectUri.AbsoluteUri),
            new("response_type", "code"),
            new("scope", string.Join(' ', settings.Scopes)),
            new("code_challenge", session.Challenge),
            new("code_challenge_method", "S256"),
            new("state", session.State),
            new("access_type", "offline"),
            new("prompt", "consent"),
        };
        if (!string.IsNullOrWhiteSpace(loginHint))
            query.Add(new("login_hint", loginHint));

        var encoded = string.Join('&', query.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        return new Uri($"{endpoint.AbsoluteUri}?{encoded}");
    }

    /// <summary>Exchanges an authorization code for tokens.</summary>
    public async Task<EmailTokenSet> ExchangeCodeAsync(
        OAuthSettings settings, string? clientSecret, string code, Uri redirectUri, PkceSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(redirectUri);
        ArgumentNullException.ThrowIfNull(session);
        var form = new List<KeyValuePair<string, string>>
        {
            new("grant_type", "authorization_code"),
            new("client_id", settings.ClientId),
            new("code", code),
            new("redirect_uri", redirectUri.AbsoluteUri),
            new("code_verifier", session.Verifier),
        };
        AddSecret(form, clientSecret);

        using var document = await PostAsync(settings.TokenEndpoint, form, cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        if (TryReadError(root, out var error, out var description))
            throw new EmailToolException(EmailErrorCode.AuthenticationFailed, $"The authorization code was refused: {error} {description}".Trim());
        return ReadTokens(root, previousRefreshToken: null, settings.Scopes);
    }

    /// <summary>Obtains a new access token from <paramref name="refreshToken"/>.</summary>
    public async Task<EmailTokenSet> RefreshAsync(
        OAuthSettings settings, string? clientSecret, string refreshToken, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var form = new List<KeyValuePair<string, string>>
        {
            new("grant_type", "refresh_token"),
            new("client_id", settings.ClientId),
            new("refresh_token", refreshToken),
        };
        if (settings.ScopesOnRefresh)
            form.Add(new("scope", string.Join(' ', settings.Scopes)));
        AddSecret(form, clientSecret);

        using var document = await PostAsync(settings.TokenEndpoint, form, cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        if (TryReadError(root, out var error, out var description))
        {
            var code = error is "invalid_grant" or "interaction_required" or "invalid_client" or "unauthorized_client"
                ? EmailErrorCode.LoginRequired
                : EmailErrorCode.AuthenticationFailed;
            throw new EmailToolException(code, $"The token refresh was refused: {error} {description}".Trim());
        }

        return ReadTokens(root, refreshToken, settings.Scopes);
    }

    private static void AddSecret(List<KeyValuePair<string, string>> form, string? clientSecret)
    {
        if (!string.IsNullOrEmpty(clientSecret))
            form.Add(new("client_secret", clientSecret));
    }

    private async Task<JsonDocument> PostAsync(Uri endpoint, IEnumerable<KeyValuePair<string, string>> form, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);
        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsync(endpoint, content, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new EmailToolException(EmailErrorCode.ServerError, $"The identity provider at {endpoint.Host} could not be reached: {ex.Message}", ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // The HTTP client's own timeout, not a cancellation by the caller.
            throw new EmailToolException(EmailErrorCode.ServerError, $"The identity provider at {endpoint.Host} did not answer in time; retry later.", ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            }
            catch (JsonException ex)
            {
                var status = ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture);
                throw new EmailToolException(EmailErrorCode.ServerError, $"The identity provider at {endpoint.Host} answered HTTP {status} without JSON.", ex);
            }
        }
    }

    private EmailTokenSet ReadTokens(JsonElement root, string? previousRefreshToken, IReadOnlyList<string> requestedScopes)
    {
        var accessToken = ReadString(root, "access_token")
            ?? throw new EmailToolException(EmailErrorCode.ServerError, "The token response has no access token.");
        var expiresIn = ReadInt(root, "expires_in") ?? 3600;
        var scopes = ReadString(root, "scope") is { Length: > 0 } granted
            ? granted.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            : requestedScopes.ToArray();

        return new EmailTokenSet
        {
            AccessToken = accessToken,
            RefreshToken = ReadString(root, "refresh_token") ?? previousRefreshToken,
            ExpiresAt = _time.GetUtcNow().AddSeconds(expiresIn),
            Scopes = scopes,
        };
    }

    private static bool TryReadError(JsonElement root, out string error, out string description)
    {
        error = ReadString(root, "error") ?? string.Empty;
        description = ReadString(root, "error_description") ?? string.Empty;
        return error.Length > 0;
    }

    private static string? ReadString(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? ReadInt(JsonElement root, string name)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var value))
            return null;
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        };
    }
}

/// <summary>Where an authorization redirect landed, parsed.</summary>
/// <param name="Code">The authorization code.</param>
/// <param name="State">The echoed state.</param>
/// <param name="Error">The provider's error code, when the user refused.</param>
internal sealed record AuthorizationRedirect(string? Code, string? State, string? Error)
{
    /// <summary>Parses the query of a redirect address.</summary>
    public static AuthorizationRedirect Parse(Uri redirect)
    {
        ArgumentNullException.ThrowIfNull(redirect);
        string? code = null, state = null, error = null;
        foreach (var pair in redirect.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var cut = pair.IndexOf('=', StringComparison.Ordinal);
            if (cut <= 0)
                continue;
            var key = pair[..cut];
            var value = WebUtility.UrlDecode(pair[(cut + 1)..]);
            switch (key)
            {
                case "code": code = value; break;
                case "state": state = value; break;
                case "error": error = value; break;
            }
        }

        return new AuthorizationRedirect(code, state, error);
    }

    /// <summary>Whether the redirect carries an outcome (a code or an error).</summary>
    public bool IsOutcome => Code is not null || Error is not null;
}
