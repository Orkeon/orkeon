using System.Diagnostics.CodeAnalysis;
using Orkeon.Studio.Core.Presets;

namespace Orkeon.Studio.Core.Llm;

/// <summary>
/// Resolves the API key a probe of the <c>Llm</c> section should present — the one a run presents,
/// read in the run's order (STUDIO-54) and under every spelling the run reads (STUDIO-56). Studio
/// recommends keeping the key out of the file, so a "Test connection" that only ever read the file
/// would fail for exactly the users who followed that advice. First the key the configuration
/// resolves, its layers composed as every runner composes them (GAP-36): the <c>ORKEON_</c>
/// variables, which win, then the file's <c>ApiKey</c>, then the unprefixed variables beneath the
/// file. A layer that sets the key hides those under it, and a blank value reads as absent. Only then
/// the variable <c>Llm:ApiKeyEnvVar</c> names — that reference composed the same way —, by its exact
/// name, in the process environment, then in the user's scope (read, never copied; STUDIO-49).
/// </summary>
/// <remarks>
/// A layer is found as the .NET configuration finds it: a variable whose name starts with
/// <c>ORKEON_</c>, without case, and whose rest, <c>__</c> read as <c>:</c>, is <c>Llm:ApiKey</c>
/// without case — <c>ORKEON_LLM__APIKEY</c>, <c>orkeon_llm__apikey</c>, <c>ORKEON_Llm:ApiKey</c> —;
/// the unprefixed layer by the whole name, same rule. Under Linux and macOS two spellings are two
/// variables, and a run takes either one: when the layer that decides holds two different values,
/// the resolution is a conflict naming the variables, never a key.
/// </remarks>
public static class LlmApiKeyResolver
{
    /// <summary>The key, as the configuration names it.</summary>
    public const string ApiKeySetting = "Llm:ApiKey";

    /// <summary>The reference to the variable holding the key, as the configuration names it.</summary>
    public const string ApiKeyEnvVarSetting = "Llm:ApiKeyEnvVar";

    /// <summary>The prefix of the variables every runner reads over the file.</summary>
    private const string OrkeonPrefix = "ORKEON_";

    /// <summary>
    /// The key over the machine's own environment, both scopes, in the run's order: the
    /// configuration's key — the <c>ORKEON_</c> layer, else <paramref name="inlineApiKey"/>, else
    /// the unprefixed layer — then the variable the reference names.
    /// </summary>
    /// <param name="inlineApiKey">The key held in the settings file, if any.</param>
    /// <param name="apiKeyEnvVar">The file's <c>Llm:ApiKeyEnvVar</c>, if any.</param>
    /// <param name="environment">The process environment and the user scope.</param>
    public static LlmApiKeyResolution Resolve(string? inlineApiKey, string? apiKeyEnvVar, IEnvironmentVariables environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        var block = environment.ReadAll(EnvironmentVariableTarget.Process);

        var key = Composed(block, ApiKeySetting, inlineApiKey);
        if (key.Conflict is { } keyConflict)
            return keyConflict;
        if (Clean(key.Value) is { } configured)
            return LlmApiKeyResolution.Found(configured);

        var reference = Composed(block, ApiKeyEnvVarSetting, apiKeyEnvVar);
        if (reference.Conflict is { } referenceConflict)
            return referenceConflict;
        if (Clean(reference.Value) is not { } name)
            return LlmApiKeyResolution.None;

        // The variable the reference names is read by its exact name, as the run reads it.
        var named = Clean(environment.Read(name, EnvironmentVariableTarget.Process)) ?? Clean(ReadUserScope(environment, name));
        return named is null ? LlmApiKeyResolution.None : LlmApiKeyResolution.Found(named);
    }

    /// <summary>
    /// A key of the <c>Llm</c> section as the configuration composes it: the <c>ORKEON_</c> layer
    /// when it is set — blank or not, it hides the layers under it —, else the file's value, else the
    /// unprefixed layer. A blank field of the file is no value: the form removes it when it saves.
    /// </summary>
    private static Layer Composed(IReadOnlyList<KeyValuePair<string, string>> block, string setting, string? file)
    {
        var orkeon = LayerOf(block, OrkeonPrefix, setting);
        if (orkeon.IsSet)
            return orkeon;

        return string.IsNullOrWhiteSpace(file)
            ? LayerOf(block, "", setting)
            : new Layer(true, file, null);
    }

    /// <summary>
    /// The variables of <paramref name="block"/> that give <paramref name="setting"/> through
    /// <paramref name="prefix"/>: one value, or a conflict when they hold different ones.
    /// </summary>
    private static Layer LayerOf(IReadOnlyList<KeyValuePair<string, string>> block, string prefix, string setting)
    {
        var matches = block.Where(variable => Gives(variable.Key, prefix, setting)).ToList();
        if (matches.Count == 0)
            return new Layer(false, null, null);

        if (matches.Select(variable => variable.Value).Distinct(StringComparer.Ordinal).Count() == 1)
            return new Layer(true, matches[0].Value, null);

        var names = matches.Select(variable => variable.Key).Order(StringComparer.Ordinal).ToList();
        return new Layer(true, null, LlmApiKeyResolution.Conflicting(setting, names));
    }

    /// <summary>
    /// True when the configuration reads <paramref name="name"/> as <paramref name="setting"/> under
    /// <paramref name="prefix"/>: the prefix and the key compared without case, <c>__</c> read as
    /// <c>:</c> (<c>EnvironmentVariablesConfigurationProvider</c>).
    /// </summary>
    private static bool Gives(string name, string prefix, string setting) =>
        name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
        && string.Equals(
            name[prefix.Length..].Replace("__", ":", StringComparison.Ordinal),
            setting,
            StringComparison.OrdinalIgnoreCase);

    [SuppressMessage("Design", "CA1031",
        Justification = "A user scope that cannot be read (registry access denied) is a key not found, exactly " +
                        "like an absent one — as the runtime reads it.")]
    private static string? ReadUserScope(IEnvironmentVariables environment, string name)
    {
        try
        {
            return environment.Read(name, EnvironmentVariableTarget.User);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>One layer: whether it sets the key, its value, or its conflict.</summary>
    private readonly record struct Layer(bool IsSet, string? Value, LlmApiKeyResolution? Conflict);
}

/// <summary>
/// What <see cref="LlmApiKeyResolver"/> found: a key, none, or a conflict — two variables that set the
/// same key with different values, in the layer that decides (STUDIO-56). A conflict names the setting
/// and the variables, never their values.
/// </summary>
public sealed record LlmApiKeyResolution
{
    private LlmApiKeyResolution(string? key, string? setting, IReadOnlyList<string> variables)
    {
        Key = key;
        Setting = setting;
        ConflictingVariables = variables;
    }

    /// <summary>No key anywhere.</summary>
    public static LlmApiKeyResolution None { get; } = new(null, null, []);

    /// <summary>The key a run presents; null for none and for a conflict.</summary>
    public string? Key { get; }

    /// <summary>The setting two variables disagree on (<c>Llm:ApiKey</c> or <c>Llm:ApiKeyEnvVar</c>); null otherwise.</summary>
    public string? Setting { get; }

    /// <summary>The variables that disagree, ordered by name; empty unless <see cref="IsConflict"/>.</summary>
    public IReadOnlyList<string> ConflictingVariables { get; }

    /// <summary>True when two spellings of the setting hold different values: a run reads either one.</summary>
    public bool IsConflict => ConflictingVariables.Count > 0;

    /// <summary>A key found.</summary>
    public static LlmApiKeyResolution Found(string key) => new(key, null, []);

    /// <summary>A conflict on <paramref name="setting"/> between <paramref name="variables"/>.</summary>
    public static LlmApiKeyResolution Conflicting(string setting, IReadOnlyList<string> variables) =>
        new(null, setting, variables);
}
