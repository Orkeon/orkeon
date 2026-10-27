using System.Collections.ObjectModel;

namespace Orkeon.Tools.Code;

/// <summary>
/// The <c>Orkeon:Tools:Shell:Environment</c> section: the environment variables a
/// <see cref="ShellCommandTool"/> session and the code sandbox receive on top of the built-in
/// allow-list (#216).
/// </summary>
public sealed class ShellEnvironmentOptions
{
    /// <summary>
    /// Extra variable names propagated from the runner's environment, such as <c>HTTPS_PROXY</c>. A name
    /// that is blank, holds <c>=</c> or a NUL, starts with <c>ORKEON_</c> or contains <c>KEY</c>,
    /// <c>TOKEN</c>, <c>SECRET</c>, <c>PASSWORD</c>, <c>PASSWD</c>, <c>CREDENTIAL</c> or <c>AUTH</c>
    /// (without case) is dropped with a warning; a built-in name is ignored. Under bubblewrap the value
    /// passes whatever the network, but no path it names is bound into the view.
    /// </summary>
    public Collection<string> Propagate { get; } = [];
}
