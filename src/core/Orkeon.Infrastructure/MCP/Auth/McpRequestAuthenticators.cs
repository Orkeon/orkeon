namespace Orkeon.Infrastructure.MCP.Auth;

/// <summary>Validates a server's headers and auth settings and builds its request authenticator.</summary>
internal static class McpRequestAuthenticators
{
    private const string EnvPrefix = "env:";

    /// <summary>
    /// Returns the authenticator for <paramref name="config"/>, or <see langword="null"/> when it
    /// declares no header and no auth.
    /// </summary>
    /// <exception cref="McpServerConfigurationException">The configuration is invalid; the key is named.</exception>
    public static IMcpRequestAuthenticator? Create(string serverId, McpServerConfig config, Func<string, string?>? environment = null)
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
        foreach (var (name, raw) in config.Headers)
        {
            var key = $"{prefix}:Headers:{name}";
            if (string.Equals(name, "Authorization", StringComparison.OrdinalIgnoreCase))
            {
                throw new McpServerConfigurationException(key,
                    $"{key} is not accepted: a credential is never written in the settings. Use {prefix}:Auth with TokenEnv.");
            }

            headers[name] = raw.StartsWith(EnvPrefix, StringComparison.OrdinalIgnoreCase)
                ? Read(key, raw[EnvPrefix.Length..], environment)
                : raw;
        }

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
