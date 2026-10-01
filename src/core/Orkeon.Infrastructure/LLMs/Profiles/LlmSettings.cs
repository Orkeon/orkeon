using System.Globalization;
using Microsoft.Extensions.Configuration;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Constants.Configuration;
using Orkeon.Domain.Constants.Llm;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Infrastructure.LLMs.Profiles;

/// <summary>
/// Reads the host's LLM settings: the <c>Llm</c> section — the default profile — and its named
/// profiles under <c>Llm:Profiles:&lt;name&gt;</c>, each of the same shape (GAP-17). One reader
/// for both, so a profile accepts exactly the keys the default section does: <c>BaseUrl</c>,
/// <c>ApiKey</c>, <c>Model</c>, <c>Temperature</c>, <c>MaxTokens</c>, <c>TimeoutSeconds</c>,
/// <c>MaxRetries</c>, <c>Thinking:{Enabled,Effort}</c> and <c>Grammar</c>.
/// </summary>
public static class LlmSettings
{
    /// <summary>
    /// Whether the host configures a default provider: an <c>Llm</c> section with at least one
    /// key besides <c>Profiles</c>. A section holding profiles alone leaves the default unset.
    /// </summary>
    /// <param name="configuration">The host configuration.</param>
    /// <returns>True when the <c>Llm</c> section describes a provider.</returns>
    public static bool HasDefault(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return configuration.GetSection(ConfigurationKeys.LlmSection).GetChildren()
            .Any(c => !string.Equals(c.Key, ConfigurationKeys.LlmProfiles, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The names under <c>Llm:Profiles</c>, in configuration order, unvalidated.</summary>
    /// <param name="configuration">The host configuration.</param>
    /// <returns>The declared profile names.</returns>
    public static IReadOnlyList<string> ProfileNames(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return [.. ProfilesSection(configuration).GetChildren().Select(c => c.Key)];
    }

    /// <summary>
    /// Reads the default profile — the <c>Llm</c> section — into the configuration its provider
    /// is built with.
    /// </summary>
    /// <param name="configuration">The host configuration.</param>
    /// <returns>The default profile's configuration.</returns>
    /// <exception cref="InvalidOperationException">A value cannot be read (an invalid <c>BaseUrl</c>).</exception>
    public static LlmConfig ReadDefault(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return Read(configuration.GetSection(ConfigurationKeys.LlmSection), strict: false);
    }

    /// <summary>
    /// Reads every profile under <c>Llm:Profiles</c>, validated: a reserved name,
    /// an invalid <c>BaseUrl</c> or a value that is not a number where one is expected fails the
    /// host start with the key to fix, rather than the first crew that names the profile.
    /// </summary>
    /// <param name="configuration">The host configuration.</param>
    /// <returns>Each profile's name and configuration, in configuration order.</returns>
    /// <exception cref="InvalidOperationException">A profile is invalid.</exception>
    public static IReadOnlyList<(string Name, LlmConfig Config)> ReadProfiles(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var profiles = new List<(string, LlmConfig)>();
        foreach (var section in ProfilesSection(configuration).GetChildren())
        {
            if (LlmProfiles.IsDefault(section.Key))
                throw new InvalidOperationException(
                    $"{section.Path}: '{LlmProfiles.Default}' is the reserved name of the default profile, " +
                    $"which is the {ConfigurationKeys.LlmSection} section itself. Rename the profile.");

            profiles.Add((section.Key, Read(section, strict: true)));
        }

        return profiles;
    }

    private static IConfigurationSection ProfilesSection(IConfiguration configuration) =>
        configuration.GetSection(ConfigurationKeys.LlmSection).GetSection(ConfigurationKeys.LlmProfiles);

    /// <summary>
    /// Reads one section of the <c>Llm</c> shape. The default section keeps its historical
    /// leniency — an unreadable number falls back to its default; a profile is
    /// <paramref name="strict"/>: it is new configuration, and a silent default would run a crew
    /// on settings its author never wrote.
    /// </summary>
    private static LlmConfig Read(IConfigurationSection section, bool strict)
    {
        // The one default, not a literal: LlmConfig, AgentBuilder and Studio's presets all
        // read LlmDefaults.DefaultModelName, so a hardcoded model here gave an appsettings
        // whose Llm section omits Model a different model from every other entry point.
        var config = LlmConfig.Create(section["Model"] ?? LlmDefaults.DefaultModelName) with
        {
            BaseUrl = ReadBaseUrl(section),
#pragma warning disable CS0618
            ApiKey = section["ApiKey"],
#pragma warning restore CS0618
            // Invariant parse: configuration values are written invariant ("0.7"), and a
            // culture-sensitive read turns that into 7 on a comma-decimal locale (fr-FR).
            Temperature = ReadDouble(section, "Temperature", strict) ?? LlmDefaults.DefaultTemperature,
            // Absent = not pinned: the provider sends the model's documented maximum (LLM-10).
            MaxTokens = ReadInt(section, "MaxTokens", strict),
            TimeoutSeconds = ReadInt(section, "TimeoutSeconds", strict) ?? 30,
            Thinking = ReadThinking(section),
            // Llm:Grammar — the endpoint honours a GBNF grammar (llama.cpp-compatible server).
            GrammarEnabled = bool.TryParse(section[ConfigurationKeys.LlmGrammar], out var grammar) && grammar,
        };

        return ReadInt(section, "MaxRetries", strict) is { } maxRetries
            ? config with { MaxRetries = Math.Max(0, maxRetries) }
            : config;
    }

    private static Uri? ReadBaseUrl(IConfigurationSection section)
    {
        var raw = section["BaseUrl"];
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        return Uri.TryCreate(raw, UriKind.Absolute, out var url)
            ? url
            : throw new InvalidOperationException(
                $"{section.Path}:BaseUrl is not an absolute URL: '{raw}'.");
    }

    private static double? ReadDouble(IConfigurationSection section, string key, bool strict)
    {
        var raw = section[key];
        if (raw is null)
            return null;
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return value;
        return strict ? throw NotANumber(section, key, raw) : null;
    }

    private static int? ReadInt(IConfigurationSection section, string key, bool strict)
    {
        var raw = section[key];
        if (raw is null)
            return null;
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            return value;
        return strict ? throw NotANumber(section, key, raw) : null;
    }

    private static InvalidOperationException NotANumber(IConfigurationSection section, string key, string raw) =>
        new($"{section.Path}:{key} is not a number: '{raw}'.");

    private static LlmThinkingConfig? ReadThinking(IConfigurationSection section)
    {
        // Llm:Thinking:{Enabled,Effort} — forwarded to thinking-capable providers
        // (DeepSeek, Z.AI GLM) as the `thinking` block + `reasoning_effort` field.
        var thinking = section.GetSection(ConfigurationKeys.ThinkingSection);
        if (!thinking.Exists())
            return null;

        return new LlmThinkingConfig
        {
            Enabled = bool.TryParse(thinking["Enabled"], out var enabled) ? enabled : null,
            Effort = thinking["Effort"],
        };
    }
}
