using System.Globalization;
using System.Reflection;

namespace Orkeon.Studio.Core;

/// <summary>
/// The version a Studio front-end reports on <c>--version</c>. It is read from the assembly
/// attributes the build stamps from <c>src/Directory.Build.props</c> — the repository's single
/// source of truth — so no front-end can drift from the packages it ships in, and none of them
/// restates the SourceLink stripping rule the smoke checks depend on.
/// </summary>
public static class StudioAssemblyInfo
{
    /// <summary>Reported when an assembly carries no version attribute at all.</summary>
    public const string UnknownVersion = "0.0.0";

    /// <summary>
    /// The informational version of <paramref name="assembly"/> without its build metadata:
    /// SourceLink appends <c>+&lt;commit sha&gt;</c>, and users — and the packaging smokes —
    /// want the release.
    /// </summary>
    public static string VersionOf(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (string.IsNullOrWhiteSpace(informational))
            return assembly.GetName().Version?.ToString() ?? UnknownVersion;

        var metadata = informational.IndexOf('+', StringComparison.Ordinal);
        return metadata < 0 ? informational : informational[..metadata];
    }

    /// <summary>The single line a <c>--version</c> switch writes: the tool name and its version.</summary>
    public static string VersionLine(string toolName, Assembly assembly)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);

        return string.Create(CultureInfo.InvariantCulture, $"{toolName} {VersionOf(assembly)}");
    }
}
