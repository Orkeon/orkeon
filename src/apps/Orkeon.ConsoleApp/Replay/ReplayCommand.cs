using Orkeon.Cli.Abstractions.Commands;
using Orkeon.Domain.FileSystem;

namespace Orkeon.ConsoleApp.Replay;

/// <summary>
/// <c>/replay &lt;file&gt;</c>: replays a file in the open session, line after line as if typed, then hands the
/// keyboard back — always interactive. Refused while a replay runs (a replay file that replays is one).
/// </summary>
/// <remarks>
/// The replay console is taken lazily, at the first execution, never at construction: this command is one of
/// the universal commands, which the console's completion reads, and the console decorator is the replay's own
/// (#212: taken at construction, the console that is built resolves the completion, which resolves this command,
/// which resolves the console being built — the start then hangs with stdin a terminal, where the line editor
/// or the TUI takes the completion; stdin redirected takes the dumb adapter, which does not).
/// </remarks>
internal sealed class ReplayCommand(Func<ReplayConsoleAdapter> replay, IFileSystemService fileSystem) : IInteractiveCommand
{
    private readonly Func<ReplayConsoleAdapter> _replay = replay ?? throw new ArgumentNullException(nameof(replay));

    /// <inheritdoc />
    public string Name => "replay";

    /// <inheritdoc />
    public IReadOnlyList<string> Aliases => [];

    /// <inheritdoc />
    public string Description => "Replay a file of input lines as if typed: replay <file>";

    /// <inheritdoc />
    public async Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Args.Count == 0)
            return CommandResult.Fail("usage: /replay <file>");
        var replay = _replay();
        if (replay.IsActive)
            return CommandResult.Fail("a replay is already running");

        var load = await ReplayFile.LoadAsync(fileSystem, string.Join(' ', context.Args), cancellationToken).ConfigureAwait(false);
        if (load.Lines is not { } lines)
            return CommandResult.Fail(load.Error ?? $"cannot read the replay file {load.VirtualPath}");

        return replay.TryStart(lines, closeInputAtEnd: false)
            ? CommandResult.Continue($"replaying {load.VirtualPath}: {lines.Count} line(s)")
            : CommandResult.Fail("a replay is already running");
    }
}
