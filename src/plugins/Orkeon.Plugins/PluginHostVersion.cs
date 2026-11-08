using System.Reflection;

namespace Orkeon.Plugins;

/// <summary>The version of the Orkeon release the plugins run under.</summary>
internal static class PluginHostVersion
{
    /// <summary>
    /// Informational version of this assembly (<c>VersionPrefix</c> + <c>VersionSuffix</c> of
    /// <c>src/Directory.Build.props</c>), build metadata removed.
    /// </summary>
    public static string Current { get; } = Read();

    private static string Read()
    {
        var informational = typeof(PluginHostVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(PluginHostVersion).Assembly.GetName().Version?.ToString(3)
            ?? "0.0.0";
        var plus = informational.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? informational : informational[..plus];
    }
}
