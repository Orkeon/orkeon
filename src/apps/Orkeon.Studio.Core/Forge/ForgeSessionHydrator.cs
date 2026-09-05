using Orkeon.Studio.Core.Events;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using Orkeon.Compliance.Vfs;

namespace Orkeon.Studio.Core.Forge;

/// <summary>
/// Seeds a <see cref="ForgeSessionModel"/> from a session directory before a resume: the
/// stream never replays the past — the artifacts on disk carry it
/// (<c>transcript.jsonl</c>, <c>brief.json</c>, <c>blueprint.json</c>, <c>verdict.json</c>,
/// the SPEC §4.1 layout). Each file is wrapped into the synthetic event the live stream
/// would have emitted, so the model has exactly one reading of every shape. Tolerant
/// throughout: a missing or broken artifact seeds nothing and blocks nothing.
/// </summary>
[SuppressVfsCompliance(
    "EXCEPTION-BOOTSTRAP: Studio is a host application reading the CLI's session artifacts on " +
    "the physical disk, before any VFS mount exists.")]
public static class ForgeSessionHydrator
{
    /// <summary>Seeds <paramref name="model"/> from <paramref name="sessionDirectory"/>.</summary>
    public static void Hydrate(ForgeSessionModel model, string sessionDirectory)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionDirectory);

        // Identity first (review of the dry-pause resume, W-09): a hydrate-only reopen
        // has no live session.started, and every downstream gate — the try-the-team
        // button, the save button — keys on the model's Slug.
        HydrateIdentity(model, sessionDirectory);
        HydrateTranscript(model, Path.Combine(sessionDirectory, "transcript.jsonl"));
        FeedWrapped(model, Path.Combine(sessionDirectory, "brief.json"), ForgeEventKinds.BriefReady, "brief");
        FeedWrapped(model, Path.Combine(sessionDirectory, "blueprint.json"), ForgeEventKinds.BlueprintReady, "blueprint");
        HydrateVerdict(model, sessionDirectory);
    }

    /// <summary>
    /// Wraps <c>session.json</c> into the <c>session.started</c> the live stream would
    /// have opened with, so Slug/Directory/Format have exactly one reading.
    /// </summary>
    private static void HydrateIdentity(ForgeSessionModel model, string sessionDirectory)
    {
        if (!TryReadObject(Path.Combine(sessionDirectory, "session.json"), out var session))
            return;

        var envelope = new JsonObject
        {
            ["v"] = 2,
            ["seq"] = 0,
            ["ts"] = "",
            ["kind"] = ForgeEventKinds.SessionStarted,
            ["slug"] = session["slug"]?.DeepClone(),
            ["dir"] = sessionDirectory,
            ["format"] = session["format"]?.DeepClone(),
            ["resumed"] = true,
        };
        Feed(model, envelope.ToJsonString());
    }

    /// <summary>
    /// The verdict, with the last trial's metrics folded in the way the live
    /// <c>verdict.ready</c> now carries them (W-08) — the recalled screen shows the same
    /// chips as the live one.
    /// </summary>
    private static void HydrateVerdict(ForgeSessionModel model, string sessionDirectory)
    {
        var verdictPath = Path.Combine(sessionDirectory, "verdict.json");
        if (!TryReadObject(Path.Combine(sessionDirectory, "last-run.json"), out var lastRun))
        {
            FeedFlat(model, verdictPath, ForgeEventKinds.VerdictReady);
            return;
        }

        if (!TryReadObject(verdictPath, out var verdict))
            return;

        foreach (var metric in new[] { "durationMs", "tokens", "cacheHitTokens", "cacheMissTokens" })
        {
            if (!verdict.ContainsKey(metric) && lastRun[metric] is { } value)
                verdict[metric] = value.DeepClone();
        }

        var envelope = new JsonObject { ["v"] = 2, ["seq"] = 0, ["ts"] = "", ["kind"] = ForgeEventKinds.VerdictReady };
        foreach (var property in verdict.ToList())
        {
            verdict.Remove(property.Key);
            if (!envelope.ContainsKey(property.Key))
                envelope[property.Key] = property.Value;
        }

        Feed(model, envelope.ToJsonString());
    }

    private static void HydrateTranscript(ForgeSessionModel model, string path)
    {
        foreach (var line in ReadLines(path))
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                var role = ReadString(root, "role");
                var text = ReadString(root, "text");
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                if (string.Equals(role, "user", StringComparison.OrdinalIgnoreCase))
                    model.AddUserMessage(text);
                else
                    Feed(model, $$"""{"v":2,"seq":0,"ts":"","kind":"assistant.message","text":{{JsonSerializer.Serialize(text)}}}""");
            }
            catch (JsonException)
            {
                // A truncated tail is an ordinary crash artifact; the rest still seeds.
            }
        }
    }

    /// <summary>Wraps a whole artifact under one payload property (<c>brief</c>, <c>blueprint</c>).</summary>
    private static void FeedWrapped(ForgeSessionModel model, string path, string kind, string property)
    {
        if (!TryReadObject(path, out var artifact))
            return;

        var envelope = new JsonObject { ["v"] = 2, ["seq"] = 0, ["ts"] = "", ["kind"] = kind, [property] = artifact };
        Feed(model, envelope.ToJsonString());
    }

    /// <summary>Merges a flat artifact (<c>verdict.json</c>) into the envelope, the wire way.</summary>
    private static void FeedFlat(ForgeSessionModel model, string path, string kind)
    {
        if (!TryReadObject(path, out var artifact))
            return;

        var envelope = new JsonObject { ["v"] = 2, ["seq"] = 0, ["ts"] = "", ["kind"] = kind };
        foreach (var property in artifact.ToList())
        {
            artifact.Remove(property.Key);
            if (!envelope.ContainsKey(property.Key))
                envelope[property.Key] = property.Value;
        }

        Feed(model, envelope.ToJsonString());
    }

    private static void Feed(ForgeSessionModel model, string line)
    {
        if (OrkeonEventParser.TryParse(line, out var orkeonEvent))
            model.Feed(orkeonEvent!);
    }

    /// <summary>
    /// Reads one artifact. The try shape is deliberate: a missing, unreadable or non-object
    /// file is "no artifact at all", which is not the same thing as an empty object -- an
    /// empty one would seed a hollow event, so the absence must stay expressible.
    /// </summary>
    private static bool TryReadObject(string path, [NotNullWhen(true)] out JsonObject? artifact)
    {
        try
        {
            artifact = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            artifact = null;
        }

        return artifact is not null;
    }

    private static IEnumerable<string> ReadLines(string path)
    {
        string[] lines;
        try
        {
            if (!File.Exists(path))
                return [];
            lines = File.ReadAllLines(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        return lines.Where(static line => !string.IsNullOrWhiteSpace(line));
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
