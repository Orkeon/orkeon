using System.Collections.Frozen;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Orkeon.Tools.Abstractions.Auth;
using Orkeon.Tools.Chat;

namespace Orkeon.Host.Gateway;

/// <summary>
/// Which addresses a file of Teams may have, and what is sent to them: the tenant's SharePoint with the application's own
/// token, or, until Microsoft ends them, a temporary address without any. A token is asked for a host of the rule only, and
/// sent to that host only.
/// </summary>
internal sealed partial class TeamsFileAccess : IDisposable
{
    /// <summary>When Microsoft ends the temporary addresses.</summary>
    internal static readonly DateTimeOffset TemporaryUntil = new(2027, 3, 1, 0, 0, 0, TimeSpan.Zero);

    internal const string ForeignAddress = "its address is not a file's on the tenant's SharePoint the host names";
    internal const string NoNames = "the host names no SharePoint to exchange files with (Teams:SharePointNames)";
    internal const string NoCertificate = "SharePoint needs the host's certificate, and it has none (Teams:CertificatePathEnvironmentVariable)";
    internal const string NoSignIn = "the host could not sign in to SharePoint";

    private readonly TeamsChannelOptions _options;
    private readonly Func<CancellationToken, Task<string>>? _certificate;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly OAuth2Client _oauth;
    private readonly FrozenSet<string> _hosts;
    private readonly FrozenSet<string> _personal;
    private readonly FrozenDictionary<string, HostToken> _tokens;

    /// <summary>The access to one tenant's files.</summary>
    /// <param name="options">The channel's configuration.</param>
    /// <param name="http">The transport that reaches the sign-in service.</param>
    /// <param name="certificate">Reads the application's certificate; null when there is none.</param>
    /// <param name="time">The clock.</param>
    /// <param name="logger">Where failures are logged, without an address or anything a service answered.</param>
    public TeamsFileAccess(
        TeamsChannelOptions options, HttpClient http, Func<CancellationToken, Task<string>>? certificate, TimeProvider time, ILogger logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _certificate = certificate;
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _oauth = new OAuth2Client(http, time);
        _hosts = options.SharePointHosts.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        _personal = _hosts.Where(h => h.EndsWith("-my.sharepoint.com", StringComparison.OrdinalIgnoreCase)).ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        _tokens = _hosts.ToFrozenDictionary(h => h, _ => new HostToken(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Why no file can be exchanged at all, null when one may be.</summary>
    public string? Unavailable =>
        _hosts.Count == 0 ? NoNames
        : _certificate is null && _time.GetUtcNow() >= TemporaryUntil ? NoCertificate
        : null;

    /// <summary>An address of the tenant's SharePoint, null when it is not one, or is of another tenant. Nothing is sent to it.</summary>
    public Uri? Address(TeamsFileAddress file) => OfTenant(file, _hosts);

    /// <summary>Where a file of the tenant is read, null when it is anything else.</summary>
    public Uri? Download(TeamsFileAddress file) => OfTenant(file, _hosts) is { } address && TeamsFilePaths.IsDownload(address) ? address : null;

    /// <summary>Where a file is put in the OneDrive of a person of the tenant, null when it is anything else.</summary>
    public Uri? Upload(TeamsFileAddress file) => OfTenant(file, _personal) is { } address && TeamsFilePaths.IsUpload(address) ? address : null;

    private Uri? OfTenant(TeamsFileAddress file, FrozenSet<string> hosts)
    {
        ArgumentNullException.ThrowIfNull(file);
        return file.TenantId is null || string.Equals(file.TenantId, _options.TenantId, StringComparison.OrdinalIgnoreCase)
            ? Address(file.Written, hosts)
            : null;
    }

    /// <summary>
    /// An address on one of <paramref name="hosts"/>, null when it is anything else. It is kept as it was written: SharePoint
    /// refuses one that was encoded again.
    /// </summary>
    public static Uri? Address(string? written, FrozenSet<string> hosts)
    {
        ArgumentNullException.ThrowIfNull(hosts);
        return written is { Length: > 0 and <= 8192 }
            && written.All(c => c is > ' ' and <= '~' and not ('\\' or '#'))
            && Uri.TryCreate(written, new UriCreationOptions { DangerousDisablePathAndQueryCanonicalization = true }, out var address)
            && address.IsAbsoluteUri
            && address.Scheme == Uri.UriSchemeHttps
            && address.IsDefaultPort
            && address.UserInfo.Length == 0
            && hosts.Contains(address.Host)
                ? address
                : null;
    }

    /// <summary>What to send to <paramref name="address"/>, which passed the rule: a token, or nothing to a temporary address; else why not.</summary>
    public async Task<(AuthenticationHeaderValue? Header, string? Refusal)> AuthorizeAsync(Uri address, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (!_tokens.TryGetValue(address.Host, out var held))
            return (null, ForeignAddress);

        if (_certificate is not null && await TokenAsync(address.Host, held, _certificate, ct).ConfigureAwait(false) is { } token)
            return (new AuthenticationHeaderValue("Bearer", token), null);
        if (_time.GetUtcNow() < TemporaryUntil && IsTemporary(address))
            return (null, null);
        return (null, _certificate is null ? NoCertificate : NoSignIn);
    }

    /// <summary>Forgets the token of a host that refused it: the next file asks for another.</summary>
    public async Task ForgetAsync(string host, CancellationToken ct)
    {
        if (!_tokens.TryGetValue(host, out var held))
            return;

        await held.Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            held.Token = null;
        }
        finally
        {
            held.Gate.Release();
        }
    }

    private static bool IsTemporary(Uri address)
    {
        var query = address.AbsoluteUri.IndexOf('?', StringComparison.Ordinal);
        return query >= 0 && address.AbsoluteUri[(query + 1)..].Split('&')
            .Any(p => string.Equals(p.Split('=', 2)[0], "tempauth", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<string?> TokenAsync(string host, HostToken held, Func<CancellationToken, Task<string>> certificate, CancellationToken ct)
    {
        await held.Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (held.Token is { } kept && _oauth.IsFresh(kept))
                return kept.AccessToken;

            var settings = new OAuthSettings
            {
                ClientId = _options.AppId,
                Flow = OAuthFlow.ClientCredentials,
                TokenEndpoint = new Uri($"https://login.microsoftonline.com/{_options.TenantId}/oauth2/v2.0/token"),
                Scopes = [$"https://{host}/.default"],
            };

            // Read again for each token: a certificate is replaced without a restart.
            var assertion = OAuthClientAssertion.FromCertificatePem(await certificate(ct).ConfigureAwait(false));
            var tokens = await _oauth.RequestClientCredentialsAsync(settings, assertion, ct).ConfigureAwait(false);
            held.Token = tokens;
            return tokens.AccessToken;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (ChatToolException ex)
        {
            // A sentence naming the variable, never the path.
            LogNoCertificate(ex.Message);
            return null;
        }
        catch (OAuthException ex)
        {
            // Its message may quote the service.
            LogSignInFailed(ex.Failure.ToString());
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException)
        {
            LogSignInFailed(ex.GetType().Name);
            return null;
        }
        finally
        {
            held.Gate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var held in _tokens.Values)
            held.Gate.Dispose();
    }

    /// <summary>The token of one host, asked for by one caller at a time: a host whose sign-in waits holds up no other.</summary>
    private sealed class HostToken
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public OAuthTokenSet? Token { get; set; }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The certificate of the Teams application could not be read: {Reason}")]
    private partial void LogNoCertificate(string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Teams application could not sign in to SharePoint with its certificate ({Reason})")]
    private partial void LogSignInFailed(string reason);
}
