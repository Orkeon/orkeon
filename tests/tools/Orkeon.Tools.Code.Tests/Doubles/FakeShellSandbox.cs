using Orkeon.Tools.Code.Sessions;

namespace Orkeon.Tools.Code.Tests.Doubles;

/// <summary>
/// A hand-written <see cref="IShellSandbox"/>: opens a <see cref="FakeShellSession"/> per call and
/// keeps them, with the view each was opened on.
/// </summary>
internal sealed class FakeShellSandbox : IShellSandbox
{
    /// <summary>The sandbox's name; two fakes with the same name share a run's session.</summary>
    public string Name { get; init; } = "fake";

    public bool UsesVirtualPaths { get; init; }

    /// <summary>What <see cref="Availability"/> answers.</summary>
    public ShellSandboxAvailability Availability { get; set; } = ShellSandboxAvailability.Available;

    /// <summary>Applied to each session before it is handed out.</summary>
    public Action<FakeShellSession>? Configure { get; set; }

    /// <summary>The sessions opened, in order.</summary>
    public List<FakeShellSession> Sessions { get; } = [];

    /// <summary>The view of each opened session, in order.</summary>
    public List<ShellSessionView> Views { get; } = [];

    public Task<IShellSession> OpenAsync(ShellSessionView view, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FakeShellSession session;
        lock (Sessions)
        {
            session = new FakeShellSession { Number = Sessions.Count + 1 };
            Sessions.Add(session);
            Views.Add(view);
        }

        Configure?.Invoke(session);
        return Task.FromResult<IShellSession>(session);
    }
}
