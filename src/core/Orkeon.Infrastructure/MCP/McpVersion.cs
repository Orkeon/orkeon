using System.Reflection;

namespace Orkeon.Infrastructure.MCP;

/// <summary>The version the MCP server and the MCP client introduce themselves with: the running Orkeon release's.</summary>
internal static class McpVersion
{
    /// <summary>
    /// Informational version of this assembly (<c>VersionPrefix</c> + <c>VersionSuffix</c> of
    /// <c>src/Directory.Build.props</c>), build metadata removed.
    /// </summary>
    public static string Current { get; } = Read();

    private static string Read()
    {
        var informational = typeof(McpVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(McpVersion).Assembly.GetName().Version?.ToString(3)
            ?? "0.0.0";
        var plus = informational.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? informational : informational[..plus];
    }
}
