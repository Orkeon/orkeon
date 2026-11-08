using Orkeon.Tools.Abstractions.Auth;

namespace Orkeon.Infrastructure.MCP.Auth;

/// <summary>What an OAuth server needs from the host: the token endpoint's HTTP client, the person to show a device code to, the clock.</summary>
/// <param name="HttpClientFactory">Builds the <c>orkeon-mcp-oauth</c> client.</param>
/// <param name="Interaction">Shows a device sign-in code; null in a host without a terminal (<c>orkeon-host</c>).</param>
/// <param name="Time">The clock of the token expiry.</param>
internal sealed record McpOAuthServices(IHttpClientFactory? HttpClientFactory, IOAuthLoginInteraction? Interaction, TimeProvider Time);

/// <summary>Validates a server's headers and auth settings and builds its request authenticator.</summary>
internal static class McpRequestAuthenticators
{
    private const string EnvPrefix = "env:";

    /// <summary>Headers the transport owns: a static value would overwrite the one it computes.</summary>
    private static readonly HashSet<string> ReservedHeaders =
        new(["Accept", "Content-Type", "Mcp-Session-Id", McpProtocol.ProtocolVersionHeader, McpProtocol.MethodHeader,
            McpProtocol.NameHeader, "Last-Event-ID"], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the authenticator for <paramref name="config"/>, or <see langword="null"/> when it
    /// declares no header and no auth.
    /// </summary>
    /// <exception cref="McpServerConfigurationException">The configuration is invalid; the key is named.</exception>
    public static IMcpRequestAuthenticator? Create(string serverId, McpServerConfig config, Func<string, string?>? environment = null, McpOAuthServices? oauth = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        environment ??= Environment.GetEnvironmentVariable;
        var prefix = $"MCP:Servers:{serverId}";
        var links = new List<IMcpRequestAuthenticator>();

        if (config.CallTimeout <= TimeSpan.Zero)
            throw new McpServerConfigurationException($"{prefix}:CallTimeout", $"{prefix}:CallTimeout must be a positive duration.");
        if (config.ConnectTimeout <= TimeSpan.Zero)
            throw new McpServerConfigurationException($"{prefix}:ConnectTimeout", $"{prefix}:ConnectTimeout must be a positive duration.");

        // Credentials a stdio server could never receive: refused rather than silently not sent.
        if (config.Transport == McpTransportType.Stdio)
        {
            if (config.Auth.Type != McpAuthType.None)
                throw new McpServerConfigurationException($"{prefix}:Auth", $"{prefix}:Auth applies to the Sse transport only; this server is Stdio.");
            if (config.Headers.Count > 0)
                throw new McpServerConfigurationException($"{prefix}:Headers", $"{prefix}:Headers applies to the Sse transport only; this server is Stdio.");
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var secretHeader = false;
        foreach (var (name, raw) in config.Headers)
        {
            var key = $"{prefix}:Headers:{name}";
            if (string.Equals(name, "Authorization", StringComparison.OrdinalIgnoreCase))
            {
                throw new McpServerConfigurationException(key,
                    $"{key} is not accepted: a credential is never written in the settings. Use {prefix}:Auth with TokenEnv.");
            }

            if (ReservedHeaders.Contains(name))
            {
                throw new McpServerConfigurationException(key,
                    $"{key} is not accepted: the transport sets {name} itself and a fixed value would overwrite it.");
            }

            var fromEnvironment = raw.StartsWith(EnvPrefix, StringComparison.OrdinalIgnoreCase);
            secretHeader |= fromEnvironment;
            headers[name] = fromEnvironment
                ? Read(key, raw[EnvPrefix.Length..], environment)
                : raw;
        }

        // A credential (an Auth, or a header read from the environment, where secrets live) goes to the server's own
        // address with every request: that address must not carry it in clear to another host.
        if (config.Transport == McpTransportType.Sse && config.Url is { } url && (config.Auth.Type != McpAuthType.None || secretHeader))
            Endpoint($"{prefix}:Url", url, "carries the credentials of Auth or of an env: header");

        if (headers.Count > 0)
            links.Add(new StaticHeaderAuthenticator(headers));

        var auth = config.Auth;
        switch (auth.Type)
        {
            case McpAuthType.None:
                break;
            case McpAuthType.Bearer:
                var tokenKey = $"{prefix}:Auth:TokenEnv";
                links.Add(new EnvBearerAuthenticator(tokenKey, Checked(tokenKey, auth.TokenEnv, environment), environment));
                break;
            case McpAuthType.Basic:
                var userKey = $"{prefix}:Auth:UserEnv";
                var passwordKey = $"{prefix}:Auth:PasswordEnv";
                links.Add(new EnvBasicAuthenticator(
                    userKey, Checked(userKey, auth.UserEnv, environment),
                    passwordKey, Checked(passwordKey, auth.PasswordEnv, environment),
                    environment));
                break;
            case McpAuthType.OAuth2ClientCredentials:
            case McpAuthType.OAuth2Device:
                links.Add(CreateOAuth(serverId, prefix, auth, environment, oauth));
                break;
            default:
                throw new McpServerConfigurationException($"{prefix}:Auth:Type", $"{prefix}:Auth:Type '{auth.Type}' is not supported.");
        }

        return links.Count switch
        {
            0 => null,
            1 => links[0],
            _ => new ChainedMcpRequestAuthenticator(links),
        };
    }

    private static OAuthMcpAuthenticator CreateOAuth(
        string serverId, string prefix, McpAuthConfig auth, Func<string, string?> environment, McpOAuthServices? oauth)
    {
        var device = auth.Type == McpAuthType.OAuth2Device;
        var typeName = device ? "oauth2-device" : "oauth2-client-credentials";
        var tokenEndpoint = Endpoint($"{prefix}:Auth:TokenUrl", auth.TokenUrl);
        Uri? deviceEndpoint = device ? Endpoint($"{prefix}:Auth:DeviceAuthorizationUrl", auth.DeviceAuthorizationUrl) : null;
        if (string.IsNullOrWhiteSpace(auth.ClientId))
            throw new McpServerConfigurationException($"{prefix}:Auth:ClientId", $"{prefix}:Auth:ClientId must name the OAuth client.");

        var secretKey = $"{prefix}:Auth:ClientSecretEnv";
        var secretVariable = device && string.IsNullOrWhiteSpace(auth.ClientSecretEnv)
            ? null
            : Checked(secretKey, auth.ClientSecretEnv, environment);

        if (device && oauth?.Interaction is null)
        {
            throw new McpServerConfigurationException($"{prefix}:Auth:Type",
                $"{prefix}:Auth:Type '{typeName}' is refused for MCP server '{serverId}': this host has no terminal to show the sign-in code on; run it with orkeon run or the REPL.");
        }

        if (oauth?.HttpClientFactory is null)
        {
            throw new McpServerConfigurationException($"{prefix}:Auth:Type",
                $"{prefix}:Auth:Type '{typeName}' needs an HTTP client factory for MCP server '{serverId}'.");
        }

        var settings = new OAuthSettings
        {
            ClientId = auth.ClientId,
            ClientSecretEnvVar = secretVariable,
            Flow = device ? OAuthFlow.DeviceCode : OAuthFlow.ClientCredentials,
            TokenEndpoint = tokenEndpoint,
            DeviceCodeEndpoint = deviceEndpoint,
            Scopes = [.. auth.Scopes],
            Audience = string.IsNullOrWhiteSpace(auth.Audience) ? null : auth.Audience,
        };
        var client = new OAuth2Client(oauth.HttpClientFactory.CreateClient(McpServiceExtensions.OAuthHttpClientName), oauth.Time);
        return new OAuthMcpAuthenticator(serverId, client, settings, secretVariable, environment, oauth.Interaction, oauth.Time);
    }

    /// <summary>An absolute address, HTTPS or loopback HTTP: a credential is never posted in clear to another host.</summary>
    private static Uri Endpoint(string key, Uri? value, string? reason = null)
    {
        if (value is null)
            throw new McpServerConfigurationException(key, $"{key} must give the address of the endpoint.");
        var uri = value;
        if (!uri.IsAbsoluteUri
            || !(uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)))
        {
            throw new McpServerConfigurationException(key, reason is null
                ? $"{key} must be an absolute https address (http only on loopback)."
                : $"{key} {reason}: it must be an absolute https address (http only on loopback).");
        }

        return uri;
    }

    /// <summary>The variable name, once the key names one and it holds a value.</summary>
    private static string Checked(string key, string? variable, Func<string, string?> environment)
    {
        if (string.IsNullOrWhiteSpace(variable))
            throw new McpServerConfigurationException(key, $"{key} must name an environment variable.");
        Read(key, variable, environment);
        return variable;
    }

    private static string Read(string key, string variable, Func<string, string?> environment)
    {
        var value = environment(variable);
        if (string.IsNullOrEmpty(value))
        {
            throw new McpServerConfigurationException(key,
                $"{key} reads the environment variable '{variable}', which is not set.");
        }

        return value;
    }
}
