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
    /// Extra variable names propagated from the runner's environment, such as <c>HTTPS_PROXY</c> when
    /// the proxy needs no credential. A name that is blank, holds <c>=</c> or a NUL, starts with
    /// <c>ORKEON_</c> or contains <c>KEY</c>, <c>TOKEN</c>, <c>SECRET</c>, <c>PASSWORD</c>,
    /// <c>PASSWD</c>, <c>PASS</c>, <c>CREDENTIAL</c> or <c>AUTH</c> or the word <c>PAT</c> on its own
    /// (without case), or whose value carries URL credentials (<c>http://user:password@proxy:3128</c>,
    /// <c>user:password@proxy:3128</c>), is dropped with a warning that never quotes the value; a
    /// built-in name is ignored. Names compare with case, except on Windows. Under bubblewrap the value
    /// passes whatever the network, but no path it names is bound into the view. The code sandbox
    /// reads this list from configuration only: one set in code (<c>Configure&lt;ShellToolOptions&gt;</c>)
    /// reaches the shell alone.
    /// </summary>
    public Collection<string> Propagate { get; } = [];

    /// <summary>
    /// Whether a confined shell session (bubblewrap, Seatbelt, AppContainer) receives the operator's
    /// session bus: <c>DBUS_SESSION_BUS_ADDRESS</c> and, under bubblewrap with the network, its socket.
    /// False by default. The bus lets a command leave the sandbox (<c>systemd-run --user</c>) and read
    /// every keyring secret (<c>secret-tool</c>), so turning it on is logged as a warning. An
    /// unconfined session and the code sandbox, which no jail holds, always receive it.
    /// </summary>
    public bool SessionBus { get; set; }
}
