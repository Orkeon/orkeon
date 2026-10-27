using System.Text;
using Orkeon.Tools.Code.Confinement;

namespace Orkeon.Tools.Code.Sessions;

/// <summary>
/// What a session is opened on: the mounts as the VFS shows them to the agent, the extra read-only
/// host directories and the network. A session keeps the view it was opened on, so a command whose
/// view differs (the ambient file-system scope swapped its mounts) closes it and opens a new one
/// rather than run in a stale view: <see cref="Fingerprint"/> tells two views apart.
/// </summary>
/// <param name="Paths">The mounts and their sub paths, each at its virtual path with its access.</param>
/// <param name="ReadOnlyPaths">Extra absolute host directories bound read-only at their own path.</param>
/// <param name="AllowNetwork">Whether the session may reach the network.</param>
internal sealed record ShellSessionView(
    IReadOnlyList<ConfinedPath> Paths,
    IReadOnlyList<string> ReadOnlyPaths,
    bool AllowNetwork)
{
    private string? _fingerprint;

    /// <summary>A view with no mount, no extra path and no network.</summary>
    public static ShellSessionView Empty { get; } = new([], [], false);

    /// <summary>
    /// The variable names the operator added to the environment allow-list, already filtered
    /// (<see cref="ShellEnvironment.SelectPropagated"/>). Their values pass; no path they name is bound.
    /// </summary>
    public IReadOnlyList<string> PropagatedVariables { get; init; } = [];

    /// <summary>
    /// Whether a confined session receives the operator's session bus
    /// (<c>Orkeon:Tools:Shell:Environment:SessionBus</c>, false by default): its address and, under
    /// bubblewrap, its socket. Through the bus a command can leave the sandbox.
    /// </summary>
    public bool SessionBus { get; init; }

    /// <summary>The sockets of the operator's session a confined backend hands over: the bus only when <see cref="SessionBus"/>.</summary>
    public ShellSessionSockets ConfinedSockets => SessionBus ? ShellSessionSockets.All : ShellSessionSockets.WithoutBus;

    /// <summary>A text equal for two views exactly when they bind the same paths the same way and pass the same variables.</summary>
    public string Fingerprint => _fingerprint ??= Compute();

    private string Compute()
    {
        var builder = new StringBuilder();
        builder.Append(AllowNetwork ? "net" : "nonet").Append(SessionBus ? "+bus" : "");
        foreach (var path in Paths)
            builder.Append('\u001e').Append(path.Source).Append('\u001f').Append(path.Destination).Append('\u001f').Append(path.Access);
        builder.Append('\u001d');
        foreach (var path in ReadOnlyPaths)
            builder.Append('\u001e').Append(path);
        builder.Append('\u001d');
        foreach (var name in PropagatedVariables)
            builder.Append('\u001e').Append(name);
        return builder.ToString();
    }
}
