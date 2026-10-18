using Orkeon.Tools.Code.Sessions;

namespace Orkeon.Tools.Code.Tests.Doubles;

/// <summary>
/// A hand-written <see cref="IShellSandbox"/> over a real one: it opens the real sessions and keeps
/// them, so a test can inspect a session the manager opened and owns.
/// </summary>
/// <param name="inner">The sandbox that opens the sessions.</param>
internal sealed class MockShellSandbox(IShellSandbox inner) : IShellSandbox
{
    /// <summary>The sessions opened, in order.</summary>
    public List<IShellSession> Opened { get; } = [];

    public string Name => inner.Name;

    public bool UsesVirtualPaths => inner.UsesVirtualPaths;

    public ShellSandboxAvailability Availability => inner.Availability;

    public async Task<IShellSession> OpenAsync(ShellSessionView view, CancellationToken cancellationToken)
    {
        var session = await inner.OpenAsync(view, cancellationToken);
        lock (Opened)
            Opened.Add(session);
        return session;
    }
}
