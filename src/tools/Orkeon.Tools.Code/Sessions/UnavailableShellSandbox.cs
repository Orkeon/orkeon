namespace Orkeon.Tools.Code.Sessions;

/// <summary>
/// A confinement that cannot run here: a mode for another platform, or a backend not built yet. It
/// reports the reason and never opens a session, so the call fails rather than run unconfined.
/// </summary>
internal sealed class UnavailableShellSandbox : IShellSandbox
{
    private readonly string _reason;

    /// <summary>A sandbox that reports <paramref name="reason"/>.</summary>
    /// <param name="name">The name of the backend that was asked for.</param>
    /// <param name="reason">Why it cannot run here.</param>
    public UnavailableShellSandbox(string name, string reason)
    {
        Name = name;
        _reason = reason;
        Availability = new(reason);
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public bool UsesVirtualPaths => true;

    /// <inheritdoc />
    public ShellSandboxAvailability Availability { get; }

    /// <inheritdoc />
    public Task<IShellSession> OpenAsync(ShellSessionView view, CancellationToken cancellationToken)
        => throw new ShellSessionException($"Confinement unavailable: {_reason}.");
}
