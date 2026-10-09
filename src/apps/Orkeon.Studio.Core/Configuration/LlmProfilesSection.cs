using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Orkeon.Constants.Configuration;
using Orkeon.Constants.Llm;

namespace Orkeon.Studio.Core.Configuration;

/// <summary>
/// Typed view over <c>Llm:Profiles</c> (GAP-17): the named providers a crew picks with
/// <c>llm: { profile: &lt;id&gt; }</c>, each of the <c>Llm</c> section's shape. Studio writes the
/// entries its model settings own (STUDIO-48) — field by field, so a key it does not model
/// (<c>MaxRetries</c>, <c>Grammar</c>) survives an edit — and never a key: an entry names the
/// variable holding its key (<c>ApiKeyEnvVar</c>, STUDIO-49), which a run outside Studio reads, and a
/// Studio launch lays the key itself as <c>ORKEON_Llm__Profiles__&lt;id&gt;__ApiKey</c>. The one
/// <c>ApiKey</c> Studio writes is no key: Docker Model Runner's placeholder <c>not-needed</c>, where
/// none is set (<see cref="KeyAgrees(AppSettingsDocument, string, LlmProfileEntry)"/>, STUDIO-54).
/// Which entries Studio owns is <c>Orkeon.Studio.Core.Profiles.HostLlmProfiles</c>' business; this
/// view only reads and writes.
/// <para>
/// The configuration binder compares keys case-insensitively, so <see cref="Find"/> does too: two
/// JSON properties spelt <c>claude</c> and <c>Claude</c> are one profile to the runtime, and a
/// write under one replaces the other rather than leaving a twin the binder would merge.
/// </para>
/// </summary>
public sealed class LlmProfilesSection
{
    /// <summary>Configuration path of the section.</summary>
    public const string SectionPath = LlmSection.SectionPath + ":" + ConfigurationKeys.LlmProfiles;

    /// <summary>
    /// The reserved name: the default profile is the <c>Llm</c> section itself, and a host that
    /// defines <c>Llm:Profiles:default</c> refuses to start.
    /// </summary>
    public const string DefaultProfile = LlmProfileNames.Default;

    private readonly AppSettingsDocument _document;

    internal LlmProfilesSection(AppSettingsDocument document) => _document = document;

    /// <summary>True when the section holds at least one profile.</summary>
    public bool Exists => _document.SectionExists(SectionPath);

    /// <summary>The profile ids as the document spells them, in document order.</summary>
    public IReadOnlyList<string> Ids => _document.ObjectKeys(SectionPath);

    /// <summary>Whether <paramref name="id"/> designates the default profile: blank, or <see cref="DefaultProfile"/>.</summary>
    public static bool IsDefault(string? id) =>
        string.IsNullOrWhiteSpace(id) || string.Equals(id.Trim(), DefaultProfile, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The id as the document spells it, matched case-insensitively — the binder's comparison —,
    /// or null when no entry answers to it.
    /// </summary>
    public string? Find(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return Ids.FirstOrDefault(key => string.Equals(key, id.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The entry answering to <paramref name="id"/>, or null when there is none.</summary>
    public LlmProfileEntry? Get(string id)
    {
        if (Find(id) is not { } key)
            return null;

        return ReadEntry(_document, $"{SectionPath}:{key}", key);
    }

    /// <summary>
    /// Writes the entry's fields in place under its id: a field left null removes its key, every
    /// key the entry does not model stays, and no key is ever written — the entry names the variable
    /// holding it (<c>ApiKeyEnvVar</c>); its <c>ApiKey</c> follows the placeholder rule
    /// (<see cref="KeyAgrees(AppSettingsDocument, string, LlmProfileEntry)"/>). A differently cased
    /// twin of the id is renamed onto it first.
    /// </summary>
    /// <returns>True when the document changed.</returns>
    public bool Set(LlmProfileEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (IsDefault(entry.Id))
            throw new ArgumentException(
                $"'{DefaultProfile}' is the reserved name of the default profile, the {LlmSection.SectionPath} section itself.",
                nameof(entry));

        var before = _document.GetNode(SectionPath)?.ToJsonString();
        var id = entry.Id.Trim();
        if (Find(id) is { } existing && !string.Equals(existing, id, StringComparison.Ordinal))
            Rename(existing, id);

        var path = $"{SectionPath}:{id}";
        WriteEntry(_document, path, entry);

        // An entry that pins nothing still exists: the runtime must see the profile, and builds
        // it on its provider's own defaults.
        if (_document.GetNode(path) is null)
            _document.SetNode(path, new JsonObject());

        return !string.Equals(before, _document.GetNode(SectionPath)?.ToJsonString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the <c>ApiKey</c> of the entry answering to <paramref name="entry"/>'s id agrees with
    /// it (<see cref="KeyAgrees(AppSettingsDocument, string, LlmProfileEntry)"/>); false when no entry
    /// answers to it.
    /// </summary>
    /// <param name="entry">What the setting the entry mirrors writes (<c>ModelProfile.ToHostEntry</c>).</param>
    public bool KeyAgrees(LlmProfileEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return Find(entry.Id) is { } key && KeyAgrees(_document, $"{SectionPath}:{key}", entry);
    }

    /// <summary>
    /// The one rule that judges the <c>ApiKey</c> of a section of the <c>Llm</c> shape at
    /// <paramref name="path"/> — an entry of <c>Llm:Profiles</c>, or <c>Llm</c> itself — against the
    /// setting it mirrors (STUDIO-54). A Docker Model Runner setting's
    /// (<see cref="LlmProfileEntry.ApiKeyPlaceholder"/>) agrees when the section holds a key — its
    /// placeholder or any other: that server checks none —; any other setting's agrees when the
    /// section does not hold that placeholder, which would pass before the variable the setting
    /// names. A key written by hand is never Studio's to judge further. Writing an entry makes the
    /// rule true, so a repair is never repeated. <see cref="ReadEntry"/> never reads the key: a
    /// comparison of entries compares the other fields, and leaves the key to this rule.
    /// </summary>
    /// <param name="document">The settings file.</param>
    /// <param name="path">The section's configuration path, as the document spells it.</param>
    /// <param name="entry">What the setting writes.</param>
    public static bool KeyAgrees(AppSettingsDocument document, string path, LlmProfileEntry entry)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(entry);

        var apiKey = document.GetString($"{path}:ApiKey");
        return entry.ApiKeyPlaceholder is not null
            ? !string.IsNullOrWhiteSpace(apiKey)
            : !string.Equals(apiKey, LlmProviderDefaultModels.DockerModelRunnerApiKeyPlaceholder, StringComparison.Ordinal);
    }

    /// <summary>The fields Studio models of the section of the <c>Llm</c> shape at <paramref name="path"/>.</summary>
    internal static LlmProfileEntry ReadEntry(AppSettingsDocument document, string path, string id) => new()
    {
        Id = id,
        BaseUrl = document.GetString($"{path}:BaseUrl"),
        Model = document.GetString($"{path}:Model"),
        ApiKeyEnvVar = document.GetString($"{path}:{ConfigurationKeys.LlmApiKeyEnvVar}"),
        Temperature = document.GetDouble($"{path}:Temperature"),
        TimeoutSeconds = document.GetInt32($"{path}:TimeoutSeconds"),
        StreamIdleSeconds = document.GetInt32($"{path}:StreamIdleSeconds"),
        MaxTokens = document.GetInt32($"{path}:MaxTokens"),
        ThinkingEnabled = document.GetBoolean($"{path}:{ConfigurationKeys.ThinkingSection}:Enabled"),
        ThinkingEffort = document.GetString($"{path}:{ConfigurationKeys.ThinkingSection}:Effort"),
    };

    /// <summary>
    /// Writes <paramref name="entry"/>'s fields into the section of the <c>Llm</c> shape at
    /// <paramref name="path"/>, one by one — a null field removes its key, a key the entry does
    /// not model stays, a key is never written. The section's <c>ApiKey</c> is made to agree
    /// (<see cref="KeyAgrees(AppSettingsDocument, string, LlmProfileEntry)"/>, STUDIO-54): a Docker
    /// Model Runner setting writes its placeholder where no key is set, any other setting takes that
    /// placeholder out, and every other <c>ApiKey</c> stays (STUDIO-49). Shared by a profile's entry
    /// and the default section (the election), which have the same shape.
    /// </summary>
    internal static void WriteEntry(AppSettingsDocument document, string path, LlmProfileEntry entry)
    {
        var thinking = $"{path}:{ConfigurationKeys.ThinkingSection}";
        document.SetString($"{path}:BaseUrl", entry.BaseUrl);
        document.SetString($"{path}:Model", entry.Model);
        document.SetString($"{path}:{ConfigurationKeys.LlmApiKeyEnvVar}", entry.ApiKeyEnvVar);
        document.SetDouble($"{path}:Temperature", entry.Temperature);
        document.SetInt32($"{path}:TimeoutSeconds", entry.TimeoutSeconds);
        document.SetInt32($"{path}:StreamIdleSeconds", entry.StreamIdleSeconds);
        document.SetInt32($"{path}:MaxTokens", entry.MaxTokens);
        document.SetBoolean($"{thinking}:Enabled", entry.ThinkingEnabled);
        document.SetString($"{thinking}:Effort", entry.ThinkingEffort);
        if (document.GetNode(thinking) is JsonObject { Count: 0 })
            document.Remove(thinking);

        // The placeholder where the server checks no key and the dialect wants one; out from under
        // any other card, where it would pass before the variable the setting names.
        if (!KeyAgrees(document, path, entry))
            document.SetString($"{path}:ApiKey", entry.ApiKeyPlaceholder);
    }

    /// <summary>
    /// Removes the entry and everything under it. The section goes when it empties, and so does
    /// an <c>Llm</c> section left with nothing in it — absent and empty mean the same to the
    /// runtime (the echo provider), and an empty object is clutter the user never wrote.
    /// </summary>
    /// <returns>True when an entry was removed.</returns>
    public bool Remove(string id)
    {
        if (Find(id) is not { } key)
            return false;

        _document.Remove($"{SectionPath}:{key}");
        if (Ids.Count == 0)
            _document.Remove(SectionPath);
        if (_document.GetNode(LlmSection.SectionPath) is JsonObject { Count: 0 })
            _document.Remove(LlmSection.SectionPath);
        return true;
    }

    /// <summary>
    /// Moves an entry under a new id, every key it carries included, so a rename loses nothing
    /// Studio does not model. A missing source, or a target already taken by another entry,
    /// leaves the document as it was.
    /// </summary>
    /// <returns>True when the entry moved.</returns>
    public bool Rename(string oldId, string newId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newId);
        if (Find(oldId) is not { } source)
            return false;

        var target = newId.Trim();
        if (string.Equals(source, target, StringComparison.Ordinal))
            return false;
        if (Find(target) is { } taken && !string.Equals(taken, source, StringComparison.Ordinal))
            return false;

        var copy = _document.GetNode($"{SectionPath}:{source}")!.DeepClone();
        _document.Remove($"{SectionPath}:{source}");
        _document.SetNode($"{SectionPath}:{target}", copy);
        return true;
    }
}

/// <summary>
/// One <c>Llm:Profiles</c> entry, as far as Studio models it: the fields a model setting pins,
/// and the name of the variable holding its key. The key is not one of them — it never lives in
/// the file; a Docker Model Runner setting carries the placeholder its card writes in its place
/// (<see cref="ApiKeyPlaceholder"/>).
/// </summary>
public sealed record LlmProfileEntry
{
    /// <summary>The name a crew writes: <c>llm: { profile: &lt;Id&gt; }</c>.</summary>
    public required string Id { get; init; }

    /// <summary><c>BaseUrl</c>, as typed.</summary>
    [SuppressMessage("Design", "CA1056",
        Justification = "This is the JSON field as typed, not a resolved endpoint: a hand-written entry " +
                        "may hold a malformed URL, which must be shown as written rather than dropped.")]
    public string? BaseUrl { get; init; }

    /// <summary><c>Model</c>; null leaves the provider's own default.</summary>
    public string? Model { get; init; }

    /// <summary>
    /// <c>ApiKeyEnvVar</c> (STUDIO-49): the environment variable holding the key — its name,
    /// never the key — which a run outside Studio reads; null for a setting that needs no key.
    /// </summary>
    public string? ApiKeyEnvVar { get; init; }

    /// <summary>
    /// The placeholder key the setting's card writes where the section holds no <c>ApiKey</c> —
    /// <c>not-needed</c> for Docker Model Runner, whose server checks no key while the run's OpenAI
    /// dialect wants one; null for every other card (STUDIO-54). Derived from the card, never read
    /// from the file: <see cref="LlmProfilesSection.KeyAgrees(AppSettingsDocument, string, LlmProfileEntry)"/>
    /// judges the key a section holds.
    /// </summary>
    public string? ApiKeyPlaceholder { get; init; }

    /// <summary><c>Temperature</c>, when pinned.</summary>
    public double? Temperature { get; init; }

    /// <summary><c>TimeoutSeconds</c>, when pinned.</summary>
    public int? TimeoutSeconds { get; init; }

    /// <summary><c>StreamIdleSeconds</c>, when pinned (LLM-12).</summary>
    public int? StreamIdleSeconds { get; init; }

    /// <summary><c>MaxTokens</c>, when pinned.</summary>
    public int? MaxTokens { get; init; }

    /// <summary><c>Thinking:Enabled</c>, when pinned.</summary>
    public bool? ThinkingEnabled { get; init; }

    /// <summary><c>Thinking:Effort</c>, when pinned.</summary>
    public string? ThinkingEffort { get; init; }

    /// <summary>
    /// One line for a read-only card: the model and the endpoint the entry names, what it leaves
    /// out simply absent.
    /// </summary>
    public string Summary =>
        string.Join(" · ", new[] { Model, BaseUrl }.Where(part => !string.IsNullOrWhiteSpace(part)));
}
