using System.Text.RegularExpressions;
using Orkeon.Compliance.Vfs;
using Orkeon.Constants.Llm;

namespace Orkeon.Studio.Core.Teams;

/// <summary>
/// The models a YAML crew names itself (STUDIO-73): an agent's own <c>llm:</c> block that pins a
/// profile or a model, a task's <c>llm_override.profile</c>. The engine resolves them before the
/// host's default (<c>ExecutionOrchestrator.LlmFor</c>), so the choice of a team's model does not
/// reach them, and the card and the Run screen say so. A reading of the files, never the engine's: a
/// best effort on the shape every crew of the forge writes, which counts and never refuses. A
/// <c>.ork.ts</c> crew is not read.
/// </summary>
[SuppressVfsCompliance(
    "EXCEPTION-BOOTSTRAP: Studio is a host application; a team's crew is user-owned storage on the " +
    "physical disk, read as the team catalog reads it.")]
public static partial class CrewLlmDeclarations
{
    /// <summary>
    /// How many agents and tasks of the crew behind <paramref name="targetPath"/> name their own
    /// model — a team folder, its <c>crew/</c>, or a crew file. Zero when there is none, when the
    /// crew is a script, or when nothing can be read.
    /// </summary>
    public static int CountIn(string? targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
            return 0;

        try
        {
            return YamlFilesOf(targetPath).Sum(file => Count(File.ReadAllLines(file)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return 0;
        }
    }

    /// <summary>
    /// How many blocks of one YAML text name their own model: a <c>llm:</c> key whose value, inline
    /// or on the indented lines under it, sets a <c>profile</c> other than <c>default</c> or a
    /// <c>model</c>; a <c>llm_override:</c> key whose block sets a <c>profile</c>.
    /// </summary>
    public static int Count(IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var count = 0;
        for (var index = 0; index < lines.Count; index++)
        {
            var key = BlockKey().Match(lines[index]);
            if (!key.Success)
                continue;

            var isOverride = key.Groups["key"].Value == "llm_override";
            var inline = key.Groups["value"].Value.Trim();
            var names = inline.Length > 0 && !inline.StartsWith('#')
                ? InlineNames(inline, isOverride)
                : Names(ChildrenOf(lines, index, key.Groups["indent"].Value.Length), isOverride);
            count += names ? 1 : 0;
        }

        return count;
    }

    /// <summary>Whether an inline value pins a model: « llm: writer » names a profile; a flow map « { profile: x } » is read as a block.</summary>
    private static bool InlineNames(string inline, bool isOverride) =>
        inline.StartsWith('{')
            ? Names(inline.Trim('{', '}').Split(','), isOverride)
            : !isOverride && Names(Unquote(inline));

    /// <summary>The lines indented under the key of line <paramref name="index"/>, blank and comment lines left out.</summary>
    private static List<string> ChildrenOf(IReadOnlyList<string> lines, int index, int indent)
    {
        var children = new List<string>();
        for (var next = index + 1; next < lines.Count; next++)
        {
            if (string.IsNullOrWhiteSpace(lines[next]) || lines[next].TrimStart().StartsWith('#'))
                continue;
            if (lines[next].Length - lines[next].TrimStart().Length <= indent)
                break;
            children.Add(lines[next]);
        }

        return children;
    }

    /// <summary>Whether the entries of one block pin a model: a <c>profile</c> other than the default, or — for an agent — a <c>model</c>.</summary>
    private static bool Names(IEnumerable<string> entries, bool isOverride)
    {
        foreach (var entry in entries)
        {
            var pair = Entry().Match(entry);
            if (!pair.Success)
                continue;

            var value = Unquote(pair.Groups["value"].Value.Trim());
            if (pair.Groups["key"].Value == "profile" && Names(value))
                return true;
            if (!isOverride && pair.Groups["key"].Value == "model" && value.Length > 0)
                return true;
        }

        return false;
    }

    /// <summary>Whether a profile name names another profile than the host's default.</summary>
    private static bool Names(string profile) =>
        profile.Length > 0 && !string.Equals(profile, LlmProfileNames.Default, StringComparison.OrdinalIgnoreCase);

    private static string Unquote(string value)
    {
        var hash = value.IndexOf(" #", StringComparison.Ordinal);
        if (hash >= 0)
            value = value[..hash];
        return value.Trim().Trim('"', '\'');
    }

    /// <summary>The YAML files of the crew: the file itself, or every YAML file of the crew folder and of its <c>agents/</c> and <c>tasks/</c>.</summary>
    internal static IEnumerable<string> YamlFilesOf(string targetPath)
    {
        if (File.Exists(targetPath))
            return IsYaml(targetPath) ? [targetPath] : [];

        if (!Directory.Exists(targetPath))
            return [];

        var crew = Path.Combine(targetPath, "crew");
        var root = Directory.Exists(crew) ? crew : targetPath;
        return new[] { root, Path.Combine(root, "agents"), Path.Combine(root, "tasks") }
            .Where(Directory.Exists)
            .SelectMany(folder => Directory.EnumerateFiles(folder))
            .Where(IsYaml)
            .Order(StringComparer.Ordinal);
    }

    private static bool IsYaml(string path) =>
        path.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".yml", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"^(?<indent>[ \t]*)(?:-[ \t]+)?(?<key>llm|llm_override)[ \t]*:(?<value>.*)$")]
    private static partial Regex BlockKey();

    [GeneratedRegex(@"^[ \t]*(?:-[ \t]+)?(?<key>[A-Za-z_]+)[ \t]*:(?<value>.*)$")]
    private static partial Regex Entry();
}
