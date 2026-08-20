using System.Text;
using Orkeon.Infrastructure.Serialization;

namespace Orkeon.Scripting.Cli.Commands.Forge;

/// <summary>
/// Writes a compilation to the per-entity YAML layout (SPEC-ORKEON-FORGE §8.1):
/// <c>crew/config.yaml</c> + <c>crew/agents/&lt;key&gt;.yaml</c> + <c>crew/tasks/&lt;key&gt;.yaml</c>
/// — the layout <c>YamlCrewDefinitionLoader.LoadFromDirectoryAsync</c> loads and
/// <c>RunTargetDetector</c> recognises, one readable file per agent.
/// <para>
/// The files are serialized from the very DTOs the loader deserializes, with the very
/// serializer the loader uses (camelCase canonical form) — the round-trip is closed by
/// construction. Deliberate deviation from the spec's first idea (recorded in FORGE-03):
/// no <c>YamlCrewExporter</c> extension — the exporter starts from a
/// <c>CrewConfiguration</c>, while the forge holds the upstream DTO form; rendering here
/// avoids a public-API addition and a VFS mount for what is session-directory bootstrap
/// I/O (the CLI assembly is <c>SuppressVfsCompliance</c> by design).
/// </para>
/// </summary>
internal static class ForgeYamlRenderer
{
    /// <summary>Directory of the rendered crew inside a session.</summary>
    public const string CrewDirectoryName = "crew";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Renders <paramref name="compilation"/> under <paramref name="sessionDirectory"/>,
    /// replacing any previous render — a repair must never leave a stale agent file behind.
    /// Returns the written paths, relative to the session directory, in write order.
    /// </summary>
    public static IReadOnlyList<string> Render(ForgeCompilation compilation, string sessionDirectory)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionDirectory);

        var crewDirectory = Path.Combine(sessionDirectory, CrewDirectoryName);
        if (Directory.Exists(crewDirectory))
            Directory.Delete(crewDirectory, recursive: true);

        Directory.CreateDirectory(Path.Combine(crewDirectory, "agents"));
        Directory.CreateDirectory(Path.Combine(crewDirectory, "tasks"));

        var serializer = new YamlDotNetSerializer();
        var written = new List<string>
        {
            Write(sessionDirectory, Path.Combine(CrewDirectoryName, "config.yaml"), serializer.Serialize(compilation.Settings)),
        };

        foreach (var (key, agent) in compilation.Agents.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            written.Add(Write(sessionDirectory, Path.Combine(CrewDirectoryName, "agents", key + ".yaml"), serializer.Serialize(agent)));

        foreach (var (key, task) in compilation.Tasks.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            written.Add(Write(sessionDirectory, Path.Combine(CrewDirectoryName, "tasks", key + ".yaml"), serializer.Serialize(task)));

        return written;
    }

    private static string Write(string sessionDirectory, string relativePath, string yaml)
    {
        File.WriteAllText(Path.Combine(sessionDirectory, relativePath), yaml, Utf8NoBom);
        return relativePath;
    }
}
