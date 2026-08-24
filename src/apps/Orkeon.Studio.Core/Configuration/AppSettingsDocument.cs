using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Orkeon.Studio.Core.Configuration;

/// <summary>
/// An <c>appsettings.json</c> document edited as a JSON tree rather than a POCO, so
/// that every key Studio does not know about survives a load/save round-trip in place
/// and in order. Typed views over the sections the UIs edit hang off this document
/// (<see cref="Llm"/>, <see cref="RateLimiting"/>, <see cref="Rag"/>,
/// <see cref="Mounts"/>, <see cref="Logging"/>, <see cref="LlmLogging"/>); everything
/// else stays reachable through <see cref="Root"/> and the path accessors.
/// </summary>
/// <remarks>
/// Comments and trailing commas are accepted on load (the .NET configuration JSON
/// provider accepts them too) but are <b>not</b> preserved on save — JSON itself has
/// no comment syntax, which is exactly why <c>orkeon init</c> carries its note in a
/// <c>_comment</c> key.
/// </remarks>
public sealed class AppSettingsDocument
{
    /// <summary>Conventional file name of a settings document.</summary>
    public const string FileName = "appsettings.json";

    private static readonly JsonDocumentOptions ParseOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private AppSettingsDocument(JsonObject root)
    {
        Root = root;
        Llm = new LlmSection(this);
        RateLimiting = new RateLimitingSection(this);
        Rag = new RagSection(this);
        Mounts = new MountsSection(this);
        Logging = new LoggingSection(this);
        LlmLogging = new LlmLoggingSection(this);
    }

    /// <summary>The mutable JSON tree backing this document — the raw-edit surface of the UIs.</summary>
    public JsonObject Root { get; }

    /// <summary>Typed view over the <c>Llm</c> section.</summary>
    public LlmSection Llm { get; }

    /// <summary>Typed view over the <c>RateLimiting</c> section.</summary>
    public RateLimitingSection RateLimiting { get; }

    /// <summary>Typed view over the <c>Orkeon:Rag</c> section.</summary>
    public RagSection Rag { get; }

    /// <summary>Typed view over the <c>Orkeon:FileSystem:Mounts</c> array.</summary>
    public MountsSection Mounts { get; }

    /// <summary>Typed view over the <c>Logging:LogLevel</c> section.</summary>
    public LoggingSection Logging { get; }

    /// <summary>Typed view over the <c>LlmLogging</c> section.</summary>
    public LlmLoggingSection LlmLogging { get; }

    /// <summary>Creates an empty document (<c>{}</c>).</summary>
    public static AppSettingsDocument CreateEmpty() => new(new JsonObject());

    /// <summary>Parses JSON text into an editable document.</summary>
    /// <exception cref="JsonException">The text is not a well-formed JSON object.</exception>
    public static AppSettingsDocument Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        var node = JsonNode.Parse(json, nodeOptions: null, ParseOptions);
        if (node is not JsonObject root)
            throw new JsonException("An appsettings document must be a JSON object.");

        return new AppSettingsDocument(root);
    }

    /// <summary>
    /// Parses JSON text, reporting a malformed document as a message instead of an
    /// exception — the form validation path of the UIs.
    /// </summary>
    public static bool TryParse(
        string? json,
        [NotNullWhen(true)] out AppSettingsDocument? document,
        out string? error)
    {
        document = null;
        error = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            error = "The document is empty.";
            return false;
        }

        try
        {
            document = Parse(json);
            return true;
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Serializes the document, indented and newline-terminated — the exact shape
    /// <c>orkeon init</c> writes, so a Studio-written file and a CLI-written file are
    /// textually comparable.
    /// </summary>
    public string ToJson() =>
        JsonSerializer.Serialize(Root, WriteOptions) + Environment.NewLine;

    /// <summary>Deep-copies the document, including the keys Studio does not model.</summary>
    public AppSettingsDocument Clone() => Parse(Root.ToJsonString());

    // ── path accessors (":" separates nesting levels, as in IConfiguration) ──

    /// <summary>True when the colon-separated path resolves to a node (of any kind).</summary>
    public bool ContainsPath(string path) => GetNode(path) is not null;

    /// <summary>Returns the node at the colon-separated path, or <see langword="null"/>.</summary>
    public JsonNode? GetNode(string path)
    {
        var segments = SplitPath(path);
        JsonNode? current = Root;

        foreach (var segment in segments)
        {
            if (current is not JsonObject obj || !obj.TryGetPropertyValue(segment, out var next))
                return null;
            current = next;
        }

        return current;
    }

    /// <summary>
    /// Writes a node at the colon-separated path, creating the intermediate objects.
    /// A <see langword="null"/> value removes the key instead of writing a JSON null,
    /// so that clearing a field never leaves a key the runtime would then bind.
    /// </summary>
    public void SetNode(string path, JsonNode? value)
    {
        var segments = SplitPath(path);
        var parent = Root;

        for (var i = 0; i < segments.Length - 1; i++)
        {
            var segment = segments[i];
            if (!parent.TryGetPropertyValue(segment, out var child) || child is not JsonObject childObject)
            {
                if (value is null)
                    return; // nothing to remove — never materialize (or overwrite a scalar on) the parent chain

                childObject = new JsonObject();
                parent[segment] = childObject;
            }

            parent = childObject;
        }

        var leaf = segments[^1];
        if (value is null)
            parent.Remove(leaf);
        else
            parent[leaf] = value;
    }

    /// <summary>Removes the key at the colon-separated path, if present.</summary>
    public void Remove(string path) => SetNode(path, null);

    /// <summary>
    /// Reads a value as text. Numbers and booleans are rendered in their invariant
    /// form (the configuration binder accepts both spellings); objects and arrays
    /// return <see langword="null"/>.
    /// </summary>
    public string? GetString(string path)
    {
        return GetNode(path) switch
        {
            JsonValue value when value.TryGetValue<string>(out var text) => text,
            JsonValue value => value.ToJsonString().Trim('"'),
            _ => null,
        };
    }

    /// <summary>Writes a string value; <see langword="null"/> or blank removes the key.</summary>
    public void SetString(string path, string? value) =>
        SetNode(path, string.IsNullOrWhiteSpace(value) ? null : JsonValue.Create(value));

    /// <summary>Reads an integer, accepting the string spelling the configuration binder accepts.</summary>
    public int? GetInt32(string path)
    {
        return GetNode(path) switch
        {
            JsonValue value when value.TryGetValue<int>(out var number) => number,
            JsonValue value when value.TryGetValue<string>(out var text)
                && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        };
    }

    /// <summary>Writes an integer value; <see langword="null"/> removes the key.</summary>
    public void SetInt32(string path, int? value) =>
        SetNode(path, value is null ? null : JsonValue.Create(value.Value));

    /// <summary>Reads a floating-point value, accepting the string spelling.</summary>
    public double? GetDouble(string path)
    {
        return GetNode(path) switch
        {
            JsonValue value when value.TryGetValue<double>(out var number) => number,
            JsonValue value when value.TryGetValue<string>(out var text)
                && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        };
    }

    /// <summary>Writes a floating-point value; <see langword="null"/> removes the key.</summary>
    public void SetDouble(string path, double? value) =>
        SetNode(path, value is null ? null : JsonValue.Create(value.Value));

    /// <summary>Reads a boolean, accepting the string spelling.</summary>
    public bool? GetBoolean(string path)
    {
        return GetNode(path) switch
        {
            JsonValue value when value.TryGetValue<bool>(out var flag) => flag,
            JsonValue value when value.TryGetValue<string>(out var text)
                && bool.TryParse(text, out var parsed) => parsed,
            _ => null,
        };
    }

    /// <summary>Writes a boolean value; <see langword="null"/> removes the key.</summary>
    public void SetBoolean(string path, bool? value) =>
        SetNode(path, value is null ? null : JsonValue.Create(value.Value));

    /// <summary>
    /// Reads an array of strings (the shape of <c>Orkeon:FileSystem:Mounts</c>).
    /// Non-string entries are rendered with <see cref="GetString"/> semantics;
    /// a missing or non-array node yields an empty list.
    /// </summary>
    public IReadOnlyList<string> GetStringArray(string path)
    {
        if (GetNode(path) is not JsonArray array)
            return [];

        var items = new List<string>(array.Count);
        foreach (var item in array)
        {
            if (item is JsonValue value)
            {
                items.Add(value.TryGetValue<string>(out var text) ? text : value.ToJsonString().Trim('"'));
            }
        }

        return items;
    }

    /// <summary>Writes an array of strings; an empty sequence writes an empty array (not a removal).</summary>
    public void SetStringArray(string path, IEnumerable<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var array = new JsonArray();
        foreach (var value in values)
            array.Add(JsonValue.Create(value));

        SetNode(path, array);
    }

    /// <summary>
    /// True when the path resolves to a JSON object with at least one key — the same
    /// notion of "the section exists" the .NET configuration binder uses, and the one
    /// <c>RunnerHost</c> tests before falling back to the echo provider.
    /// </summary>
    public bool SectionExists(string path) => GetNode(path) is JsonObject { Count: > 0 };

    private static string[] SplitPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return path.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
