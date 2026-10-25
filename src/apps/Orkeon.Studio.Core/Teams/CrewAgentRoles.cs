using System.Text.RegularExpressions;
using Orkeon.Compliance.Vfs;

namespace Orkeon.Studio.Core.Teams;

/// <summary>
/// The agents a YAML crew declares, by role (refonte lot 12): the Launch screen gives each its
/// column before the run starts, and the run's events fill it. A reading of the files on the shape
/// every crew of the forge writes — a <c>role:</c> key in the agents' files, or under the agents
/// of a single-file crew — never the engine's parse: it lists and never refuses. A
/// <c>.ork.ts</c> crew is not read; its agents appear as the run announces them.
/// </summary>
[SuppressVfsCompliance(
    "EXCEPTION-BOOTSTRAP: Studio is a host application; a team's crew is user-owned storage on the " +
    "physical disk, read as the team catalog reads it.")]
public static partial class CrewAgentRoles
{
    /// <summary>
    /// The roles of the crew behind <paramref name="targetPath"/>, in the files' order, each once.
    /// Empty when there is none, when the crew is a script, or when nothing can be read.
    /// </summary>
    public static IReadOnlyList<string> In(string? targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
            return [];

        try
        {
            var roles = new List<string>();
            foreach (var file in CrewLlmDeclarations.YamlFilesOf(targetPath))
            {
                // A task file names its agent, it declares none.
                if (string.Equals(Path.GetFileName(Path.GetDirectoryName(file)), "tasks", StringComparison.OrdinalIgnoreCase))
                    continue;

                foreach (var role in Of(File.ReadAllLines(file)))
                {
                    if (!roles.Contains(role, StringComparer.OrdinalIgnoreCase))
                        roles.Add(role);
                }
            }

            return roles;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return [];
        }
    }

    /// <summary>The <c>role:</c> values of one YAML text, unquoted, comments left out.</summary>
    public static IReadOnlyList<string> Of(IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var roles = new List<string>();
        foreach (var line in lines)
        {
            var match = RoleKey().Match(line);
            if (!match.Success)
                continue;

            var value = match.Groups["value"].Value;
            var hash = value.IndexOf(" #", StringComparison.Ordinal);
            if (hash >= 0)
                value = value[..hash];
            value = value.Trim().Trim('"', '\'').Trim();
            if (value.Length > 0)
                roles.Add(value);
        }

        return roles;
    }

    [GeneratedRegex(@"^[ \t]*(?:-[ \t]+)?role[ \t]*:(?<value>.*)$")]
    private static partial Regex RoleKey();
}
