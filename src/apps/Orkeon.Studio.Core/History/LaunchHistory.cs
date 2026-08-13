using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orkeon.Studio.Core.History;

/// <summary>
/// The bounded list of recent launches, newest first.
/// <para>
/// The bound is the point: this file is appended to on every single run, for the whole life
/// of the installation, and nothing ever prunes it otherwise. <see cref="MaxEntries"/> keeps
/// it a few kilobytes and keeps the UI's "recent" list a list rather than an archive.
/// </para>
/// </summary>
public sealed record LaunchHistory
{
    /// <summary>How many launches are kept; older ones are dropped on write.</summary>
    public const int MaxEntries = 50;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>An empty history.</summary>
    public static LaunchHistory Empty { get; } = new();

    /// <summary>Recent launches, newest first, at most <see cref="MaxEntries"/> of them.</summary>
    [JsonPropertyName("entries")]
    public IReadOnlyList<LaunchHistoryEntry> Entries { get; init; } = [];

    /// <summary>Returns a history with <paramref name="entry"/> in front, truncated to the bound.</summary>
    public LaunchHistory Add(LaunchHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var entries = new List<LaunchHistoryEntry>(Math.Min(Entries.Count + 1, MaxEntries)) { entry };
        entries.AddRange(Entries.Take(MaxEntries - 1));
        return new LaunchHistory { Entries = entries };
    }

    /// <summary>Serializes to the on-disk JSON shape.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, SerializerOptions);

    /// <summary>
    /// Reads a history file. A file that is missing, empty or unreadable yields
    /// <see cref="Empty"/> with an error message: a corrupt history is a lost convenience,
    /// never a reason to stop the user from launching a crew.
    /// </summary>
    public static bool TryParse(string? json, out LaunchHistory history, out string? error)
    {
        history = Empty;

        if (string.IsNullOrWhiteSpace(json))
        {
            error = "The history file is empty.";
            return false;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<LaunchHistory>(json, SerializerOptions);
            if (parsed is null)
            {
                error = "The history file does not contain a history object.";
                return false;
            }

            // Trust nothing about a file a previous version (or a hand edit) wrote: re-apply
            // the bound and drop entries with no target.
            history = new LaunchHistory
            {
                Entries = [.. parsed.Entries.Where(e => !string.IsNullOrWhiteSpace(e.Target)).Take(MaxEntries)],
            };
            error = null;
            return true;
        }
        catch (JsonException ex)
        {
            error = $"The history file is not valid JSON: {ex.Message}";
            return false;
        }
    }
}
