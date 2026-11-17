using System.Text.Json.Serialization;
using Orkeon.Constants.FileSystem;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Scripting.Cli.Commands.Forge;

/// <summary>
/// One folder of the team: a virtual root its agents address, whether they read
/// it or write to it, and why. The forge LLM extracts the list from the request into the
/// brief (<c>folders</c>); the user confirms it (<c>folders.confirmed</c>); from then on it is
/// the one list every consumer reads — the blueprint prompt, the deliverable check, the trial
/// bench's mounts, the promoted team's launcher and the team sidecar.
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

    /// <summary>
    /// Whether what the folder holds changes from run to run — an inbox the user refills — so
    /// that the files found in it at the fill step are examples of what will come, not the
    /// team's permanent material (#400). An input folder only. The interview sets it from the
    /// request, a client may set it in <c>folders.confirmed</c>; null is «not said», which a
    /// confirmation takes from the proposal (<see cref="ForgeFolders.WithProposedExamples"/>)
    /// and every reader takes as fixed.
    /// </summary>
    [JsonPropertyName("examples")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Examples { get; init; }

    /// <summary>
    /// What the user put in a folder kept inside the team, read when the fill step is answered
    /// (<c>folders.filled</c>): the files and sub-folders relative to the folder, a sub-folder
    /// ending with <c>/</c>, at most <see cref="ForgeFolders.MaxContents"/> of them. Set by the
    /// engine only; the plan prompt names them, so the tasks are designed for what is there —
    /// as examples the plan names none of, when the folder's contents change (<see cref="Examples"/>).
    /// </summary>
    [JsonPropertyName("contents")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Contents { get; init; }

    /// <summary>The files of <see cref="ForgeFolders.LargeFileBytes"/> or more, largest first; null when none. Set by the fill step.</summary>
    [JsonPropertyName("large")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ForgeLargeFile>? Large { get; init; }

    /// <summary>Every file under the folder, relative with <c>/</c> separators, uncapped up to 10 000; set by the fill step.</summary>
    [JsonIgnore]
    internal IReadOnlyList<string> Existing { get; init; } = [];

    /// <summary>The wire form of <see cref="Existing"/>: absent when empty.</summary>
    [JsonInclude]
    [JsonPropertyName("existing")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    internal IReadOnlyList<string>? ExistingOnWire
    {
        get => Existing.Count > 0 ? Existing : null;
        init => Existing = value ?? [];
    }

    /// <summary>Whether the team only reads this folder.</summary>
    [JsonIgnore]
    public bool IsInput => string.Equals(Role, ForgeFolders.InputRole, StringComparison.Ordinal);

    /// <summary>Whether the files found in the folder are examples: an input whose contents change (<see cref="Examples"/>).</summary>
    [JsonIgnore]
    public bool HoldsExamples => Examples == true && IsInput;

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

    /// <summary>
    /// Whether the fill step is settled: no folder is kept inside the team, or the user said
    /// <c>folders.filled</c>. False keeps a session stopped at that wait asking it on resume.
    /// </summary>
    [JsonPropertyName("filled")]
    public bool Filled { get; init; }
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
/// The rules of the folder list: what a valid list is, the defaults when the
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
    /// no reserved root, a known role, no root twice, <c>examples</c> on an input only. An empty list is a valid one, and so is a
    /// list with no output: a team that sends mails from what it reads writes no
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

            if (folder.Examples == true && string.Equals(folder.Role, OutputRole, StringComparison.Ordinal))
                errors.Add($"{label}: 'examples' is for an input folder — leave it out of '{folder.Path}', which the team writes to.");
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
    /// when something comes in: a team given a URL or a text reads no folder), and
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
        if (string.Equals(role, InputRole, StringComparison.Ordinal))
            return french ? "Ce que l'équipe lit." : "What the team reads.";

        return french ? "Où l'équipe écrit ses résultats." : "Where the team writes its results.";
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
    /// trial: a folder that exists is one an explorer can open.
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

    /// <summary>
    /// A confirmed list with what it left unsaid about <see cref="ForgeFolder.Examples"/> taken
    /// from the proposal: an input folder the answer does not flag either way keeps the value
    /// the proposal gave the same path. A client that does not know the property thus keeps
    /// what the interview understood, and one that sends true or false decides (#400).
    /// </summary>
    public static IReadOnlyList<ForgeFolder> WithProposedExamples(
        IReadOnlyList<ForgeFolder> answer, IReadOnlyList<ForgeFolder> proposal)
    {
        ArgumentNullException.ThrowIfNull(answer);
        ArgumentNullException.ThrowIfNull(proposal);

        return [.. answer.Select(folder =>
            folder is { Examples: null, IsInput: true }
            && proposal.FirstOrDefault(p => p.IsInput && string.Equals(p.Path, folder.Path, StringComparison.Ordinal)) is { Examples: { } proposed }
                ? folder with { Examples = proposed }
                : folder)];
    }

    /// <summary>The folder as the brief and the prompts carry it: no physical path.</summary>
    public static ForgeFolder WithoutDirectory(ForgeFolder folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        return folder with { Directory = null };
    }

    /// <summary>The confirmed list of <paramref name="session"/>, or null before the confirmation.</summary>
    public static IReadOnlyList<ForgeFolder>? Confirmed(ForgeSession session) => ListOf(session)?.Folders;

    /// <summary>The kept list of <paramref name="session"/>, fill step included, or null before the confirmation.</summary>
    public static ForgeFolderList? ListOf(ForgeSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.TryLoadArtifact<ForgeFolderList>(FileName);
    }

    /// <summary>The most entries <see cref="Inventory"/> names; the rest is counted.</summary>
    public const int MaxContents = 40;

    /// <summary>
    /// What a held folder holds (<see cref="ForgeFolder.Contents"/>): every file and sub-folder
    /// under <paramref name="directory"/>, relative and with <c>/</c> separators, a sub-folder
    /// ending with <c>/</c>, in ordinal order; past <see cref="MaxContents"/> entries the last
    /// one says how many more there are. Empty when the folder is empty or gone.
    /// </summary>
    public static IReadOnlyList<string> Inventory(string directory)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        if (!System.IO.Directory.Exists(directory))
            return [];

        // A tree the user dropped may hold what this process cannot read: it is skipped, never a
        // failure of the forge, and a folder removed while it is listed lists what was read.
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 };
        var entries = new List<string>();
        try
        {
            foreach (var entry in System.IO.Directory.EnumerateFileSystemEntries(directory, "*", options))
            {
                var relative = System.IO.Path.GetRelativePath(directory, entry).Replace('\\', '/');
                entries.Add(System.IO.Directory.Exists(entry) ? relative + "/" : relative);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // what was read so far stands
        }

        entries.Sort(StringComparer.Ordinal);
        if (entries.Count <= MaxContents)
            return entries;

        var kept = entries.Take(MaxContents - 1).ToList();
        kept.Add($"... and {entries.Count - kept.Count} more");
        return kept;
    }

    /// <summary>Bytes from which a file is large (128 KiB).</summary>
    public const long LargeFileBytes = 128 * 1024;

    /// <summary>The most files <see cref="Survey(string)"/> lists in <see cref="ForgeFolder.Existing"/>; a bound tree may be huge.</summary>
    public const int MaxExisting = 10_000;

    /// <summary>
    /// The files under <paramref name="directory"/> of <see cref="LargeFileBytes"/> or more, by
    /// relative path with <c>/</c> separators, largest first, at most <see cref="MaxContents"/>.
    /// Empty when the folder is gone.
    /// </summary>
    public static IReadOnlyList<ForgeLargeFile> LargeFiles(string directory) => Survey(directory).Large;

    /// <summary>
    /// One walk of <paramref name="directory"/>: its large files (<see cref="LargeFiles"/>) and
    /// every file, relative with <c>/</c> separators, up to <see cref="MaxExisting"/>. An entry
    /// this process cannot read is skipped; the walk ends, keeping what it read, when it fails to
    /// advance twice in a row; both are empty when the folder is gone.
    /// </summary>
    public static (IReadOnlyList<ForgeLargeFile> Large, IReadOnlyList<string> Existing) Survey(string directory)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        if (!System.IO.Directory.Exists(directory))
            return ([], []);

        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 };
        return Survey(directory, () => new System.IO.DirectoryInfo(directory).EnumerateFiles("*", options).GetEnumerator());
    }

    /// <summary><see cref="Survey(string)"/> over a given file walk, for tests.</summary>
    internal static (IReadOnlyList<ForgeLargeFile> Large, IReadOnlyList<string> Existing) Survey(
        string directory, Func<IEnumerator<System.IO.FileInfo>> walk)
    {
        var found = new List<ForgeLargeFile>();
        var existing = new List<string>();
        using var files = walk();
        var failedMoves = 0;
        while (true)
        {
            bool moved;
            try
            {
                moved = files.MoveNext();
                failedMoves = 0;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // the walk could not advance: one failure skips the entry, a second in a row
                // (a persistent readdir error) ends it with what was read
                if (++failedMoves >= 2)
                    break;
                continue;
            }

            if (!moved)
                break;

            try
            {
                var file = files.Current;
                var relative = System.IO.Path.GetRelativePath(directory, file.FullName).Replace('\\', '/');
                var length = file.Length;
                if (existing.Count < MaxExisting)
                    existing.Add(relative);
                if (length >= LargeFileBytes)
                    found.Add(new ForgeLargeFile { Name = relative, Bytes = length });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // this entry is skipped; the walk goes on with the next one
            }
        }

        return ([.. found.OrderByDescending(f => f.Bytes).ThenBy(f => f.Name, StringComparer.Ordinal).Take(MaxContents)], existing);
    }

    /// <summary>Every file the folders already hold, as <c>&lt;folder&gt;/&lt;entry&gt;</c>.</summary>
    public static List<string> ExistingPaths(IEnumerable<ForgeFolder>? folders) =>
        [.. (folders ?? []).Where(static f => f.Path is not null)
            .SelectMany(static f => f.Existing.Select(entry => $"{f.Path!.TrimEnd('/')}/{entry}"))];

    /// <summary>Every file the session's filled brief records in its folders, as <c>&lt;folder&gt;/&lt;entry&gt;</c>.</summary>
    public static List<string> ExistingPaths(ForgeSession session) =>
        ExistingPaths(session.TryLoadArtifact<ForgeBrief>(ForgeSession.BriefFileName)?.Folders);

    /// <summary>
    /// The list a session's team uses: the confirmed one — empty included, when the user kept
    /// no folder at all; for a session that never confirmed one — rebuilt by
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
    /// team exists (the user drops files in it) and moved into it at the adoption.
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

/// <summary>A large file of a held folder: its relative path and its size in bytes.</summary>
internal sealed record ForgeLargeFile
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("bytes")]
    public long Bytes { get; init; }
}
