using System.Collections.Immutable;
using System.Text.Json;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Hosting.ToolBridge;

/// <summary>
/// The bridge's physical paths scrubbed from the error and the metadata of a tool's answer (#445, ADR-019), as the
/// file system scrubs them from its own refusals: a tool that does not go through the file system (a shell, an MCP
/// server) may name them. A scratch folder becomes the caller's virtual path of its mount; every other base path of the
/// bridge, its mounts, the scratch and its parent, becomes <see cref="Redacted"/>. The result is left as the tool gave
/// it: the caller's pipeline judges it.
/// </summary>
internal static class ToolBridgeRedaction
{
    /// <summary>What a physical path of the bridge is replaced with.</summary>
    public const string Redacted = "[REDACTED]";

    /// <summary>The physical paths of the bridge and their replacements, the longest first.</summary>
    /// <param name="boot">The bridge's own mounts; none when null.</param>
    /// <param name="scratch">The call's scratch; none when null.</param>
    /// <returns>The pairs.</returns>
    public static ImmutableList<(string Physical, string Replacement)> PathsOf(FileSystemRegistry? boot, ToolBridgeScratch? scratch)
    {
        var pairs = new List<(string, string)>();
        if (scratch is not null)
        {
            pairs.AddRange(scratch.Folders.Select(f => (f.Physical, f.Virtual)));
            pairs.Add((scratch.Root, Redacted));
        }

        if (ToolBridgeScratch.KnownParent is { } parent)
            pairs.Add((parent, Redacted));
        foreach (var mount in boot?.GetMounts() ?? [])
            pairs.Add((mount.BasePath, Redacted));
        return [.. pairs
            .Select(p => (Physical: p.Item1.TrimEnd('/', '\\'), Replacement: p.Item2))
            // A root ('/', 'C:') would scrub every path the tool names, virtual ones too.
            .Where(p => p.Physical.Length > 3)
            .DistinctBy(p => p.Physical, StringComparer.Ordinal)
            .OrderByDescending(p => p.Physical.Length)];
    }

    /// <summary>The answer with <paramref name="paths"/> replaced in its error and its metadata.</summary>
    /// <param name="answer">The tool's answer, as the wire carries it.</param>
    /// <param name="paths">The pairs of <see cref="PathsOf"/>.</param>
    /// <returns>The scrubbed answer.</returns>
    public static BridgeCallResponse Scrub(BridgeCallResponse answer, ImmutableList<(string Physical, string Replacement)> paths)
    {
        ArgumentNullException.ThrowIfNull(answer);
        ArgumentNullException.ThrowIfNull(paths);
        if (paths.IsEmpty)
            return answer;
        return answer with
        {
            Error = answer.Error is { } error ? Text(error, paths, encoded: false) : null,
            Metadata = answer.Metadata?.ToImmutableDictionary(e => e.Key, e => Element(e.Value, paths), StringComparer.Ordinal),
        };
    }

    private static StringComparison Comparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static string Text(string text, ImmutableList<(string Physical, string Replacement)> paths, bool encoded)
    {
        foreach (var (physical, replacement) in paths)
        {
            var from = encoded ? Encoded(physical) : physical;
            if (text.Contains(from, Comparison))
                text = text.Replace(from, encoded ? Encoded(replacement) : replacement, Comparison);
        }

        return text;
    }

    private static JsonElement Element(JsonElement value, ImmutableList<(string Physical, string Replacement)> paths)
    {
        var raw = value.GetRawText();
        var scrubbed = Text(raw, paths, encoded: true);
        if (ReferenceEquals(raw, scrubbed) || raw == scrubbed)
            return value;
        using var document = JsonDocument.Parse(scrubbed);
        return document.RootElement.Clone();
    }

    // The text as the serializer writes it inside a JSON string, without the quotes.
    private static string Encoded(string text) => JsonSerializer.Serialize(text, ToolBridgeProtocol.JsonOptions)[1..^1];
}
