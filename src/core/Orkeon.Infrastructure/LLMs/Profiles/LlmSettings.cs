using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Constants.Configuration;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Infrastructure.LLMs.Profiles;

/// <summary>
/// Reads the host's LLM settings: the <c>Llm</c> section — the default profile — and its named
/// profiles under <c>Llm:Profiles:&lt;name&gt;</c>, each of the same shape (GAP-17). One reader
/// for both, so a profile accepts exactly the keys the default section does: <c>BaseUrl</c>,
/// <c>ApiKey</c>, <c>ApiKeyEnvVar</c>, <c>Model</c>, <c>Temperature</c>, <c>MaxTokens</c>,
/// <c>TimeoutSeconds</c>, <c>MaxRetries</c>, <c>Thinking:{Enabled,Effort}</c> and <c>Grammar</c>.
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
public static partial class LlmSettings
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
    /// <exception cref="InvalidOperationException">A value cannot be read: an invalid <c>BaseUrl</c>,
    /// an <c>ApiKeyEnvVar</c> that is no variable name, an <c>ApiKey</c> written as a <c>${NAME}</c> placeholder.</exception>
    public static LlmConfig ReadDefault(IConfiguration configuration) =>
        ReadDefault(configuration, LlmKeyEnvironment.Machine);

    /// <summary><see cref="ReadDefault(IConfiguration)"/> over an explicit environment.</summary>
    internal static LlmConfig ReadDefault(IConfiguration configuration, LlmKeyEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);
        return Read(configuration.GetSection(ConfigurationKeys.LlmSection), strict: false, environment);
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
    /// Reads one section of the <c>Llm</c> shape. The default section keeps its historical
    /// leniency — an unreadable number falls back to its default; a profile is
    /// <paramref name="strict"/>: it is new configuration, and a silent default would run a crew
    /// on settings its author never wrote. The key settings are new: strict in both.
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
            Thinking = ReadThinking(section),
            // Llm:Grammar — the endpoint honours a GBNF grammar (llama.cpp-compatible server).
            GrammarEnabled = bool.TryParse(section[ConfigurationKeys.LlmGrammar], out var grammar) && grammar,
        };

        return ReadInt(section, "MaxRetries", strict) is { } maxRetries
            ? config with { MaxRetries = Math.Max(0, maxRetries) }
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
            RefusePlaceholder(section, inline);
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
    /// An <c>ApiKey</c> written <c>${NAME}</c> — the shape the old settings templates carried —
    /// was never expanded: copied as is, the text went out as the key and came back a 401 at the
    /// first call. It refuses the start instead, with the setting that does what it meant.
    /// </summary>
    private static void RefusePlaceholder(IConfigurationSection section, string inline)
    {
        var placeholder = PlaceholderPattern().Match(inline.Trim());
        if (!placeholder.Success)
            return;

        var name = placeholder.Groups["name"].Value.Trim();
        var suggestion = name.Length > 0 && !name.Any(c => c == '=' || char.IsWhiteSpace(c) || char.IsControl(c))
            ? name
            : "<the variable's name>";
        throw new InvalidOperationException(
            $"{section.Path}:ApiKey is a ${{…}} placeholder, which Orkeon never expands — the text itself would be " +
            $"sent as the key. Name the variable that holds the key instead, in place of ApiKey: " +
            $"\"{ConfigurationKeys.LlmApiKeyEnvVar}\": \"{suggestion}\".");
    }

    [GeneratedRegex(@"^\$\{(?<name>[^{}]*)\}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex PlaceholderPattern();

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

        return Uri.TryCreate(raw, UriKind.Absolute, out var url)
            ? url
            : throw new InvalidOperationException(
                $"{section.Path}:BaseUrl is not an absolute URL: '{raw}'.");
    }

    private static double? ReadDouble(IConfigurationSection section, string key, bool strict)
    {
        var raw = section[key];
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return value;
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

    private static InvalidOperationException NotANumber(IConfigurationSection section, string key, string raw) =>
        new($"{section.Path}:{key} is not a number: '{raw}'.");

    private static LlmThinkingConfig? ReadThinking(IConfigurationSection section)
    {
        // Llm:Thinking:{Enabled,Effort} — forwarded to thinking-capable providers
        // (DeepSeek, Z.AI GLM) as the `thinking` block + `reasoning_effort` field. Blank pins
        // nothing, and a block that pins nothing is no block.
        var thinking = section.GetSection(ConfigurationKeys.ThinkingSection);
        bool? enabled = bool.TryParse(thinking["Enabled"], out var on) ? on : null;
        var effort = string.IsNullOrWhiteSpace(thinking["Effort"]) ? null : thinking["Effort"];
        if (enabled is null && effort is null)
            return null;

        return new LlmThinkingConfig
        {
            Enabled = enabled,
            Effort = effort,
        };
    }
}
