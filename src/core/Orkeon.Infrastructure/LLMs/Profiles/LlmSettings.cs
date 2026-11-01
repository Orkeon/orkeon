using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.Extensions.Configuration;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Constants.Configuration;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.Configuration;

namespace Orkeon.Infrastructure.LLMs.Profiles;

/// <summary>
/// Reads the host's LLM settings: the <c>Llm</c> section — the default profile — and its named
/// profiles under <c>Llm:Profiles:&lt;name&gt;</c>, each of the same shape (GAP-17). One reader
/// for both, so a profile accepts exactly the keys the default section does: <c>BaseUrl</c>,
/// <c>ApiKey</c>, <c>ApiKeyEnvVar</c>, <c>Model</c>, <c>Temperature</c>, <c>MaxTokens</c>,
/// <c>TimeoutSeconds</c>, <c>StreamIdleSeconds</c>, <c>MaxRetries</c>, <c>MaxRateLimitWait</c>, <c>CallMode</c>,
/// <c>Thinking:{Enabled,Effort}</c> and <c>Grammar</c>.
/// A key left out sets nothing: no <c>Temperature</c> sends none, and the model applies its own
/// (GAP-36) — the reader filled in the engine's 0.7, which the default models of OpenAI and
/// Anthropic refuse.
/// <para>
/// The key (STUDIO-49): an <c>ApiKey</c> the configuration resolves — the settings file, an
/// <c>ORKEON_</c> variable, the environment of a Studio launch — wins; otherwise
/// <c>ApiKeyEnvVar</c> names the environment variable holding it, read in the process, then in
/// the user's persistent scope on Windows, never copied into the process. A value left blank
/// reads as absent: a Studio launch blanks the default fields its team's setting does not set.
/// </para>
/// </summary>
public static class LlmSettings
{
    /// <summary>
    /// Whether the host configures a default provider: an <c>Llm</c> section with at least one
    /// key besides <c>Profiles</c> that holds a value. A section holding profiles alone leaves the
    /// default unset, and so does one whose every value is blank — a Studio launch on the « no
    /// model » setting blanks the whole default, and its team runs on the echo provider.
    /// </summary>
    /// <param name="configuration">The host configuration.</param>
    /// <returns>True when the <c>Llm</c> section describes a provider.</returns>
    public static bool HasDefault(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return configuration.GetSection(ConfigurationKeys.LlmSection).GetChildren()
            .Any(c => !string.Equals(c.Key, ConfigurationKeys.LlmProfiles, StringComparison.OrdinalIgnoreCase)
                      && HoldsValue(c));
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
    /// <exception cref="InvalidOperationException">A value cannot be read: an invalid <c>BaseUrl</c>, a
    /// number or a switch that is none, an <c>ApiKeyEnvVar</c> that is no variable name, an <c>ApiKey</c>
    /// written as a <c>${NAME}</c> placeholder.</exception>
    public static LlmConfig ReadDefault(IConfiguration configuration) =>
        ReadDefault(configuration, LlmKeyEnvironment.Machine);

    /// <summary><see cref="ReadDefault(IConfiguration)"/> over an explicit environment.</summary>
    internal static LlmConfig ReadDefault(IConfiguration configuration, LlmKeyEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);
        return Read(configuration.GetSection(ConfigurationKeys.LlmSection), strict: true, environment);
    }

    /// <summary>
    /// Reads every profile under <c>Llm:Profiles</c>, validated: a reserved name,
    /// an invalid <c>BaseUrl</c> or a value that is not a number where one is expected fails the
    /// host start with the key to fix, rather than the first crew that names the profile.
    /// </summary>
    /// <param name="configuration">The host configuration.</param>
    /// <returns>Each profile's name and configuration, in configuration order.</returns>
    /// <exception cref="InvalidOperationException">A profile is invalid.</exception>
    public static IReadOnlyList<(string Name, LlmConfig Config)> ReadProfiles(IConfiguration configuration) =>
        ReadProfiles(configuration, LlmKeyEnvironment.Machine);

    /// <summary><see cref="ReadProfiles(IConfiguration)"/> over an explicit environment.</summary>
    internal static IReadOnlyList<(string Name, LlmConfig Config)> ReadProfiles(
        IConfiguration configuration,
        LlmKeyEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);
        var profiles = new List<(string, LlmConfig)>();
        foreach (var section in ProfilesSection(configuration).GetChildren())
        {
            if (LlmProfiles.IsDefault(section.Key))
                throw new InvalidOperationException(
                    $"{section.Path}: '{LlmProfiles.Default}' is the reserved name of the default profile, " +
                    $"which is the {ConfigurationKeys.LlmSection} section itself. Rename the profile.");

            profiles.Add((section.Key, Read(section, strict: true, environment)));
        }

        return profiles;
    }

    /// <summary>
    /// Where the API key of <paramref name="section"/> — the <c>Llm</c> section or one of its
    /// profiles — comes from, for a startup line or a diagnostic: the configuration
    /// (<c>ApiKey</c>), the variable <c>ApiKeyEnvVar</c> names — in the process environment or in
    /// the user's — or none, and then whether a reference named a variable that is not set.
    /// Never the key, and never the variable's name: a key pasted into <c>ApiKeyEnvVar</c> by
    /// mistake must not reach a log, and the configuration path says which setting to look at.
    /// </summary>
    /// <param name="section">A section of the <c>Llm</c> shape.</param>
    /// <returns>One line, such as "from the variable named by Llm:ApiKeyEnvVar (user environment)".</returns>
    /// <exception cref="InvalidOperationException">The section's key settings cannot be read (see <see cref="ReadDefault(IConfiguration)"/>).</exception>
    public static string DescribeApiKey(IConfigurationSection section) =>
        DescribeApiKey(section, LlmKeyEnvironment.Machine);

    /// <summary><see cref="DescribeApiKey(IConfigurationSection)"/> over an explicit environment.</summary>
    internal static string DescribeApiKey(IConfigurationSection section, LlmKeyEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(environment);
        var reference = $"{section.Path}:{ConfigurationKeys.LlmApiKeyEnvVar}";
        return ResolveApiKey(section, environment).Source switch
        {
            ApiKeySource.Configuration => $"from configuration ({section.Path}:ApiKey)",
            ApiKeySource.ProcessEnvironment => $"from the variable named by {reference} (process environment)",
            ApiKeySource.UserEnvironment => $"from the variable named by {reference} (user environment)",
            ApiKeySource.Unresolved => $"none — the variable named by {reference} is not set",
            _ => "none",
        };
    }

    /// <summary>
    /// The configuration paths of the <c>ApiKeyEnvVar</c> references — the default's, then each
    /// profile's, in configuration order — that name a variable set nowhere the runtime reads,
    /// with no <c>ApiKey</c> to mask them: a host warns once per path, and every call on such a
    /// profile answers that an API key is required. The paths only, never the names they hold.
    /// </summary>
    /// <param name="configuration">The host configuration.</param>
    /// <returns>Paths such as <c>Llm:Profiles:z-ai:ApiKeyEnvVar</c>; empty when every reference resolves.</returns>
    /// <exception cref="InvalidOperationException">A section's key settings cannot be read.</exception>
    public static IReadOnlyList<string> UnresolvedApiKeyReferences(IConfiguration configuration) =>
        UnresolvedApiKeyReferences(configuration, LlmKeyEnvironment.Machine);

    /// <summary><see cref="UnresolvedApiKeyReferences(IConfiguration)"/> over an explicit environment.</summary>
    internal static IReadOnlyList<string> UnresolvedApiKeyReferences(IConfiguration configuration, LlmKeyEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);
        return
        [
            .. new[] { configuration.GetSection(ConfigurationKeys.LlmSection) }
                .Concat(ProfilesSection(configuration).GetChildren())
                .Where(section => ResolveApiKey(section, environment).Source == ApiKeySource.Unresolved)
                .Select(section => $"{section.Path}:{ConfigurationKeys.LlmApiKeyEnvVar}"),
        ];
    }

    private static IConfigurationSection ProfilesSection(IConfiguration configuration) =>
        configuration.GetSection(ConfigurationKeys.LlmSection).GetSection(ConfigurationKeys.LlmProfiles);

    private static bool HoldsValue(IConfigurationSection section) =>
        !string.IsNullOrWhiteSpace(section.Value) || section.GetChildren().Any(HoldsValue);

    /// <summary>
    /// Reads one section of the <c>Llm</c> shape, the default as strictly as a profile (GAP-40): a
    /// number or a switch it cannot read refuses the start, naming its key. The default used to fall
    /// back on its default for an unreadable number — a <c>TimeoutSeconds</c> of <c>"600s"</c> ran on
    /// 30 s — and a switch that was no boolean read as unset, in profiles too.
    /// </summary>
    private static LlmConfig Read(IConfigurationSection section, bool strict, LlmKeyEnvironment environment)
    {
        // No Model: the section names none, and its provider runs its own default (GAP-18) —
        // OpenAI's default model, filled in here, went to a DeepSeek endpoint inferred from
        // BaseUrl as well, which refused it.
        var model = section["Model"];
        var config = (string.IsNullOrWhiteSpace(model) ? LlmConfig.OnProfile() : LlmConfig.Create(model)) with
        {
            BaseUrl = ReadBaseUrl(section),
            ApiKey = ResolveApiKey(section, environment).Key,
            // Invariant parse: configuration values are written invariant ("0.7"), and a
            // culture-sensitive read turns that into 7 on a comma-decimal locale (fr-FR).
            // Absent = not set: none is sent, the model applies its own (GAP-36).
            Temperature = ReadDouble(section, "Temperature", strict),
            // Absent = not pinned: the provider sends the model's documented maximum (LLM-10).
            MaxTokens = ReadInt(section, "MaxTokens", strict),
            // Absent = not pinned: the provider runs on LlmDefaults.DefaultTimeoutSeconds (30 s).
            TimeoutSeconds = ReadInt(section, "TimeoutSeconds", strict),
            // Absent = not bounded: only TimeoutSeconds bounds a streamed call (LLM-12).
            StreamIdleSeconds = ReadInt(section, "StreamIdleSeconds", strict),
            // Absent = Auto: stream, falling back on a buffered call when the provider refuses it.
            CallMode = ReadCallMode(section),
            Thinking = ReadThinking(section),
            // Llm:Cache — the default prompt cache of every agent that sets none of its own.
            Cache = ReadCache(section),
            // Llm:Grammar — the endpoint honours a GBNF grammar (llama.cpp-compatible server).
            GrammarEnabled = ReadBool(section, ConfigurationKeys.LlmGrammar) ?? LlmProfileShape.DefaultGrammar,
        };

        if (ReadInt(section, "MaxRetries", strict) is { } maxRetries)
            config = config with { MaxRetries = Math.Max(0, maxRetries) };

        // Absent = ResilienceDefaults.DefaultMaxRateLimitWait.
        return ReadTimeSpan(section, "MaxRateLimitWait", strict) is { } maxRateLimitWait
            ? config with { MaxRateLimitWait = maxRateLimitWait }
            : config;
    }

    private enum ApiKeySource
    {
        None,
        Configuration,
        ProcessEnvironment,
        UserEnvironment,
        Unresolved,
    }

    private readonly record struct ResolvedApiKey(string? Key, ApiKeySource Source);

    /// <summary>
    /// The section's key and where it came from: the <c>ApiKey</c> the configuration resolves,
    /// else the variable <c>ApiKeyEnvVar</c> names — process, then user scope —, else none.
    /// </summary>
    private static ResolvedApiKey ResolveApiKey(IConfigurationSection section, LlmKeyEnvironment environment)
    {
        var reference = ReadReference(section);
        var inline = section["ApiKey"];
        if (!string.IsNullOrWhiteSpace(inline))
        {
            SettingReference.Refuse($"{section.Path}:ApiKey", inline);
            return new ResolvedApiKey(inline, ApiKeySource.Configuration);
        }

        if (reference is null)
            return new ResolvedApiKey(null, ApiKeySource.None);
        if (Clean(environment.Process(reference)) is { } fromProcess)
            return new ResolvedApiKey(fromProcess, ApiKeySource.ProcessEnvironment);
        if (Clean(ReadUserScope(environment, reference)) is { } fromUser)
            return new ResolvedApiKey(fromUser, ApiKeySource.UserEnvironment);
        return new ResolvedApiKey(null, ApiKeySource.Unresolved);
    }

    /// <summary>
    /// <c>ApiKeyEnvVar</c>, or null when blank. Read as written — Windows compares variable names
    /// without case, Linux with — and refused when it cannot be a variable's name: an <c>=</c>, a
    /// space or a line break is a key or a sentence pasted in the wrong field, so the message
    /// names the path and never repeats the value.
    /// </summary>
    private static string? ReadReference(IConfigurationSection section)
    {
        var reference = section[ConfigurationKeys.LlmApiKeyEnvVar];
        if (string.IsNullOrWhiteSpace(reference))
            return null;

        SettingReference.Refuse($"{section.Path}:{ConfigurationKeys.LlmApiKeyEnvVar}", reference);

        if (reference.Any(c => c == '=' || char.IsWhiteSpace(c) || char.IsControl(c)))
        {
            throw new InvalidOperationException(
                $"{section.Path}:{ConfigurationKeys.LlmApiKeyEnvVar} is not the name of an environment variable " +
                "(it holds an '=', a space or a line break). It names the variable that holds the API key — " +
                "the key itself never goes in a settings file.");
        }

        return reference;
    }

    /// <summary>
    /// The user scope, read only. A scope that cannot be read — the registry refused, a virtual
    /// service account — is a variable not found: the call then fails as it does without a key,
    /// rather than the host refusing to start over a convenience.
    /// </summary>
    [SuppressMessage("Design", "CA1031",
        Justification = "Whatever reading the user scope throws, the variable is not found there — exactly " +
                        "like an absent one; the unresolved reference is then warned about by name of its path.")]
    private static string? ReadUserScope(LlmKeyEnvironment environment, string name)
    {
        try
        {
            return environment.User(name);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Uri? ReadBaseUrl(IConfigurationSection section)
    {
        var raw = section["BaseUrl"];
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        SettingReference.Refuse($"{section.Path}:BaseUrl", raw);

        // The Domain reading is the only test of the address.
        if (LlmEndpointReading.Of(raw, null, null).Notice == LlmEndpointNotice.Refused)
            throw new InvalidOperationException(OperatorMessages.LlmBaseUrlRefused($"{section.Path}:BaseUrl", raw));

        return new Uri(raw.Trim(), UriKind.Absolute);
    }

    /// <summary>
    /// A number of the section, written invariant; blank is unset. A number that parses but is not
    /// finite — <c>NaN</c>, <c>Infinity</c>, or <c>1e400</c>, which reads as infinity — is refused like
    /// one that does not parse (GAP-40): JSON writes no such number, and every request failed to
    /// serialise on it.
    /// </summary>
    private static double? ReadDouble(IConfigurationSection section, string key, bool strict)
    {
        var raw = section[key];
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            if (double.IsFinite(value))
                return value;
            return strict ? throw NotAFiniteNumber(section, key, raw) : null;
        }

        return strict ? throw NotANumber(section, key, raw) : null;
    }

    private static int? ReadInt(IConfigurationSection section, string key, bool strict)
    {
        var raw = section[key];
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            return value;
        return strict ? throw NotANumber(section, key, raw) : null;
    }

    private static TimeSpan? ReadTimeSpan(IConfigurationSection section, string key, bool strict)
    {
        var raw = section[key];
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        if (TimeSpan.TryParse(raw, CultureInfo.InvariantCulture, out var value) && value >= TimeSpan.Zero)
            return value;
        return strict ? throw NotANumber(section, key, raw) : null;
    }

    private static LlmCallMode ReadCallMode(IConfigurationSection section)
    {
        try
        {
            return LlmCallMode.Parse(section["CallMode"]);
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException(
                $"{section.Path}:CallMode is not a call mode: '{section["CallMode"]}'. Use Auto, Buffer or Stream.");
        }
    }

    private static InvalidOperationException NotANumber(IConfigurationSection section, string key, string raw) =>
        new($"{section.Path}:{key} is not a number: '{raw}'.");

    private static InvalidOperationException NotAFiniteNumber(IConfigurationSection section, string key, string raw) =>
        new($"{section.Path}:{key} is not a finite number: '{raw}'.");

    /// <summary>A switch of the section: <c>true</c> or <c>false</c>, any case; blank is unset.</summary>
    private static bool? ReadBool(IConfigurationSection section, string key)
    {
        var raw = section[key];
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        if (bool.TryParse(raw, out var value))
            return value;
        throw new InvalidOperationException($"{section.Path}:{key} is not true or false: '{raw}'.");
    }

    /// <summary>
    /// <c>Llm:Cache:{SystemPrompt,Tools,Ttl}</c>, mapped by <see cref="LlmCacheConfig.Request"/> as a
    /// crew file's <c>cache:</c> block and a script's <c>cache({ ... })</c> are (#254). A block that
    /// marks nothing is no block; a <c>Ttl</c> that is not <c>5m</c> or <c>1h</c>, or one set with
    /// nothing marked, refuses the start, naming its key.
    /// </summary>
    private static LlmCacheConfig? ReadCache(IConfigurationSection section)
    {
        var cache = section.GetSection("Cache");
        var systemPrompt = ReadBool(cache, "SystemPrompt") ?? false;
        var tools = ReadBool(cache, "Tools") ?? false;
        var ttl = string.IsNullOrWhiteSpace(cache["Ttl"]) ? null : cache["Ttl"]!.Trim();
        try
        {
            return LlmCacheConfig.Request(systemPrompt, tools, ttl);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException($"{cache.Path}:Ttl: {ex.Message}", ex);
        }
    }

    private static LlmThinkingConfig? ReadThinking(IConfigurationSection section)
    {
        // Llm:Thinking:{Enabled,Effort} — forwarded to thinking-capable providers
        // (DeepSeek, Z.AI GLM) as the `thinking` block + `reasoning_effort` field. Blank pins
        // nothing, and a block that pins nothing is no block.
        var thinking = section.GetSection(ConfigurationKeys.ThinkingSection);
        var enabled = ReadBool(thinking, "Enabled");
        var effort = string.IsNullOrWhiteSpace(thinking["Effort"]) ? null : thinking["Effort"];
        if (enabled is null && effort is null)
            return null;

        if (effort is not null)
        {
            try
            {
                LlmThinkingConfig.ValidateEffort(effort);
            }
            catch (ArgumentException ex)
            {
                throw new InvalidOperationException($"{thinking.Path}:Effort: {ex.Message}", ex);
            }
        }

        return new LlmThinkingConfig
        {
            Enabled = enabled,
            Effort = effort,
        };
    }
}
