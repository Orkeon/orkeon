using Orkeon.Scripting.Cli.Events;

namespace Orkeon.Scripting.Cli.Commands.Forge;

/// <summary>
/// The Atelier's view of the shared event protocol: the envelope and the emission come from
/// <see cref="OrkeonEventWriter"/> (BUS-01), this type only adds the typed helpers of the
/// cycle. Forge events carry no crew identity — during the interview no crew is running —
/// so they emit with an empty scope, and the identity fields simply do not appear on the line.
/// </summary>
internal sealed class ForgeEventWriter : OrkeonEventWriter
{
    /// <summary>Creates a writer over <paramref name="output"/> (stdout in the CLI).</summary>
    public ForgeEventWriter(TextWriter output, IOrkeonClock? clock = null)
        : base(output, clock)
    {
    }

    /// <summary>Opening event of a run of the engine.</summary>
    public void SessionStarted(ForgeSession session, bool resumed)
    {
        ArgumentNullException.ThrowIfNull(session);
        Emit("session.started", new
        {
            slug = session.Document.Slug,
            // The session's stable id (STUDIO-25): what its team's forge.json carries, and the
            // one name that survives the folder being moved, renamed or copied. Absent for a
            // session written before the id existed.
            id = session.Document.Id,
            dir = session.Directory,
            format = session.Document.Format,
            resumed,
            // Which build is answering. A client that shows it can tell «this engine reports
            // nothing» from «this engine is too old to report it» — two states that look
            // identical on screen and are fixed in completely different ways.
            engine = ForgeEngineVersion.Current,
            budget = session.Document.Budget.ToEventPayload(),
        });
    }

    /// <summary>The cycle entered a stage.</summary>
    public void StageEntered(ForgeState stage, int iteration) =>
        Emit("stage.entered", new { stage = Spell(stage), iteration });

    /// <summary>An anomaly, recoverable or not.</summary>
    public void Error(string code, string message, bool recoverable) =>
        Emit("error", new { code, message, recoverable });

    /// <summary>
    /// The session folder followed its team (STUDIO-26): <paramref name="from"/> and
    /// <paramref name="to"/> are its slugs, <paramref name="dir"/> its new absolute folder — what a
    /// client that reads the session's files next must read from — and <paramref name="suffixed"/>
    /// says the team's own name was already another session's (D-04).
    /// </summary>
    public void SessionRenamed(string from, string to, string dir, bool suffixed) =>
        Emit("session.renamed", new { from, to, dir, suffixed });

    /// <summary>
    /// Something the command could not do while everything it was asked for stands — a session
    /// folder the disk would not rename after a written promotion (STUDIO-26, D-05). Never an
    /// <c>error</c>: a client that stops on one must not stop here, and the exit code is the
    /// command's own.
    /// </summary>
    public void Warning(string code, string message) =>
        Emit("warning", new { code, message });

    /// <summary>Closing event; mirrors the process exit code.</summary>
    public void SessionFinished(string status, int exitCode) =>
        Emit("session.finished", new { status, exitCode });

    /// <summary>Stage names on the wire are lowercase; the enum spelling stays a C# detail.</summary>
#pragma warning disable CA1308 // lowercase is the wire spelling of the protocol contract, not a comparison normalization
    internal static string Spell(ForgeState state) => state.ToString().ToLowerInvariant();
#pragma warning restore CA1308
}
