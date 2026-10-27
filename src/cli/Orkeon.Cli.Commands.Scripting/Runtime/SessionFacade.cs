using Orkeon.Infrastructure.Session;

namespace Orkeon.Cli.Commands.Scripting.Runtime;

#pragma warning disable IDE1006 // camelCase methods: this CLR type is exposed to scripts as ctx.services.get("session")

/// <summary>
/// The REPL's persisted session, as a command handles it: <c>ctx.services.get("session")</c> (#227). Served when the
/// session is persisted per project (<c>Orkeon:Cli:Session:Persist</c>, on by default in the REPL). It saves now what
/// the REPL saves at exit, and loads another saved session in place of the conversation buffer and the state bag -
/// the two things <c>/clear</c> resets - leaving the running command instances as they are.
/// </summary>
public sealed class SessionFacade
{
    private readonly FileSessionBufferService _session;

    internal SessionFacade(FileSessionBufferService session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
    }

    /// <summary>
    /// Writes the session now to its file - the file and the shape Persist writes at exit - and returns that file's
    /// virtual path.
    /// </summary>
    /// <exception cref="InvalidOperationException">The host has not named the session's project yet.</exception>
    public async Task<string> save()
    {
        var path = _session.SessionFilePath
            ?? throw new InvalidOperationException("session.save: the REPL has not opened the project's session yet; nothing to save to.");
        await _session.SaveAsync(CancellationToken.None).ConfigureAwait(false);
        return path;
    }

    /// <summary>
    /// Swaps the conversation buffer, the state bag, the id and the title for those of a session file the REPL saved,
    /// read at the virtual <paramref name="path"/>, and returns the number of messages loaded. The next turn continues
    /// the loaded conversation; the session goes on saving to its own file. A file that is missing, unreadable or not
    /// of the shape the REPL writes is refused whole: the session stays as it was and the promise rejects.
    /// </summary>
    /// <param name="path">The virtual path of the file, e.g. <c>/workspace/.orkeon/sessions/app-1a2b3c4d5e6f/last.json</c>.</param>
    public async Task<int> load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("session.load: name the session file to load, e.g. '/workspace/saved/last.json'.", nameof(path));
        await _session.LoadFromAsync(path.Trim(), CancellationToken.None).ConfigureAwait(false);
        return _session.MessageCount;
    }
}
