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
            dir = session.Directory,
            format = session.Document.Format,
            resumed,
            budget = session.Document.Budget.ToEventPayload(),
        });
    }

    /// <summary>The cycle entered a stage.</summary>
    public void StageEntered(ForgeState stage, int iteration) =>
        Emit("stage.entered", new { stage = Spell(stage), iteration });

    /// <summary>An anomaly, recoverable or not.</summary>
    public void Error(string code, string message, bool recoverable) =>
        Emit("error", new { code, message, recoverable });

    /// <summary>Closing event; mirrors the process exit code.</summary>
    public void SessionFinished(string status, int exitCode) =>
        Emit("session.finished", new { status, exitCode });

    /// <summary>Stage names on the wire are lowercase; the enum spelling stays a C# detail.</summary>
#pragma warning disable CA1308 // lowercase is the wire spelling of the protocol contract, not a comparison normalization
    internal static string Spell(ForgeState state) => state.ToString().ToLowerInvariant();
#pragma warning restore CA1308
}
