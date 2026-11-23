using System.Collections.Frozen;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Orkeon.Infrastructure.Constants.Orchestration;

namespace Orkeon.Host.Gateway;

/// <summary>What the check of an incoming activity's bearer token concluded.</summary>
internal enum TeamsTokenOutcome
{
    /// <summary>It is not the Bot Connector's: nothing of the request may be acted on.</summary>
    Refused,

    /// <summary>The token is the Bot Connector's, for this bot.</summary>
    Accepted,

    /// <summary>The signing keys could not be read: nothing can be accepted, and the service may try again.</summary>
    KeysUnavailable,
}

/// <summary>
/// The result of the check. <see cref="ServiceUrl"/> and <see cref="Endorsements"/> are set on
/// <see cref="TeamsTokenOutcome.Accepted"/> only; <see cref="Reason"/> never quotes the token.
/// </summary>
internal sealed record TeamsTokenResult(
    TeamsTokenOutcome Outcome,
    string Reason,
    Uri? ServiceUrl = null,
    string? ServiceUrlClaim = null,
    IReadOnlyList<string>? Endorsements = null);

/// <summary>
/// The check every incoming activity passes before its body is read (#446): the requirements Microsoft lists for a bot that
/// writes its own authentication — bearer scheme, signature by a listed key, issuer, audience, lifetime, and a
/// <c>serviceurl</c> claim the caller then compares with the activity's. There is no way to switch it off.
/// </summary>
internal sealed class TeamsTokenValidator
{
    private const string ServiceUrlClaimName = "serviceurl";
    private const string BearerPrefix = "Bearer ";
    private const int MaxTokenLength = 16 * 1024;

    private readonly BotConnectorKeys _keys;
    private readonly TimeProvider _time;
    private readonly string _appId;
    private readonly FrozenSet<string> _serviceHosts;
    private readonly JsonWebTokenHandler _handler = new() { MapInboundClaims = false, MaximumTokenSizeInBytes = MaxTokenLength };

    /// <summary>Builds the check for the bot the options name.</summary>
    public TeamsTokenValidator(TeamsChannelOptions options, BotConnectorKeys keys, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(options);
        _keys = keys ?? throw new ArgumentNullException(nameof(keys));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _appId = Guid.Parse(options.AppId).ToString("D");
        _serviceHosts = TeamsChannelOptions.KnownServiceHosts.Concat(options.ServiceHosts)
            .ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Checks the value of a request's <c>Authorization</c> header.</summary>
    public async Task<TeamsTokenResult> ValidateAsync(string? authorization, CancellationToken ct)
    {
        if (authorization is null || !authorization.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
            return Refused("no bearer token");

        var token = authorization[BearerPrefix.Length..].Trim();
        if (token.Length is 0 or > MaxTokenLength || token.Count(c => c == '.') != 2)
            return Refused("not a signed token");

        // Read unverified only to find which listed key to verify with, and to refuse without a fetch what no key could save.
        string keyId;
        try
        {
            var unverified = _handler.ReadJsonWebToken(token);
            if (unverified.Alg != SecurityAlgorithms.RsaSha256)
                return Refused("the token is not signed with RS256");
            if (unverified.Issuer != BotConnectorKeys.Issuer || !unverified.Audiences.Contains(_appId, StringComparer.Ordinal))
                return Refused("the token is not the Bot Connector's for this bot");
            keyId = unverified.Kid;
        }
        catch (Exception ex) when (ex is ArgumentException or SecurityTokenException or JsonException)
        {
            return Refused("not a signed token");
        }

        if (string.IsNullOrEmpty(keyId))
            return Refused("the token names no key");

        var (available, key) = await _keys.FindAsync(keyId, ct).ConfigureAwait(false);
        if (!available)
            return new TeamsTokenResult(TeamsTokenOutcome.KeysUnavailable, "the signing keys could not be read");
        if (key is null)
            return Refused("the token is signed by an unknown key");

        var result = await _handler.ValidateTokenAsync(token, Parameters(key)).ConfigureAwait(false);
        if (!result.IsValid)
        {
            // The type alone: the message of an IdentityModel exception can quote the token.
            return Refused(result.Exception?.GetType().Name ?? "invalid token");
        }

        var claims = ((JsonWebToken)result.SecurityToken).Claims
            .Where(c => string.Equals(c.Type, ServiceUrlClaimName, StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
        if (claims is not [var claim])
            return Refused("the token carries no single serviceurl claim");

        return ServiceUrlOf(claim.Value) is { } serviceUrl
            ? new TeamsTokenResult(TeamsTokenOutcome.Accepted, string.Empty, serviceUrl, claim.Value, EndorsementsOf(key))
            : Refused("the serviceurl claim names no accepted https service host (Teams:ServiceHosts)");
    }

    private TokenValidationParameters Parameters(JsonWebKey key) => new()
    {
        ValidateIssuer = true,
        ValidIssuer = BotConnectorKeys.Issuer,
        ValidateAudience = true,
        ValidAudience = _appId,
        ValidateLifetime = true,
        LifetimeValidator = IsCurrent,
        RequireExpirationTime = true,
        RequireSignedTokens = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = key,
        TryAllIssuerSigningKeys = false,
        ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
        ClockSkew = OrchestrationDefaults.ClockSkew,
    };

    /// <summary>The lifetime against the channel's own clock, so that one clock rules the keys and the tokens.</summary>
    private bool IsCurrent(DateTime? notBefore, DateTime? expires, SecurityToken token, TokenValidationParameters parameters)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        return expires is { } end
            && now <= Shifted(end, parameters.ClockSkew)
            && (notBefore is not { } start || now >= Shifted(start, -parameters.ClockSkew));
    }

    /// <summary>A moment moved by the skew, held within what a date can express.</summary>
    private static DateTime Shifted(DateTime moment, TimeSpan by) =>
        by > TimeSpan.Zero
            ? moment > DateTime.MaxValue - by ? DateTime.MaxValue : moment + by
            : moment < DateTime.MinValue - by ? DateTime.MinValue : moment + by;

    private Uri? ServiceUrlOf(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.IsDefaultPort
        && uri.UserInfo.Length == 0
        && uri.Query.Length == 0
        && uri.Fragment.Length == 0
        && _serviceHosts.Contains(uri.Host)
            ? uri
            : null;

    private static List<string> EndorsementsOf(JsonWebKey key)
    {
        key.AdditionalData.TryGetValue("endorsements", out var value);
        return value switch
        {
            JsonElement { ValueKind: JsonValueKind.Array } list =>
                [.. list.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString() ?? string.Empty)],
            IEnumerable<object> items => [.. items.Select(i => i?.ToString() ?? string.Empty)],
            _ => [],
        };
    }

    private static TeamsTokenResult Refused(string reason) => new(TeamsTokenOutcome.Refused, reason);
}
