using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Infrastructure.MCP.Auth;

/// <summary>
/// A server's configuration cannot be honoured (a literal <c>Authorization</c> header, an absent
/// environment variable). Names the settings key, never a value.
/// </summary>
[Experimental("ORKEXP004", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
[SuppressMessage("Design", "CA1032", Justification = "Raised only by this library with a settings key; the standard constructors would build an exception without one.")]
public sealed class McpServerConfigurationException : InvalidOperationException
{
    /// <summary>Creates the exception for <paramref name="key"/>.</summary>
    public McpServerConfigurationException(string key, string message) : base(message) => Key = key;

    /// <summary>The settings key at fault, such as <c>MCP:Servers:tracker:Auth:TokenEnv</c>.</summary>
    public string Key { get; }
}
