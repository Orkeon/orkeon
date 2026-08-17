using System.Collections.Immutable;

namespace Orkeon.Cli.Commands.Scripting.Runtime;

/// <summary>
/// Options for <see cref="ScriptHostFacade"/> — chiefly the virtual directories scanned for
/// <c>&lt;name&gt;/crew.ork.ts</c> crews. The host populates <see cref="CrewDirectories"/>
/// from its mount bootstrap (exp 07 SPEC §9.3); empty means no crews are resolvable.
/// </summary>
public sealed class ScriptHostFacadeOptions
{
    /// <summary>Config section bound to this options type.</summary>
    public const string SectionName = "Orkeon:Cli:ScriptHost";

    /// <summary>
    /// Virtual directories under which crews live as <c>&lt;dir&gt;/&lt;name&gt;/crew.ork.ts</c>.
    /// Resolution tries each directory in order; first match wins.
    /// </summary>
    public ImmutableArray<string> CrewDirectories { get; set; } = ImmutableArray<string>.Empty;

    /// <summary>The crew entry-file name resolved under each <c>&lt;dir&gt;/&lt;name&gt;/</c>.</summary>
    public string CrewFileName { get; set; } = "crew.ork.ts";

    /// <summary>
    /// Upper bound on a single synchronous <c>script-host.runCrew</c> call. When it expires,
    /// the calling script receives a clear <see cref="TimeoutException"/>, the abandoned run is
    /// cancelled cooperatively, and the engine/REPL thread is always released. Default:
    /// 10 minutes — generous on purpose, since <c>runCrew</c> is meant for short crews
    /// (long workflows should go through <c>runCrewAsync</c>'s ticket cycle). Zero or a
    /// negative value disables the bound.
    /// </summary>
    public TimeSpan RunCrewTimeout { get; set; } = TimeSpan.FromMinutes(10);
}
