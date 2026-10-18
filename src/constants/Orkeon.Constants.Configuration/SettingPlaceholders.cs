namespace Orkeon.Constants.Configuration;

/// <summary>A setting that refuses a variable reference, with its correction.</summary>
/// <param name="Path">The setting path pattern.</param>
/// <param name="Correction">The correction to state.</param>
public sealed record SettingPlaceholderSetting(string Path, string Correction);

/// <summary>The settings that refuse a variable reference.</summary>
public static class SettingPlaceholders
{
    /// <summary>Gets the list.</summary>
    public static IReadOnlyList<SettingPlaceholderSetting> Settings { get; } = Build();

    private const string Value = "Write the value, or keep it out of the file through the environment layer <ENV>.";
    private const string Name = "Write the name alone, without ${ and }.";
    private const string ApiKey = "Write \"ApiKeyEnvVar\": \"<NAME>\" instead, naming the variable that holds the key.";

    /// <summary>Gets the correction of a concrete path, or null.</summary>
    /// <param name="settingPath">The concrete setting path.</param>
    /// <returns>The correction or null.</returns>
    public static string? CorrectionFor(string settingPath)
    {
        ArgumentNullException.ThrowIfNull(settingPath);
        var segments = settingPath.Split(':');
        foreach (var setting in Settings)
        {
            if (Matches(setting.Path.Split(':'), segments))
            {
                return setting.Correction
                    .Replace("<LAST>", segments[^1], StringComparison.Ordinal)
                    .Replace("<ENV>", "ORKEON_" + settingPath.Replace(":", "__", StringComparison.Ordinal), StringComparison.Ordinal);
            }
        }

        return null;
    }

    private static bool Matches(string[] pattern, string[] segments)
    {
        if (pattern.Length != segments.Length)
            return false;
        for (var i = 0; i < pattern.Length; i++)
        {
            if (pattern[i] != "*" && !string.Equals(pattern[i], segments[i], StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private static SettingPlaceholderSetting[] Build()
    {
        const string env = "Remove the entry: the server inherits the environment Orkeon runs in, set <LAST> there.";
        const string secret = "Remove the entry and set ORKEON_<LAST>, which the secret chain reads first.";
        const string brave = "Remove the entry, Orkeon reads the variable.";
        string[] values =
        [
            "MCP:Servers:*:Command", "MCP:Servers:*:Args:*", "MCP:Servers:*:Url",
            "Orkeon:Redis:ConnectionString", "Orkeon:Sqlite:ConnectionString",
            "RaggableTree:Embedding:ApiKey", "Orkeon:Pinecone:ApiKey", "Orkeon:LanceDb:ApiKey",
            "Llm:BaseUrl", "Llm:Profiles:*:BaseUrl", "RaggableTree:Embedding:BaseUrl", "Orkeon:ChromaDb:BaseUrl",
            "Orkeon:Pinecone:Host", "Orkeon:LanceDb:Endpoint", "Orkeon:Rag:WebFallback:Endpoint", "Telemetry:OtlpEndpoint",
            "Security:Vault:AzureKeyVaultUri", "A2A:Security:AzureAD:Authority", "A2A:Security:Oidc:Authority",
        ];
        string[] names =
        [
            "Llm:ApiKeyEnvVar", "Llm:Profiles:*:ApiKeyEnvVar", "Orkeon:Rag:WebFallback:ApiKeyEnvVar",
            "Orkeon:Host:Discord:TokenEnvironmentVariable", "A2A:Security:ApiKeySecretNames:*", "Orkeon:Encryption:SecretName",
        ];
        List<SettingPlaceholderSetting> list =
        [
            new("MCP:Servers:*:Env:*", env),
            new("Llm:ApiKey", ApiKey),
            new("Llm:Profiles:*:ApiKey", ApiKey),
            new("Secrets:*", secret),
            new("BRAVE_API_KEY", brave),
        ];
        list.AddRange(values.Select(path => new SettingPlaceholderSetting(path, Value)));
        list.AddRange(names.Select(path => new SettingPlaceholderSetting(path, Name)));
        return [.. list];
    }
}
