namespace Orkeon.Infrastructure.MCP;

/// <summary>The version the MCP server and the MCP client introduce themselves with: the running Orkeon release's.</summary>
internal static class McpVersion
{
    /// <summary>
    /// The one version every surface of a release announces (<see cref="Orkeon.Domain.Constants.Platform.OrkeonRelease"/>).
    /// </summary>
    public static string Current => Orkeon.Domain.Constants.Platform.OrkeonRelease.Version;
}
