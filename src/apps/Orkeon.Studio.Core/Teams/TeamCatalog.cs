using Orkeon.Constants.FileSystem;
using System.Text.Json;
using System.Text.Json.Serialization;
using Orkeon.Compliance.Vfs;

namespace Orkeon.Studio.Core.Teams;

/// <summary>
/// Studio's sidecar metadata for one adopted team — what the crew definition itself cannot
/// say: the human description, the model profile the team runs on, and the displayed
/// schedule. Written at adoption next to the promoted files; a team folder without it (one
/// imported or built by hand) is still a team, just a quieter card.
/// </summary>
public sealed record StudioTeamMetadata
{
    /// <summary>File name of the sidecar inside the team folder.</summary>
    public const string FileName = "studio-team.json";

    /// <summary>Display name; the folder name when absent.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>The need, in the user's words.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>Name of the model profile this team runs on.</summary>
    [JsonPropertyName("profile")]
    public string? Profile { get; init; }

    /// <summary>The engine schedule (<c>daily@HH:mm</c> / <c>hourly</c>), or null for on demand.</summary>
    [JsonPropertyName("schedule")]
    public string? Schedule { get; init; }

    /// <summary>
    /// The folders this team may see, as mount strings (<c>physical:virtual:rights</c>).
    /// A Studio-side concept, like <see cref="Profile"/>: Studio lays them on its launches
    /// as <c>--mount</c> arguments; a bare <c>orkeon run</c> in a terminal does not read them.
    /// <para>
    /// Raw, as written in the file. A physical segment starting with <c>./</c> is relative to
    /// the team folder (<see cref="TeamMountPaths"/>): <c>./output:/output:rw</c> is the
    /// team's own <c>output/</c>, wherever the folder is carried. The catalog resolves these
    /// to absolute paths when it describes a team; nothing outside it has to.
    /// </para>
    /// </summary>
    [JsonPropertyName("mounts")]
    public IReadOnlyList<string>? Mounts { get; init; }
}

/// <summary>What a launch screen shows about a target — sidecar-backed, best-effort.</summary>
public sealed record TargetDescription
{
    /// <summary>Display name: the sidecar's, else the file-system name; empty when unknown.</summary>
    public string? Name { get; init; }

    /// <summary>The sidecar's need, whole, when present.</summary>
    public string? Description { get; init; }

    /// <summary>
    /// The need cut to one paragraph for a card (STUDIO-16): derived from
    /// <see cref="Description"/> at read time through <see cref="TeamCatalog.Summarize"/>,
    /// never stored.
    /// </summary>
    public string? Summary => Description is null ? null : TeamCatalog.Summarize(Description);

    /// <summary>The model profile recorded by adoption, when present.</summary>
    public string? Profile { get; init; }

    /// <summary>Agent definitions counted in a multi-file team directory; null when unknown.</summary>
    public int? AgentCount { get; init; }

    /// <summary>
    /// The team's mount strings, resolved: a team-relative sidecar entry comes back bound
    /// under the team folder, absolute and ready to ride a launch as <c>--mount</c>; an entry
    /// naming a settings declaration by id comes back as that declaration. Empty for
    /// anything that is not an adopted team.
    /// </summary>
    public IReadOnlyList<string> Mounts { get; init; } = [];

    /// <summary>The sidecar's mounts read against the settings (VFS-90); resolved as copies when none were passed.</summary>
    public IReadOnlyList<ResolvedTeamMount> ResolvedMounts { get; init; } = [];

    /// <summary>The ids the sidecar names that this machine does not declare.</summary>
    public IReadOnlyList<string> UnknownMountIds() =>
        ResolvedMounts.Where(m => m.Source == TeamMountSource.UnknownId && m.Id is not null).Select(m => m.Id!.ToString()).Distinct().ToList();

    /// <summary>Whether the team refers to a declaration missing on this machine (D-06).</summary>
    public bool HasUnknownMountIds => ResolvedMounts.Any(m => m.Source == TeamMountSource.UnknownId);
}


/// <summary>One team folder, as the my-teams screen lists it.</summary>
public sealed record TeamSummary
{
    /// <summary>Display name.</summary>
    public required string Name { get; init; }

    /// <summary>Folder name — the identity on disk.</summary>
    public required string Slug { get; init; }

    /// <summary>Absolute path of the team folder — what <c>orkeon run</c> receives.</summary>
    public required string Path { get; init; }

    /// <summary>The sidecar, when the folder has one.</summary>
    public StudioTeamMetadata? Metadata { get; init; }

    /// <summary>True when the folder carries Studio's sidecar (adopted through the wizard).</summary>
    public bool HasMetadata => Metadata is not null;

    /// <summary>The engine schedule, or null for on demand.</summary>
    public string? Schedule => Metadata?.Schedule;

    /// <summary>Name of the team's model profile, when one was chosen.</summary>
    public string? Profile => Metadata?.Profile;

    /// <summary>The need, in the user's words, when recorded.</summary>
    public string? Description => Metadata?.Description;

    /// <summary>
    /// The need cut to one paragraph for the card (STUDIO-16): derived from
    /// <see cref="Description"/> at read time through <see cref="TeamCatalog.Summarize"/>,
    /// never stored — the sidecar keeps the whole need, it is the archive of it.
    /// </summary>
    public string? Summary => Description is null ? null : TeamCatalog.Summarize(Description);

    /// <summary>
    /// The team's mount strings, resolved: a team-relative sidecar entry (<c>./output</c>)
    /// comes back bound under <see cref="Path"/>, absolute, so the cards, the launcher and
    /// the folders modal keep receiving what they always did. The raw entries stay in
    /// <see cref="Metadata"/>. An entry naming a settings declaration by id comes back as that
    /// declaration (VFS-90). Empty when none are recorded.
    /// </summary>
    public IReadOnlyList<string> Mounts { get; init; } = [];

    /// <summary>The sidecar's mounts read against the settings (VFS-90); resolved as copies when none were passed.</summary>
    public IReadOnlyList<ResolvedTeamMount> ResolvedMounts { get; init; } = [];

    /// <summary>The ids the sidecar names that this machine does not declare.</summary>
    public IReadOnlyList<string> UnknownMountIds() =>
        ResolvedMounts.Where(m => m.Source == TeamMountSource.UnknownId && m.Id is not null).Select(m => m.Id!.ToString()).Distinct().ToList();

    /// <summary>Whether the team refers to a declaration missing on this machine (D-06).</summary>
    public bool HasUnknownMountIds => ResolvedMounts.Any(m => m.Source == TeamMountSource.UnknownId);

    /// <summary>Agent definitions counted on disk; null when the folder shows none.</summary>
    public int? AgentCount { get; init; }

    /// <summary>
    /// Whether the folder holds a YAML crew under <c>crew/</c> (<c>config.yaml</c> or
    /// <c>crew.yaml</c>, no <c>crew.ork.ts</c>) — what <c>forge reopen</c> can read back into a
    /// plan (FORGE-09). « Modify » stays possible on such a team even when no session points at
    /// it; a script crew or a foreign layout cannot be reopened.
    /// </summary>
    public bool HasYamlCrew { get; init; }
}

/// <summary>
/// The teams directory: every adopted team is an ordinary folder under one root —
/// copiable, shareable, deletable, runnable with <c>orkeon run &lt;folder&gt;</c> alone.
/// All I/O is tolerant: an unreadable folder or sidecar degrades to a plain entry or to
/// its absence, never to a crash.
/// </summary>
[SuppressVfsCompliance(
    "EXCEPTION-BOOTSTRAP: Studio is a host application; the teams directory is user-owned " +
    "storage on the physical disk, addressed before any VFS mount exists.")]
public static partial class TeamCatalog
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>
    /// The default root: <c>~/Orkeon/teams</c> — the user-profile home the design names,
    /// not the config directory: teams are documents, not preferences.
    /// </summary>
    public static string DefaultRoot() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.Create),
            "Orkeon", "teams");

    /// <summary>
    /// Lists the team folders under <paramref name="root"/>, sidecars read when present.
    /// </summary>
    /// <param name="root">The teams directory.</param>
    /// <param name="declaredMounts">The settings' mounts, to read each sidecar against (VFS-90); null not to consult them.</param>
    public static IReadOnlyList<TeamSummary> List(string root, IReadOnlyList<string>? declaredMounts = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        try
        {
            if (!Directory.Exists(root))
                return [];

            return Directory.EnumerateDirectories(root)
                .Order(StringComparer.OrdinalIgnoreCase)
                .Select(directory => Describe(directory, declaredMounts))
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Reads one team folder into its summary.</summary>
    /// <param name="teamDirectory">The team folder.</param>
    /// <param name="declaredMounts">
    /// The settings' mounts, to read the sidecar against (VFS-90): an entry naming one of them
    /// by id stands for that entry as it is today. Null not to consult them — the mounts are
    /// then the sidecar's own spelling, resolved under the team.
    /// </param>
    public static TeamSummary Describe(string teamDirectory, IReadOnlyList<string>? declaredMounts = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(teamDirectory);

        var slug = Path.GetFileName(Path.TrimEndingDirectorySeparator(teamDirectory));
        var metadata = TryReadMetadata(teamDirectory);
        // Resolved once, here, at the catalog's boundary: the runtime would resolve a
        // relative physical path against the process cwd, and no launcher should have
        // to know the sidecar's convention — nor which settings entry an id stands for.
        var resolved = TeamMountResolution.Resolve(teamDirectory, metadata?.Mounts, declaredMounts);

        return new TeamSummary
        {
            Name = metadata?.Name is { Length: > 0 } name ? name : slug,
            Slug = slug,
            Path = teamDirectory,
            Metadata = metadata,
            Mounts = resolved.Select(m => m.Effective).ToList(),
            ResolvedMounts = resolved,
            AgentCount = CountAgents(teamDirectory),
            HasYamlCrew = HasYamlCrew(teamDirectory),
        };
    }

    /// <summary>The crew layout <c>forge reopen</c> reads: a YAML settings file under <c>crew/</c>, no script.</summary>
    private static bool HasYamlCrew(string teamDirectory)
    {
        try
        {
            var crew = Path.Combine(teamDirectory, "crew");
            return !File.Exists(Path.Combine(crew, "crew.ork.ts"))
                && (File.Exists(Path.Combine(crew, ConventionalNames.CrewSettingsFile))
                    || File.Exists(Path.Combine(crew, ConventionalNames.CrewSettingsFallbackFile)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Counts agent definition files under <c>agents/</c> — and <c>crew/agents/</c>, the
    /// layout <c>forge promote</c> produces. Null when neither folder yields any.
    /// </summary>
    private static int? CountAgents(string teamDirectory)
    {
        try
        {
            var count = 0;
            foreach (var agentsDirectory in new[]
            {
                Path.Combine(teamDirectory, "agents"),
                Path.Combine(teamDirectory, "crew", "agents"),
            })
            {
                if (!Directory.Exists(agentsDirectory))
                    continue;
                count += Directory.EnumerateFiles(agentsDirectory, "*.yaml").Count()
                       + Directory.EnumerateFiles(agentsDirectory, "*.yml").Count();
            }

            return count > 0 ? count : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Ensures <paramref name="path"/> exists and hands it back — the lazy creation of
    /// the Orkeon user home the first time a forge session needs a working directory
    /// (Process.Start refuses a non-existent one). A creation failure degrades to
    /// returning the path unchanged: the caller's launch then surfaces the real error.
    /// </summary>
    public static string EnsureDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            Directory.CreateDirectory(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // Tolerant by design, like every other I/O in this catalog.
        }

        return path;
    }

    /// <summary>
    /// Everything a launch screen can honestly say about a target without a crew parser:
    /// the sidecar's name/description/profile/mounts when one sits beside it, the
    /// file-system name otherwise, and — for a team directory — the agent count.
    /// </summary>
    /// <param name="targetPath">The path the launcher was pointed at: a team folder or a file inside one.</param>
    /// <param name="declaredMounts">The settings' mounts, to read the sidecar against (VFS-90); null not to consult them.</param>
    public static TargetDescription DescribeTarget(string targetPath, IReadOnlyList<string>? declaredMounts = null)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
            return new TargetDescription();

        try
        {
            var isDirectory = Directory.Exists(targetPath);
            var directory = isDirectory
                ? targetPath
                : Path.GetDirectoryName(Path.GetFullPath(targetPath));
            var metadata = directory is { Length: > 0 } ? TryReadMetadata(directory) : null;

            var agentCount = isDirectory ? CountAgents(targetPath) : null;

            var fallbackName = isDirectory
                ? Path.GetFileName(targetPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                : Path.GetFileNameWithoutExtension(targetPath);

            var resolved = directory is { Length: > 0 }
                ? TeamMountResolution.Resolve(directory, metadata?.Mounts, declaredMounts)
                : [];

            return new TargetDescription
            {
                Name = metadata?.Name is { Length: > 0 } name ? name : fallbackName,
                Description = metadata?.Description,
                Profile = metadata?.Profile,
                AgentCount = agentCount,
                Mounts = resolved.Select(m => m.Effective).ToList(),
                ResolvedMounts = resolved,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new TargetDescription();
        }
    }

    /// <summary>
    /// Name of the model profile a launch target runs on, from the team sidecar next to it —
    /// the target's own folder, or its parent when the target is a definition file. Null for
    /// anything that is not an adopted team, which is most launches.
    /// </summary>
    public static string? ProfileFor(string targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
            return null;

        try
        {
            var directory = Directory.Exists(targetPath)
                ? targetPath
                : Path.GetDirectoryName(Path.GetFullPath(targetPath));
            return directory is { Length: > 0 } ? TryReadMetadata(directory)?.Profile : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Writes the sidecar; a failed write is silently accepted (the team folder itself is
    /// the value).
    /// <para>
    /// The one place the sidecar's folder convention is applied on the way in: a mount whose
    /// folder sits inside the team is written team-relative (<c>./output:/output:rw</c>),
    /// whatever spelling the caller handed over, and every team-relative folder is created —
    /// this is the single point where an in-team folder is materialised, at adoption and at
    /// every later edit alike. Idempotent: an <c>input/</c> that already holds documents is
    /// left as it is.
    /// </para>
    /// </summary>
    public static void SaveMetadata(string teamDirectory, StudioTeamMetadata metadata)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(teamDirectory);
        ArgumentNullException.ThrowIfNull(metadata);

        var relativized = Relativized(metadata, teamDirectory);
        try
        {
            Directory.CreateDirectory(teamDirectory);
            CreateTeamFolders(teamDirectory, relativized.Mounts);
            File.WriteAllText(
                Path.Combine(teamDirectory, StudioTeamMetadata.FileName),
                JsonSerializer.Serialize(relativized, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The promoted folder is the deliverable; losing the sidecar loses only comfort.
        }
    }

    /// <summary>
    /// The metadata with every mount under <paramref name="directory"/> rewritten
    /// team-relative. A null list stays null, so a team without folders keeps the sidecar
    /// it had.
    /// </summary>
    private static StudioTeamMetadata Relativized(StudioTeamMetadata metadata, string directory) =>
        metadata.Mounts is { Count: > 0 } mounts
            ? metadata with { Mounts = TeamMountPaths.RelativizeAll(directory, mounts) }
            : metadata;

    /// <summary>
    /// Creates the folder behind each team-relative entry — tolerant like the rest of this
    /// catalog: a folder the disk refuses is reported by the launch that needs it, with the
    /// real error, not here.
    /// </summary>
    private static void CreateTeamFolders(string teamDirectory, IReadOnlyList<string>? mounts)
    {
        if (mounts is null)
            return;

        foreach (var mount in mounts)
        {
            if (!TeamMountPaths.TryGetRelativeFolder(mount, out var folder))
                continue;

            try
            {
                Directory.CreateDirectory(Path.Combine(teamDirectory, folder));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                // Tolerant by design, like every other I/O in this catalog.
            }
        }
    }

    /// <summary>
    /// Records the team's mount strings in the sidecar, preserving everything else it says.
    /// A folder without a sidecar gains a minimal one — the mounts are worth remembering
    /// even for a hand-built team.
    /// </summary>
    public static void SaveMounts(string teamDirectory, IReadOnlyList<string> mounts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(teamDirectory);
        ArgumentNullException.ThrowIfNull(mounts);

        var metadata = TryReadMetadata(teamDirectory) ?? new StudioTeamMetadata();
        SaveMetadata(teamDirectory, metadata with { Mounts = mounts.Count > 0 ? mounts : null });
    }

    /// <summary>Deletes a team folder, recursively. Returns false when the disk refused.</summary>
    public static bool Delete(string teamDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(teamDirectory);

        try
        {
            Directory.Delete(teamDirectory, recursive: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Copies a team folder next to itself under a unique "-copy" slug. Returns the new
    /// path, or null when the disk refused.
    /// </summary>
    public static string? Duplicate(string teamDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(teamDirectory);

        try
        {
            var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(teamDirectory));
            var slug = Path.GetFileName(Path.TrimEndingDirectorySeparator(teamDirectory));
            if (parent is null || slug.Length == 0)
                return null;

            var destination = Path.Combine(parent, slug + "-copy");
            for (var i = 2; Directory.Exists(destination); i++)
                destination = Path.Combine(parent, $"{slug}-copy-{i}");

            CopyTree(teamDirectory, destination);

            // A verbatim sidecar would show two cards under the same display name — and in
            // novice mode the slug that tells them apart is hidden. The copy names itself.
            // Its folders travel with it: a team-relative entry is copied verbatim and resolves
            // under the copy; an absolute entry under the SOURCE (an older sidecar) is rewritten
            // relative on the way, so the copy writes into its own folder, never the original's.
            if (TryReadMetadata(destination) is { } metadata)
            {
                var copySlug = Path.GetFileName(destination);
                SaveMetadata(destination, Relativized(metadata, teamDirectory) with
                {
                    Name = metadata.Name is { Length: > 0 } name ? $"{name} ({copySlug[(slug.Length + 1)..]})" : copySlug,
                });
            }

            return destination;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Copies a team folder under <paramref name="destinationParent"/> for sharing. The
    /// destination keeps the slug and must not already exist (the promote-time rule: never
    /// merge into what is already there). The root <c>appsettings.json</c> is left behind —
    /// a resolved settings copy can carry provider endpoints the recipient should not
    /// inherit, and never travels. Returns the destination, or null when the disk refused.
    /// </summary>
    public static string? ExportTo(string teamDirectory, string destinationParent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(teamDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationParent);

        try
        {
            var slug = Path.GetFileName(Path.TrimEndingDirectorySeparator(teamDirectory));
            if (slug.Length == 0)
                return null;

            var destination = Path.Combine(destinationParent, slug);
            if (Directory.Exists(destination) || File.Exists(destination))
                return null;

            Directory.CreateDirectory(destination);
            foreach (var file in Directory.EnumerateFiles(teamDirectory))
            {
                if (string.Equals(Path.GetFileName(file), ConventionalNames.SettingsFile, StringComparison.OrdinalIgnoreCase))
                    continue;
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
            }

            foreach (var directory in Directory.EnumerateDirectories(teamDirectory))
            {
                if (File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint))
                    continue;
                CopyTree(directory, Path.Combine(destination, Path.GetFileName(Path.TrimEndingDirectorySeparator(directory))));
            }

            // Same rule as a duplicate: the exported folder works on another machine because
            // its own folders are recorded relative to it.
            if (TryReadMetadata(destination) is { } exported)
                SaveMetadata(destination, Relativized(exported, teamDirectory));

            return destination;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Copies an external team (a folder, or a single crew file) into the teams root under
    /// a unique slug. Returns the new team folder, or null when nothing was copied —
    /// <paramref name="refusal"/> then says why when the source itself was the reason: a
    /// folder the launcher's own detector cannot resolve to a crew definition is refused
    /// before a byte is copied, with the detector's message, instead of landing in the
    /// catalogue as a team nothing can run (STUDIO-12 C1). A disk that refused leaves
    /// <paramref name="refusal"/> null, as before.
    /// </summary>
    public static string? Import(string sourcePath, string root, out string? refusal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        // STUDIO-12 C1 — the source must be something orkeon run accepts, read with the same
        // detector the launcher uses, before anything is copied.
        var detection = new Targets.RunTargetDetector().Detect(sourcePath);
        if (detection.Status != Targets.RunTargetDetectionStatus.Resolved)
        {
            refusal = detection.Error
                ?? $"'{sourcePath}' holds several crew definitions ({string.Join(", ", detection.Candidates)}): pick one of them.";
            return null;
        }
        refusal = null;

        try
        {
            var isDirectory = Directory.Exists(sourcePath);
            if (!isDirectory && !File.Exists(sourcePath))
                return null;

            var name = isDirectory
                ? Path.GetFileName(Path.TrimEndingDirectorySeparator(sourcePath))
                : Path.GetFileNameWithoutExtension(sourcePath);
            var destination = Path.Combine(root, Slugify(name));
            for (var i = 2; Directory.Exists(destination); i++)
                destination = Path.Combine(root, $"{Slugify(name)}-{i}");

            if (isDirectory)
            {
                // Importing an ancestor of the teams root would copy the destination into
                // itself while it fills — a tree that only ends in an I/O error.
                var fullSource = Path.GetFullPath(sourcePath);
                var fullDestination = Path.GetFullPath(destination);
                if (Orkeon.Domain.FileSystem.PhysicalPathContainment.IsUnder(fullDestination, fullSource))
                {
                    return null;
                }

                CopyTree(sourcePath, destination);

                // An older sidecar carrying absolute paths under its source folder is rewritten
                // relative on import — a copy is a safeguard, not a compatibility layer.
                if (TryReadMetadata(destination) is { } imported)
                    SaveMetadata(destination, WithNormalizedName(Relativized(imported, sourcePath)));
            }
            else
            {
                Directory.CreateDirectory(destination);
                File.Copy(sourcePath, Path.Combine(destination, Path.GetFileName(sourcePath)));
            }

            return destination;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Scans an import candidate for inline secrets — an API key pasted into a definition
    /// travels with the folder, which is exactly what the environment-variable rule exists
    /// to prevent. Returns the offending files, relative to <paramref name="sourcePath"/>;
    /// values that reference the environment (<c>${…}</c>, <c>ORKEON_…</c>) are fine.
    /// </summary>
    public static IReadOnlyList<string> FindInlineSecrets(string sourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        try
        {
            // Relative to the folder for a directory candidate; a single-file candidate names
            // itself (a path relative to itself would render as ".").
            var baseDirectory = Directory.Exists(sourcePath)
                ? sourcePath
                : Path.GetDirectoryName(Path.GetFullPath(sourcePath)) ?? sourcePath;

            return ScannableFiles(sourcePath)
                .Where(file => HasInlineSecret(File.ReadAllText(file)))
                .Select(file => Path.GetRelativePath(baseDirectory, file))
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// The files a secret scan reads: every definition-shaped file under a folder candidate,
    /// or the single file candidate itself.
    /// </summary>
    private static IEnumerable<string> ScannableFiles(string sourcePath)
    {
        if (Directory.Exists(sourcePath))
        {
            return Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories)
                .Where(f => ScannedExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)
                            || f.EndsWith(".ork.ts", StringComparison.OrdinalIgnoreCase));
        }

        return File.Exists(sourcePath) ? [sourcePath] : [];
    }

    private static readonly string[] ScannedExtensions = [".yaml", ".yml", ".json", ".ts", ".js"];

    private static bool HasInlineSecret(string content)
    {
        foreach (System.Text.RegularExpressions.Match match in SecretPattern().Matches(content))
        {
            var value = match.Groups["value"].Value;
            if (!value.StartsWith("${", StringComparison.Ordinal)
                && !value.StartsWith('%')
                && !value.StartsWith("ORKEON_", StringComparison.Ordinal)
                && !value.StartsWith("env:", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    // The value may be quoted or bare — idiomatic YAML writes `api_key: sk-…` without quotes,
    // and an unquoted paste is exactly as leaked as a quoted one.
    [System.Text.RegularExpressions.GeneratedRegex(
        """(?i)(api[_-]?key|secret|token)["']?\s*[:=]\s*["']?(?<value>[^"'\s]{8,})["']?""")]
    private static partial System.Text.RegularExpressions.Regex SecretPattern();

    /// <summary>The slug a team name becomes on disk: lowercase ASCII, dashes between words.</summary>
    public static string Slugify(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var normalized = name.Normalize(System.Text.NormalizationForm.FormD);
        var builder = new System.Text.StringBuilder(normalized.Length);
        var lastWasDash = true;
        foreach (var ch in normalized)
        {
            var category = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == System.Globalization.UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsAsciiLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
                lastWasDash = false;
            }
            else if (!lastWasDash)
            {
                builder.Append('-');
                lastWasDash = true;
            }
        }

        var slug = builder.ToString().Trim('-');

        // Hard cap (64): a slug is a folder name, and Windows' MAX_PATH is a shared
        // budget — a goal-length sentence must never become a 200-character directory.
        // Cut at the last dash inside the window when one is reasonably close.
        if (slug.Length > MaxSlugLength)
        {
            var cut = slug.LastIndexOf('-', MaxSlugLength);
            slug = slug[..(cut >= MaxSlugLength / 2 ? cut : MaxSlugLength)].Trim('-');
        }

        return slug.Length > 0 ? slug : "equipe";
    }

    /// <summary>Longest slug <see cref="Slugify"/> produces.</summary>
    public const int MaxSlugLength = 64;

    #region STUDIO-16 — display normalisation

    /// <summary>Longest display name <see cref="NormalizeName"/> produces — the slug's own cap.</summary>
    public const int MaxNameLength = MaxSlugLength;

    /// <summary>Longest summary <see cref="Summarize"/> produces, ellipsis included.</summary>
    public const int MaxSummaryLength = 240;

    /// <summary>
    /// The one-line display name a free text becomes (STUDIO-16, D-04): its first line that
    /// says something, Markdown markup stripped, cut at a word boundary to
    /// <see cref="MaxNameLength"/> — the slug's cap — and never empty: a text that strips to
    /// nothing falls back on its slug. Applied at write (adoption, import) and at display
    /// (cards, headline, history) alike, because a WPF TextBlock renders line breaks even
    /// without wrapping and has no MaxLines — a pasted page used to become a forty-line title.
    /// </summary>
    public static string NormalizeName(string name)
    {
        if (TryNormalizeName(name, out var normalized))
            return normalized;

        return Slugify(string.IsNullOrWhiteSpace(name) ? "equipe" : name);
    }

    /// <summary>
    /// <see cref="NormalizeName"/> without the slug fallback: false, with an empty
    /// <paramref name="normalized"/>, when the text strips to nothing. The live form of a
    /// field being typed in, where an empty field has to stay empty.
    /// </summary>
    public static bool TryNormalizeName(string name, out string normalized)
    {
        ArgumentNullException.ThrowIfNull(name);

        var line = Lines(name).Select(StripMarkup).FirstOrDefault(l => l.Length > 0) ?? "";
        normalized = CutAtWord(line, MaxNameLength);
        return normalized.Length > 0;
    }

    /// <summary>
    /// The one-paragraph reading of a need (STUDIO-16, D-03): the first paragraph that says
    /// something once Markdown markup is stripped — fenced code, rules and a heading-only
    /// paragraph followed by prose are skipped, the heading standing in only when nothing
    /// else does — its lines joined, cut at a word boundary to <see cref="MaxSummaryLength"/>
    /// with an ellipsis. Derived at read time, never stored: the sidecar keeps the whole need,
    /// it is the archive of it. Empty for a text that says nothing.
    /// </summary>
    public static string Summarize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var scan = new SummaryScan();
        foreach (var raw in Lines(text))
        {
            var line = raw.Trim();
            var paragraph = scan.Read(line);
            if (paragraph is not null)
                return CutSummary(paragraph);
        }

        return CutSummary(scan.Close());
    }

    /// <summary>
    /// The reading state of <see cref="Summarize"/>: the lines of the paragraph being read, a
    /// heading remembered as the fallback rather than returned (prose wins over a title), and
    /// whether the cursor is inside a fenced block.
    /// </summary>
    private sealed class SummaryScan
    {
        private readonly List<string> _lines = [];
        private string? _heading;
        private bool _inFence;

        /// <summary>Reads one trimmed line; the paragraph it completes, when it completes one.</summary>
        public string? Read(string line)
        {
            if (line.StartsWith("```", StringComparison.Ordinal) || line.StartsWith("~~~", StringComparison.Ordinal))
            {
                var beforeFence = Flush(asHeading: false);
                _inFence = !_inFence;
                return beforeFence;
            }

            if (_inFence)
                return null;

            if (line.Length == 0 || RulePattern().IsMatch(line))
                return Flush(asHeading: false);

            if (line.StartsWith('#'))
            {
                // A heading is a block of its own, blank line after it or not.
                var paragraph = Flush(asHeading: false);
                if (paragraph is not null)
                    return paragraph;
                _lines.Add(line);
                Flush(asHeading: true);
                return null;
            }

            _lines.Add(line);
            return null;
        }

        /// <summary>The end of the text: the pending paragraph, else the remembered heading, else nothing.</summary>
        public string Close() => Flush(asHeading: false) ?? _heading ?? "";

        // Joins the pending lines into one paragraph; a heading is remembered as the fallback
        // rather than returned, prose wins over a title.
        private string? Flush(bool asHeading)
        {
            if (_lines.Count == 0)
                return null;

            var joined = string.Join(' ', _lines.Select(StripMarkup).Where(l => l.Length > 0));
            _lines.Clear();
            if (joined.Length == 0)
                return null;
            if (!asHeading)
                return joined;

            _heading ??= joined;
            return null;
        }
    }

    /// <summary>
    /// The sidecar with its name normalized (D-04) — applied where a sidecar is rewritten
    /// anyway (import), never in place: a sidecar already in the teams root is the user's,
    /// and the bounded display is enough for it.
    /// </summary>
    private static StudioTeamMetadata WithNormalizedName(StudioTeamMetadata metadata) =>
        metadata.Name is { Length: > 0 } name ? metadata with { Name = NormalizeName(name) } : metadata;

    private static string CutSummary(string text) =>
        text.Length <= MaxSummaryLength ? text : CutAtWord(text, MaxSummaryLength - 1) + "…";

    /// <summary>
    /// Cuts at the last space inside the window when one is reasonably close, the way the
    /// slug does; a single window-length word is cut hard. Trailing punctuation left by the
    /// cut goes with it — never a trailing comma.
    /// </summary>
    private static string CutAtWord(string text, int maxLength)
    {
        if (text.Length <= maxLength)
            return text;

        var cut = text.LastIndexOf(' ', maxLength);
        return text[..(cut >= maxLength / 2 ? cut : maxLength)].TrimEnd(',', ';', ':', '.', ' ');
    }

    private static string[] Lines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');

    /// <summary>
    /// One line without its Markdown: heading, quote and list markers, images and links
    /// reduced to their text, HTML tags, backticks and emphasis stars dropped, emphasis
    /// underscores unwrapped (a snake_case word keeps its own), whitespace runs collapsed.
    /// </summary>
    private static string StripMarkup(string line)
    {
        var text = LeadingMarkerPattern().Replace(line.Trim(), "");
        text = ImagePattern().Replace(text, "$1");
        text = LinkPattern().Replace(text, "$1");
        text = HtmlTagPattern().Replace(text, "");
        text = text.Replace("`", "", StringComparison.Ordinal).Replace("*", "", StringComparison.Ordinal);
        text = UnderscoreEmphasisPattern().Replace(text, "$1");
        return WhitespaceRunPattern().Replace(text, " ").Trim();
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^(?:#{1,6}\s*|>\s*|[-+]\s+|\d+[.)]\s+)+")]
    private static partial System.Text.RegularExpressions.Regex LeadingMarkerPattern();

    [System.Text.RegularExpressions.GeneratedRegex(@"!\[([^\]]*)\]\([^)]*\)")]
    private static partial System.Text.RegularExpressions.Regex ImagePattern();

    [System.Text.RegularExpressions.GeneratedRegex(@"\[([^\]]*)\]\([^)]*\)")]
    private static partial System.Text.RegularExpressions.Regex LinkPattern();

    [System.Text.RegularExpressions.GeneratedRegex(@"<[A-Za-z/!][^>]*>")]
    private static partial System.Text.RegularExpressions.Regex HtmlTagPattern();

    [System.Text.RegularExpressions.GeneratedRegex(@"(?<![\p{L}\p{N}])_{1,2}(\S(?:.*?\S)?)_{1,2}(?![\p{L}\p{N}])")]
    private static partial System.Text.RegularExpressions.Regex UnderscoreEmphasisPattern();

    [System.Text.RegularExpressions.GeneratedRegex(@"\s+")]
    private static partial System.Text.RegularExpressions.Regex WhitespaceRunPattern();

    /// <summary>A thematic break or a setext underline: three or more of one rule character.</summary>
    [System.Text.RegularExpressions.GeneratedRegex(@"^(?:-\s*){3,}$|^(?:\*\s*){3,}$|^(?:_\s*){3,}$|^={3,}$")]
    private static partial System.Text.RegularExpressions.Regex RulePattern();

    #endregion

    /// <summary>
    /// One canonical spelling for a path used as a dictionary key (matching a history
    /// entry's target to a team folder): absolute, no trailing separator. Degrades to the
    /// input on an unparsable path — a stable key matters more than a pretty one.
    /// </summary>
    public static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "";

        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }

    private static StudioTeamMetadata? TryReadMetadata(string teamDirectory)
    {
        try
        {
            var path = Path.Combine(teamDirectory, StudioTeamMetadata.FileName);
            return File.Exists(path)
                ? JsonSerializer.Deserialize<StudioTeamMetadata>(File.ReadAllText(path))
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static void CopyTree(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            // A directory symlink is not followed: a link to an ancestor would recurse
            // until the path length gives out, and a copy should carry files, not aliases.
            if (File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint))
                continue;

            CopyTree(directory, Path.Combine(destination, Path.GetFileName(Path.TrimEndingDirectorySeparator(directory))));
        }
    }
}
