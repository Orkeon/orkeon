using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Profiles;
using Orkeon.Studio.Core.Teams;

namespace Orkeon.Studio.Wpf.ViewModels.Capture.Worlds;

/// <summary>
/// One folder a machine offers, as a triple rather than a mount string.
/// <para>
/// Structured on purpose: the physical half is only known once the sandbox exists, and on Windows
/// it carries a drive colon — the very separator a mount string uses. Composing the string in the
/// writer, through <c>FileSystemMount.Quote</c>, is what keeps <c>C:\Users\...</c> from parsing as
/// two segments.
/// </para>
/// </summary>
/// <param name="Folder">Folder name under the world's data directory; empty for one that is absent.</param>
/// <param name="VirtualPath">The name the agents use.</param>
/// <param name="Rights">Docker-style rights, <c>ro</c> or <c>rw</c>.</param>
internal sealed record MountSeed(string Folder, string VirtualPath, string Rights);

/// <summary>One adopted team to write to disk.</summary>
/// <param name="Slug">Folder name under the teams root.</param>
/// <param name="Metadata">The sidecar, without its mounts — those come from <paramref name="Mounts"/>.</param>
/// <param name="Mounts">The folders the team may see.</param>
/// <param name="AgentFileNames">Crew agent files, so the real target probe recognises a team.</param>
/// <param name="TaskFileNames">Crew task files.</param>
internal sealed record TeamSeed(
    string Slug,
    StudioTeamMetadata Metadata,
    IReadOnlyList<MountSeed> Mounts,
    IReadOnlyList<string> AgentFileNames,
    IReadOnlyList<string> TaskFileNames);

/// <summary>
/// One forge session to write to disk — the zero-process route into a populated wizard.
/// <para>
/// Every field maps to a file the hydrator reads. A session written with
/// <see cref="State"/> = <c>Test</c> reopens on the Composer without the engine ever running,
/// which is what makes the wizard's later steps photographable at all.
/// </para>
/// </summary>
internal sealed record SessionSeed
{
    /// <summary>Session slug, and its directory name.</summary>
    public required string Slug { get; init; }

    /// <summary>Engine state, as <c>session.json</c> spells it.</summary>
    public required string State { get; init; }

    /// <summary>Session lifecycle, as <c>session.json</c> spells it.</summary>
    public required string Status { get; init; }

    /// <summary>Display name of the session.</summary>
    public string? Title { get; init; }

    /// <summary>Last save instant; the list sorts on it, so it is pinned rather than "now".</summary>
    public required string UpdatedAt { get; init; }

    /// <summary>
    /// The team this session was promoted to, by slug. Resolved to a path at write time — and it
    /// is that path which makes «Modifier» light up on the team card, since the reverse lookup goes
    /// through <c>promotedTo</c> and nothing else.
    /// </summary>
    public string? PromotedToTeamSlug { get; init; }

    /// <summary>Render format.</summary>
    public string Format { get; init; } = "yaml";

    /// <summary>Lines of <c>transcript.jsonl</c>.</summary>
    public IReadOnlyList<string> Transcript { get; init; } = [];

    /// <summary>Body of <c>brief.json</c>, or null to write none.</summary>
    public string? Brief { get; init; }

    /// <summary>Body of <c>blueprint.json</c>, or null to write none.</summary>
    public string? Blueprint { get; init; }

    /// <summary>Body of <c>verdict.json</c>, or null for a session that never reached one.</summary>
    public string? Verdict { get; init; }

    /// <summary>Body of <c>last-run.json</c>, whose metrics the verdict card folds in.</summary>
    public string? LastRun { get; init; }
}

/// <summary>
/// One past run of a seeded team. The team's folder only exists once the sandbox does, so the run
/// names its team by slug and the writer resolves the path.
/// </summary>
/// <param name="TeamSlug">The team that was launched.</param>
/// <param name="StartedAt">When it started, pinned rather than relative so two runs of the campaign agree.</param>
/// <param name="ExitCode">Exit code, or null for a run that never started.</param>
/// <param name="Outcome">How it is reported.</param>
/// <param name="DurationSeconds">Wall time, or null.</param>
/// <param name="Tokens">Total tokens, or null for an unmetered run.</param>
/// <param name="CacheHitTokens">Cache-served prompt tokens, or null.</param>
internal sealed record RunSeed(
    string TeamSlug,
    DateTimeOffset StartedAt,
    int? ExitCode,
    RunOutcome Outcome,
    double? DurationSeconds,
    long? Tokens = null,
    long? CacheHitTokens = null);

/// <summary>
/// A whole machine, described as data: what is on its disk, what its CLI answers, and whether it
/// has one at all. Nothing here touches a filesystem — <see cref="CaptureWorldWriter"/> does that,
/// which is what lets the fixture be asserted without one.
/// </summary>
internal sealed record CaptureWorldPlan
{
    /// <summary>Short name, used for the world's folder and in failure messages.</summary>
    public required string Name { get; init; }

    /// <summary>Whether the machine has an <c>orkeon</c> binary at all.</summary>
    public bool CliInstalled { get; init; } = true;

    /// <summary>Folders created for real under the data directory, so probing them tells the truth.</summary>
    public IReadOnlyList<string> DataFolders { get; init; } = [];

    /// <summary>The folders declared in <c>appsettings.json</c>.</summary>
    public IReadOnlyList<MountSeed> DeclaredMounts { get; init; } = [];

    /// <summary>The LLM section of <c>appsettings.json</c>, as a JSON object body.</summary>
    public string LlmJson { get; init; } = """{ "Provider": "ollama", "Model": "qwen3:8b" }""";

    /// <summary>Extra top-level sections of <c>appsettings.json</c>, as JSON object bodies by key.</summary>
    public IReadOnlyDictionary<string, string> ExtraSections { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Teams to adopt.</summary>
    public IReadOnlyList<TeamSeed> Teams { get; init; } = [];

    /// <summary>Forge sessions to write.</summary>
    public IReadOnlyList<SessionSeed> Sessions { get; init; } = [];

    /// <summary>Past runs, newest first — the History cards and the teams' last-run lines.</summary>
    public IReadOnlyList<RunSeed> History { get; init; } = [];

    /// <summary>The model profiles.</summary>
    public ModelProfileSet Profiles { get; init; } = ModelProfileSet.Empty;

    /// <summary>What <c>orkeon doctor --json</c> answers.</summary>
    public string DoctorJson { get; init; } = "[]";

    /// <summary>What <c>orkeon --version</c> answers.</summary>
    public string VersionLine { get; init; } = "orkeon 1.0.0-rc.2";

    /// <summary>The event stream <c>orkeon run</c> plays.</summary>
    public IReadOnlyList<string> RunStream { get; init; } = [];

    /// <summary>The event stream <c>orkeon forge</c> plays.</summary>
    public IReadOnlyList<string> ForgeStream { get; init; } = [];
}
