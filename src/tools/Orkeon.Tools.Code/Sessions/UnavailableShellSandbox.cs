namespace Orkeon.Tools.Code.Sessions;

/// <summary>
/// A confinement that cannot run here: a mode for another platform, or a backend not built yet. It
/// reports the reason and never opens a session, so the call fails rather than run unconfined.
/// </summary>
/// <param name="name">The name of the backend that was asked for.</param>
/// <param name="reason">Why it cannot run here.</param>
internal sealed class UnavailableShellSandbox(string name, string reason) : IShellSandbox
{
    /// <inheritdoc />
    public string Name => name;

    /// <inheritdoc />
    public bool UsesVirtualPaths => true;

    /// <inheritdoc />
    public ShellSandboxAvailability Availability { get; } = new(reason);

    /// <inheritdoc />
    public Task<IShellSession> OpenAsync(ShellSessionView view, CancellationToken cancellationToken)
        => throw new ShellSessionException($"Confinement unavailable: {reason}.");
}
