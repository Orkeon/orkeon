using System.Text.Json.Serialization;
using Orkeon.Constants.FileSystem;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Scripting.Cli.Commands.Forge;

/// <summary>
/// One folder of the team (STUDIO-46): a virtual root its agents address, whether they read
/// it or write to it, and why. The forge LLM extracts the list from the request into the
/// brief (<c>folders</c>); the user confirms it (<c>folders.confirmed</c>); from then on it is
/// the one list every consumer reads — the blueprint prompt, the deliverable check, the trial
/// bench's mounts, the promoted team's launcher and Studio's sidecar.
/// </summary>
internal sealed record ForgeFolder
{
    /// <summary>The virtual root, absolute and one segment: <c>/inpdf</c>.</summary>
    [JsonPropertyName("path")]
    public string? Path { get; init; }

    /// <summary><c>input</c> (the team reads it) or <c>output</c> (the team writes to it).</summary>
    [JsonPropertyName("role")]
    public string? Role { get; init; }

    /// <summary>What the folder holds, in the user's words.</summary>
    [JsonPropertyName("purpose")]
    public string? Purpose { get; init; }

    /// <summary>
    /// The real directory the user bound behind the folder, absolute — set by a confirmation
    /// only, never by the LLM, and kept out of the brief: a physical path never reaches a
    /// prompt. Null means «inside the team»: the session holds the folder until the adoption.
    /// </summary>
    [JsonPropertyName("dir")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Directory { get; init; }

    /// <summary>Whether the team only reads this folder.</summary>
    [JsonIgnore]
    public bool IsInput => string.Equals(Role, ForgeFolders.InputRole, StringComparison.Ordinal);

    /// <summary>The folder's name — its root without the leading slash (<c>inpdf</c>).</summary>
    [JsonIgnore]
    public string Name => Path is { Length: > 1 } path ? path[1..] : "";
}

/// <summary>The confirmed list, as the session keeps it (<c>folders.json</c>).</summary>
internal sealed record ForgeFolderList
{
    /// <summary>The folders, in the order the user confirmed them.</summary>
    [JsonPropertyName("folders")]
    public IReadOnlyList<ForgeFolder> Folders { get; init; } = [];
}

/// <summary>Where one folder lands during the trial: the physical directory behind its root.</summary>
/// <param name="Folder">The confirmed folder.</param>
/// <param name="PhysicalPath">The directory mounted behind it for the trial.</param>
internal sealed record ForgeTrialMount(ForgeFolder Folder, string PhysicalPath)
{
    /// <summary>The mount spec the runner reads (<c>"dir":/inpdf:ro</c>).</summary>
    public string Spec =>
        $"{FileSystemMount.Quote(PhysicalPath)}:{Folder.Path}:{(Folder.IsInput ? "ro" : "rw")}";
}

/// <summary>
/// The rules of the folder list (STUDIO-46): what a valid list is, the defaults when the
/// request names none, which list a session uses, and where each folder lands for the trial.
/// </summary>
internal static class ForgeFolders
{
    /// <summary>The role of a folder the team reads.</summary>
    public const string InputRole = "input";

    /// <summary>The role of a folder the team writes to.</summary>
    public const string OutputRole = "output";

    /// <summary>The session file holding the confirmed list.</summary>
    public const string FileName = "folders.json";

    /// <summary>
    /// Where the session keeps the folders it holds for the team until the adoption — the
    /// outputs the trial writes to, the inputs answered «inside the team».
    /// </summary>
    public const string SessionDirectoryName = "folders";

    /// <summary>
    /// The roots a team cannot take: the ones every runner keeps for itself, and the forge's
    /// own session root.
    /// </summary>
    private static readonly IReadOnlyList<string> ReservedRoots =
        [.. RunnerVirtualRoots.All, RunnerVirtualRoots.Forge];

    /// <summary>
    /// The structural rules of a list: absolute one-segment paths the mount grammar can spell,
    /// no reserved root, a known role, no root twice. An empty list is a valid one, and so is a
    /// list with no output (STUDIO-57): a team that sends mails from what it reads writes no
    /// file, and its result is what it did — the run's own output, which the judge reads.
    /// <paramref name="where"/> prefixes each message (<c>folders</c> in a brief).
    /// </summary>
    public static IReadOnlyList<string> Validate(IReadOnlyList<ForgeFolder>? folders, string where = "folders")
    {
        var errors = new List<string>();
        if (folders is not { Count: > 0 })
            return errors;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < folders.Count; i++)
        {
            var folder = folders[i];
            var label = $"{where}[{i}]";

            if (!IsWellFormedRoot(folder.Path))
            {
                errors.Add(
                    $"{label}: 'path' is '{folder.Path}', and a folder is one absolute segment such as '/inpdf' — "
                    + "letters, digits, '-', '_' or '.', no '/', '\\', ':', ';' or space inside.");
            }
            else if (ReservedRoots.Contains(folder.Path!, StringComparer.OrdinalIgnoreCase))
            {
                errors.Add($"{label}: '{folder.Path}' is reserved by the runner; name the folder otherwise.");
            }
            else if (!seen.Add(folder.Path!))
            {
                errors.Add($"{label}: '{folder.Path}' is listed twice.");
            }

            if (folder.Role is not (InputRole or OutputRole))
                errors.Add($"{label}: 'role' must be 'input' or 'output', not '{folder.Role}'.");

            if (folder.Directory is { } directory && !System.IO.Path.IsPathFullyQualified(directory))
                errors.Add($"{label}: 'dir' must be an absolute directory.");
        }

        return errors;
    }

    /// <summary>
    /// Whether <paramref name="path"/> is a root the list can hold: <c>/</c> then one segment
    /// of letters, digits, <c>-</c>, <c>_</c> or <c>.</c> — never <c>.</c> or <c>..</c> alone.
    /// </summary>
    public static bool IsWellFormedRoot(string? path)
    {
        if (path is not { Length: > 1 } value || value[0] != '/')
            return false;

        var name = value[1..];
        if (name is "." or "..")
            return false;

        foreach (var c in name)
        {
            if (!(char.IsLetterOrDigit(c) || c is '-' or '_' or '.'))
                return false;
        }

        return true;
    }

    /// <summary>
    /// The folders proposed when the request names none: <c>/workspace</c> to read, only when
    /// the team reads files (the brief's <c>readsFiles</c>; when the assistant did not say,
    /// when something comes in — STUDIO-57: a team given a URL or a text reads no folder), and
    /// <c>/output</c> to write — the two roots a team had before the list existed, now a
    /// proposal the user confirms rather than a default nobody chose. Each purpose is said in
    /// the brief's language.
    /// </summary>
    public static IReadOnlyList<ForgeFolder> Defaults(ForgeBrief brief)
    {
        ArgumentNullException.ThrowIfNull(brief);

        var folders = new List<ForgeFolder>();
        if (brief.ReadsFiles ?? brief.Inputs is { Count: > 0 })
        {
            folders.Add(new ForgeFolder
            {
                Path = RunnerVirtualRoots.Workspace,
                Role = InputRole,
                Purpose = DefaultPurpose(brief.Language, InputRole),
            });
        }

        folders.Add(new ForgeFolder
        {
            Path = RunnerVirtualRoots.Output,
            Role = OutputRole,
            Purpose = DefaultPurpose(brief.Language, OutputRole),
        });
        return folders;
    }

    /// <summary>What a default folder holds, in the brief's language (<c>fr</c>, else English).</summary>
    public static string DefaultPurpose(string? language, string role)
    {
        var french = string.Equals(language, "fr", StringComparison.OrdinalIgnoreCase);
        return string.Equals(role, InputRole, StringComparison.Ordinal)
            ? (french ? "Ce que l'équipe lit." : "What the team reads.")
            : (french ? "Où l'équipe écrit ses résultats." : "Where the team writes its results.");
    }

    /// <summary>Whether a brief's proposal is the defaults: the request named no folder.</summary>
    public static bool IsDefaultProposal(ForgeBrief brief)
    {
        ArgumentNullException.ThrowIfNull(brief);
        return brief.Folders is not { Count: > 0 };
    }

    /// <summary>The list a brief proposes: its own folders, or the defaults when it names none.</summary>
    public static IReadOnlyList<ForgeFolder> ProposalOf(ForgeBrief brief)
    {
        ArgumentNullException.ThrowIfNull(brief);
        return IsDefaultProposal(brief) ? Defaults(brief) : [.. brief.Folders!.Select(WithoutDirectory)];
    }

    /// <summary>
    /// The folders of a confirmed list the session holds for the team — the ones bound to no
    /// directory, kept «inside the team» — each with its session directory
    /// (<c>folders/&lt;name&gt;</c>), created here so the user can drop files in it before the
    /// trial (STUDIO-57): a folder that exists is one an explorer can open.
    /// </summary>
    public static IReadOnlyList<ForgeFolder> Hold(ForgeSession session, IReadOnlyList<ForgeFolder> folders)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(folders);

        var held = new List<ForgeFolder>();
        foreach (var folder in folders)
        {
            if (folder.Directory is { Length: > 0 } || folder.Path is not { Length: > 1 })
                continue;

            var directory = SessionFolder(session, folder);
            System.IO.Directory.CreateDirectory(directory);
            held.Add(folder with { Directory = directory });
        }

        return held;
    }

    /// <summary>The folder as the brief and the prompts carry it: no physical path.</summary>
    public static ForgeFolder WithoutDirectory(ForgeFolder folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        return folder with { Directory = null };
    }

    /// <summary>The confirmed list of <paramref name="session"/>, or null before the confirmation.</summary>
    public static IReadOnlyList<ForgeFolder>? Confirmed(ForgeSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.TryLoadArtifact<ForgeFolderList>(FileName)?.Folders;
    }

    /// <summary>
    /// The list a session's team uses: the confirmed one — empty included, when the user kept
    /// no folder at all (STUDIO-57); for a session that never confirmed one — rebuilt by
    /// <c>forge reopen</c> from a team folder — the brief's, else what its plan addresses (a
    /// reading agent reads <c>/workspace</c>, each deliverable root is written).
    /// </summary>
    public static IReadOnlyList<ForgeFolder> Of(ForgeSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (Confirmed(session) is { } confirmed)
            return confirmed;

        if (session.TryLoadArtifact<ForgeBrief>(ForgeSession.BriefFileName)?.Folders is { Count: > 0 } briefed)
            return briefed;

        return Derived(session.TryLoadArtifact<ForgeBlueprint>(ForgeSession.BlueprintFileName));
    }

    /// <summary>The folders a plan addresses by itself, for a session that holds no list.</summary>
    public static IReadOnlyList<ForgeFolder> Derived(ForgeBlueprint? blueprint) =>
    [
        .. ForgePromoter.DerivedMounts(blueprint).Select(mount => new ForgeFolder
        {
            Path = mount.VirtualRoot,
            Role = mount.ReadOnly ? InputRole : OutputRole,
        }),
    ];

    /// <summary>
    /// Where each folder lands during the trial. An output is always the session's own
    /// <c>folders/&lt;name&gt;</c>: the trial never writes into a real folder of the user's, and
    /// the run's snapshot takes it from there. An input reads the directory the user bound
    /// behind it; else the first unbound input reads <c>--read</c> when one is given; else the
    /// session's <c>folders/&lt;name&gt;</c> — the folder «inside the team», readable before the
    /// team exists (the user drops files in it, STUDIO-57) and moved into it at the adoption.
    /// Only a session that never confirmed a list — rebuilt from a team folder, its folders
    /// derived from the plan — keeps reading the workspace behind <c>/workspace</c>, as it did
    /// before the list existed. Every session directory is created here.
    /// </summary>
    public static IReadOnlyList<ForgeTrialMount> TrialMounts(
        ForgeSession session, IReadOnlyList<ForgeFolder> folders, string workspace, string? readRoot)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspace);

        var mounts = new List<ForgeTrialMount>();
        var readRootTaken = readRoot is null;
        var workspaceReadsWorkspace = Confirmed(session) is not { Count: > 0 };
        foreach (var folder in folders)
        {
            string physical;
            if (!folder.IsInput)
            {
                physical = SessionFolder(session, folder);
            }
            else if (folder.Directory is { Length: > 0 } bound)
            {
                physical = bound;
            }
            else if (!readRootTaken)
            {
                physical = readRoot!;
                readRootTaken = true;
            }
            else if (workspaceReadsWorkspace
                     && string.Equals(folder.Path, RunnerVirtualRoots.Workspace, StringComparison.Ordinal))
            {
                physical = workspace;
            }
            else
            {
                physical = SessionFolder(session, folder);
            }

            if (physical.StartsWith(session.Directory, StringComparison.Ordinal))
                System.IO.Directory.CreateDirectory(physical);
            mounts.Add(new ForgeTrialMount(folder, physical));
        }

        return mounts;
    }

    /// <summary>The session's own directory for <paramref name="folder"/>: <c>folders/&lt;name&gt;</c>.</summary>
    public static string SessionFolder(ForgeSession session, ForgeFolder folder)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(folder);
        return System.IO.Path.Combine(session.Directory, SessionDirectoryName, folder.Name);
    }

    /// <summary>
    /// The confirmed folder <paramref name="deliverable"/> lands in, or null when its first
    /// segment is no confirmed output — what the validation refuses.
    /// </summary>
    public static ForgeFolder? OutputOf(string? deliverable, IReadOnlyList<ForgeFolder> folders)
    {
        ArgumentNullException.ThrowIfNull(folders);
        if (deliverable is not { Length: > 1 } value || value[0] != '/')
            return null;

        var slash = value.IndexOf('/', 1);
        var root = slash > 1 ? value[..slash] : value;
        return folders.FirstOrDefault(f => !f.IsInput && string.Equals(f.Path, root, StringComparison.Ordinal));
    }
}
