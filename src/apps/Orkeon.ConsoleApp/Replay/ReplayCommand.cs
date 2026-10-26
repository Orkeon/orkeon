using Orkeon.Cli.Abstractions.Commands;
using Orkeon.Domain.FileSystem;

namespace Orkeon.ConsoleApp.Replay;

/// <summary>
/// <c>/replay &lt;file&gt;</c>: replays a file in the open session, line after line as if typed, then hands the
/// keyboard back — always interactive. Refused while a replay runs (a replay file that replays is one).
/// </summary>
internal sealed class ReplayCommand(ReplayConsoleAdapter replay, IFileSystemService fileSystem) : IInteractiveCommand
{
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
