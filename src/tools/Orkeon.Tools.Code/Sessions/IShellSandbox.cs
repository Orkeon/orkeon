namespace Orkeon.Tools.Code.Sessions;

/// <summary>Whether a sandbox can open a session on this host, or why not.</summary>
/// <param name="Reason">Why it cannot, else null.</param>
internal sealed record ShellSandboxAvailability(string? Reason)
{
    /// <summary>The sandbox can open a session here.</summary>
    public static readonly ShellSandboxAvailability Available = new((string?)null);

    /// <summary>True when <see cref="Reason"/> is null.</summary>
    public bool IsAvailable => Reason is null;
}

/// <summary>
/// One way of running an agent's shell session: on the host (<see cref="UnconfinedShellSandbox"/>)
/// or confined (<see cref="BubblewrapShellSandbox"/> on Linux; Seatbelt and AppContainer plug in the
/// same way). A sandbox holds no session: <see cref="ShellSessionManager"/> keeps them per agent run.
/// </summary>
internal interface IShellSandbox
{
    /// <summary>
    /// A stable name of the kind of sandbox: an agent run keeps one session per name, whichever
    /// tool instance asks for it.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Whether the session sees the virtual paths (a confined view built from the VFS): the
    /// arguments and working directory are then passed virtual, else translated to physical ones.
    /// </summary>
    bool UsesVirtualPaths { get; }

    /// <summary>Whether a session can be opened here, or why not.</summary>
    ShellSandboxAvailability Availability { get; }

    /// <summary>Starts a session in <paramref name="view"/>.</summary>
    /// <exception cref="ShellSessionException">The session could not start.</exception>
    Task<IShellSession> OpenAsync(ShellSessionView view, CancellationToken cancellationToken);
}
