using Orkeon.Domain.Tools;

namespace Orkeon.Scripting.Cli.Commands.Forge;

/// <summary>One catalogue entry as the blueprint prompt and the validation see it.</summary>
internal sealed record ForgeToolInfo(string Name, string Description);

/// <summary>
/// The sandbox's tool policy (SPEC-ORKEON-FORGE §9.1): restriction happens by **removing
/// tools from the catalogue** — the list injected into the blueprint prompt and enforced
/// by validation — never by hoping the model abstains. Writes are confined by the mounts
/// (<c>/output</c> only); what escapes the VFS entirely is what gets denied here.
/// </summary>
internal static class ForgeSandbox
{
    /// <summary>Tools that escape the VFS confinement and never enter a forged crew (V1).</summary>
    public static readonly IReadOnlySet<string> DeniedTools =
        new HashSet<string>(StringComparer.Ordinal) { "shell_command", "code_interpreter" };

    /// <summary>
    /// The catalogue a forged crew may draw from: everything the host registered, minus
    /// the denied set, sorted for a stable prompt prefix.
    /// </summary>
    public static IReadOnlyList<ForgeToolInfo> SelectCrewTools(IEnumerable<IBaseTool> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);

        return [.. tools
            .Where(tool => !DeniedTools.Contains(tool.Name))
            .OrderBy(tool => tool.Name, StringComparer.Ordinal)
            .Select(tool => new ForgeToolInfo(tool.Name, tool.Description))];
    }
}
