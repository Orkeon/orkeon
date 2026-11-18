using System.Reflection;

namespace Orkeon.Domain.Constants.Platform;

/// <summary>
/// The version of the Orkeon release that is running, read at run time: what the telemetry, the agent card and
/// the MCP handshake announce, and what the <c>User-Agent</c> of the HTTP tools carries (#416, #418). Every assembly of a release carries
/// the same informational version (<c>VersionPrefix</c> + <c>VersionSuffix</c> of <c>src/Directory.Build.props</c>).
/// </summary>
internal static class OrkeonRelease
{
    /// <summary>The informational version of this assembly, build metadata (<c>+sha</c>) removed.</summary>
    public static string Version { get; } = Read();

    private static string Read()
    {
        var informational = typeof(OrkeonRelease).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var plus = informational?.IndexOf('+', StringComparison.Ordinal) ?? -1;
        var version = plus < 0 ? informational : informational![..plus];
        // An attribute that is absent, empty or only build metadata announces nothing: the assembly version does.
        return string.IsNullOrWhiteSpace(version)
            ? typeof(OrkeonRelease).Assembly.GetName().Version?.ToString(3) ?? "0.0.0"
            : version;
    }
}
