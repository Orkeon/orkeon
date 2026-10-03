using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Orkeon.Constants.Configuration;
using Orkeon.Constants.Llm;

namespace Orkeon.Studio.Core.Configuration;

/// <summary>
/// Typed view over <c>Llm:Profiles</c> (GAP-17): the named providers a crew picks with
/// <c>llm: { profile: &lt;id&gt; }</c>, each of the <c>Llm</c> section's shape. Studio writes the
/// entries its model settings own (STUDIO-48) — field by field, so a key it does not model
/// (<c>MaxRetries</c>, <c>Grammar</c>) survives an edit — and never an <c>ApiKey</c>: the key
/// travels as <c>ORKEON_Llm__Profiles__&lt;id&gt;__ApiKey</c> at launch. Which entries Studio owns
/// is <c>Orkeon.Studio.Core.Profiles.HostLlmProfiles</c>' business; this view only reads and
/// writes.
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

        var path = $"{SectionPath}:{key}";
        return new LlmProfileEntry
        {
            Id = key,
            BaseUrl = _document.GetString($"{path}:BaseUrl"),
            Model = _document.GetString($"{path}:Model"),
            Temperature = _document.GetDouble($"{path}:Temperature"),
            TimeoutSeconds = _document.GetInt32($"{path}:TimeoutSeconds"),
            MaxTokens = _document.GetInt32($"{path}:MaxTokens"),
            ThinkingEnabled = _document.GetBoolean($"{path}:{ConfigurationKeys.ThinkingSection}:Enabled"),
            ThinkingEffort = _document.GetString($"{path}:{ConfigurationKeys.ThinkingSection}:Effort"),
        };
    }

    /// <summary>
    /// Writes the entry's fields in place under its id: a field left null removes its key, every
    /// key the entry does not model stays, and no <c>ApiKey</c> is ever written. A differently
    /// cased twin of the id is renamed onto it first.
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
        var thinking = $"{path}:{ConfigurationKeys.ThinkingSection}";
        _document.SetString($"{path}:BaseUrl", entry.BaseUrl);
        _document.SetString($"{path}:Model", entry.Model);
        _document.SetDouble($"{path}:Temperature", entry.Temperature);
        _document.SetInt32($"{path}:TimeoutSeconds", entry.TimeoutSeconds);
        _document.SetInt32($"{path}:MaxTokens", entry.MaxTokens);
        _document.SetBoolean($"{thinking}:Enabled", entry.ThinkingEnabled);
        _document.SetString($"{thinking}:Effort", entry.ThinkingEffort);
        if (_document.GetNode(thinking) is JsonObject { Count: 0 })
            _document.Remove(thinking);

        // An entry that pins nothing still exists: the runtime must see the profile, and builds
        // it on its provider's own defaults.
        if (_document.GetNode(path) is null)
            _document.SetNode(path, new JsonObject());

        return !string.Equals(before, _document.GetNode(SectionPath)?.ToJsonString(), StringComparison.Ordinal);
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
/// One <c>Llm:Profiles</c> entry, as far as Studio models it: the fields a model setting pins.
/// The key is not one of them — it never lives in the file.
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

    /// <summary><c>Temperature</c>, when pinned.</summary>
    public double? Temperature { get; init; }

    /// <summary><c>TimeoutSeconds</c>, when pinned.</summary>
    public int? TimeoutSeconds { get; init; }

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
