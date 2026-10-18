using System.Text.Json.Nodes;

namespace Orkeon.Studio.Core.Configuration;

/// <summary>
/// The in-place reads and writes the account sections share (<see cref="EmailSection"/>,
/// <see cref="DriveSection"/>): every key looked up without regard to case, as the configuration
/// reads it, the spelling found in the file kept, and nothing written as an empty string, an empty
/// object or an empty list — the engine reads each of them as a value. A value that reads the same
/// as the one in the file leaves the node alone.
/// </summary>
internal static class SectionNodes
{
    /// <summary>
    /// The key of <paramref name="owner"/> spelt exactly as <paramref name="name"/>, else the first
    /// that differs only by case — the one the configuration reads under that name.
    /// </summary>
    public static string? FindKey(JsonObject owner, string name) =>
        owner.ContainsKey(name)
            ? name
            : owner.Select(property => property.Key).FirstOrDefault(key => string.Equals(key, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>What <paramref name="owner"/> holds under <paramref name="key"/>, whatever the case the file spells it in.</summary>
    public static JsonNode? Node(JsonObject? owner, string key) =>
        owner is not null && FindKey(owner, key) is { } found ? owner[found] : null;

    /// <summary>Removes the key the file holds for <paramref name="key"/>, whatever its case.</summary>
    public static void Remove(JsonObject? owner, string key)
    {
        if (owner is not null && FindKey(owner, key) is { } found)
            owner.Remove(found);
    }

    /// <summary>Writes under the key the file holds for <paramref name="key"/>, or creates it as the caller spells it.</summary>
    public static void Write(JsonObject owner, string key, JsonNode value) =>
        owner[FindKey(owner, key) ?? key] = value;

    // Reads: the document's own readers over a node in hand — an account name may hold a colon or a
    // blank, which a colon-separated path could not spell.

    public static string? ReadText(JsonObject? owner, string key) => AppSettingsDocument.ReadString(Node(owner, key));

    public static int? ReadInt(JsonObject? owner, string key) => AppSettingsDocument.ReadInt32(Node(owner, key));

    public static bool? ReadBoolean(JsonObject? owner, string key) => AppSettingsDocument.ReadBoolean(Node(owner, key));

    public static IEnumerable<string> ReadStrings(JsonNode? node) =>
        node is JsonArray array
            ? array.OfType<JsonValue>().Select(value => AppSettingsDocument.ReadString(value)).OfType<string>()
            : [];

    // Writes: a value that reads the same as the one in the file leaves the node alone.

    public static void WriteText(JsonObject owner, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            Remove(owner, key);
        else if (!string.Equals(ReadText(owner, key), value, StringComparison.Ordinal))
            Write(owner, key, JsonValue.Create(value));
    }

    public static void WriteInt(JsonObject owner, string key, int? value)
    {
        if (value is null)
            Clear(owner, key, readable: ReadInt(owner, key) is not null);
        else if (ReadInt(owner, key) != value)
            Write(owner, key, JsonValue.Create(value.Value));
    }

    public static void WriteBoolean(JsonObject owner, string key, bool? value)
    {
        if (value is null)
            Clear(owner, key, readable: ReadBoolean(owner, key) is not null);
        else if (ReadBoolean(owner, key) != value)
            Write(owner, key, JsonValue.Create(value.Value));
    }

    /// <summary>
    /// Writes the list, unless it is the one the file reads as: what is no text in the array
    /// (<c>["a@x.org", 42]</c>), and what is no array at all, never came into the list that was
    /// read, so the same list written back leaves the node as the file spelt it. Blank entries are
    /// not written, and an empty list removes the key.
    /// </summary>
    public static void WriteStrings(JsonObject owner, string key, IEnumerable<string> values)
    {
        var kept = values.Where(value => !string.IsNullOrWhiteSpace(value)).ToList();
        if (kept.SequenceEqual(ReadStrings(Node(owner, key)), StringComparer.Ordinal))
            return;

        if (kept.Count == 0)
        {
            Remove(owner, key);
            return;
        }

        var array = new JsonArray();
        foreach (var value in kept)
            array.Add(JsonValue.Create(value));

        Write(owner, key, array);
    }

    /// <summary>
    /// Fills the object under <paramref name="key"/>, creating it when needed and removing it when
    /// it ends up empty. What is no object under the key (<c>"Auth": "oauth"</c>) read as no field:
    /// it stays as written while nothing is set under it, and gives way to the object as soon as
    /// something is.
    /// </summary>
    public static void WriteObject(JsonObject owner, string key, Action<JsonObject> fill)
    {
        var existing = Node(owner, key) as JsonObject;
        var child = existing ?? [];

        fill(child);

        if (existing is null)
        {
            if (child.Count > 0)
                Write(owner, key, child);
        }
        else if (child.Count == 0)
        {
            Remove(owner, key);
        }
    }

    /// <summary>
    /// Clears a number or a switch: the key goes when it held a value that reads, or nothing (a
    /// JSON null, a blank text). A value the engine cannot read (<c>"Port": "abc"</c>) came back
    /// as null when the account was read, so a null written back is that same value, unchanged:
    /// it stays as the file spelt it, with the finding the run makes of it, until a value that
    /// reads replaces it.
    /// </summary>
    private static void Clear(JsonObject owner, string key, bool readable)
    {
        if (readable
            || Node(owner, key) is not { } written
            || (written is JsonValue value && value.TryGetValue<string>(out var text) && string.IsNullOrWhiteSpace(text)))
        {
            Remove(owner, key);
        }
    }
}
