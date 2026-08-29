using System.Text.Json;
using System.Text.Json.Serialization;
using Orkeon.Compliance.Vfs;
using Orkeon.Studio.Core.FileSystem;

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
    /// </summary>
    [JsonPropertyName("mounts")]
    public IReadOnlyList<string>? Mounts { get; init; }
}

/// <summary>What a launch screen shows about a target — sidecar-backed, best-effort.</summary>
public sealed record TargetDescription
{
    /// <summary>Display name: the sidecar's, else the file-system name; empty when unknown.</summary>
    public string? Name { get; init; }

    /// <summary>The sidecar's one-line need, when present.</summary>
    public string? Description { get; init; }

    /// <summary>The model profile recorded by adoption, when present.</summary>
    public string? Profile { get; init; }

    /// <summary>Agent definitions counted in a multi-file team directory; null when unknown.</summary>
    public int? AgentCount { get; init; }

    /// <summary>The team's sidecar mount strings; empty for anything that is not an adopted team.</summary>
    public IReadOnlyList<string> Mounts { get; init; } = [];
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

    /// <summary>The team's mount strings; empty when none are recorded.</summary>
    public IReadOnlyList<string> Mounts => Metadata?.Mounts ?? [];

    /// <summary>Agent definitions counted on disk; null when the folder shows none.</summary>
    public int? AgentCount { get; init; }
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

    /// <summary>Lists the team folders under <paramref name="root"/>, sidecars read when present.</summary>
    public static IReadOnlyList<TeamSummary> List(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        try
        {
            if (!Directory.Exists(root))
                return [];

            return Directory.EnumerateDirectories(root)
                .Order(StringComparer.OrdinalIgnoreCase)
                .Select(Describe)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Reads one team folder into its summary.</summary>
    public static TeamSummary Describe(string teamDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(teamDirectory);

        var slug = Path.GetFileName(Path.TrimEndingDirectorySeparator(teamDirectory));
        var metadata = TryReadMetadata(teamDirectory);

        return new TeamSummary
        {
            Name = metadata?.Name is { Length: > 0 } name ? name : slug,
            Slug = slug,
            Path = teamDirectory,
            Metadata = metadata,
            AgentCount = CountAgents(teamDirectory),
        };
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
    public static TargetDescription DescribeTarget(string targetPath)
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

            return new TargetDescription
            {
                Name = metadata?.Name is { Length: > 0 } name ? name : fallbackName,
                Description = metadata?.Description,
                Profile = metadata?.Profile,
                AgentCount = agentCount,
                Mounts = metadata?.Mounts ?? [],
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

    /// <summary>Writes the sidecar; a failed write is silently accepted (the team folder itself is the value).</summary>
    public static void SaveMetadata(string teamDirectory, StudioTeamMetadata metadata)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(teamDirectory);
        ArgumentNullException.ThrowIfNull(metadata);

        try
        {
            Directory.CreateDirectory(teamDirectory);
            File.WriteAllText(
                Path.Combine(teamDirectory, StudioTeamMetadata.FileName),
                JsonSerializer.Serialize(metadata, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The promoted folder is the deliverable; losing the sidecar loses only comfort.
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
            if (TryReadMetadata(destination) is { } metadata)
            {
                var copySlug = Path.GetFileName(destination);
                SaveMetadata(destination, RebaseMounts(metadata, teamDirectory, destination) with
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
                if (string.Equals(Path.GetFileName(file), "appsettings.json", StringComparison.OrdinalIgnoreCase))
                    continue;
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
            }

            foreach (var directory in Directory.EnumerateDirectories(teamDirectory))
            {
                if (File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint))
                    continue;
                CopyTree(directory, Path.Combine(destination, Path.GetFileName(Path.TrimEndingDirectorySeparator(directory))));
            }

            if (TryReadMetadata(destination) is { } exported)
                SaveMetadata(destination, RebaseMounts(exported, teamDirectory, destination));

            return destination;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Copies an external team (a folder, or a single crew file) into the teams root under
    /// a unique slug. Returns the new team folder, or null when the disk refused.
    /// </summary>
    public static string? Import(string sourcePath, string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

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

                if (TryReadMetadata(destination) is { } imported)
                    SaveMetadata(destination, RebaseMounts(imported, sourcePath, destination));
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
            var files = Directory.Exists(sourcePath)
                ? Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories)
                    .Where(f => ScannedExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)
                                || f.EndsWith(".ork.ts", StringComparison.OrdinalIgnoreCase))
                : File.Exists(sourcePath) ? [sourcePath] : [];

            // Relative to the folder for a directory candidate; a single-file candidate names
            // itself (a path relative to itself would render as ".").
            var baseDirectory = Directory.Exists(sourcePath)
                ? sourcePath
                : Path.GetDirectoryName(Path.GetFullPath(sourcePath)) ?? sourcePath;
            var offending = new List<string>();
            foreach (var file in files)
            {
                if (HasInlineSecret(File.ReadAllText(file)))
                    offending.Add(Path.GetRelativePath(baseDirectory, file));
            }

            return offending;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
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

    /// <summary>
    /// Moves the sidecar's own folders with the folder. A team's write roots are bound to
    /// directories INSIDE it (<c>&lt;team&gt;/output:/output:rw</c>, derived from the blueprint at
    /// adoption), so a copy that kept them verbatim would have the new team writing into the
    /// old one — or, once exported to another machine, pointing at a path that does not exist.
    /// Mounts outside the folder are the user's own choices and are left alone.
    /// </summary>
    private static StudioTeamMetadata RebaseMounts(
        StudioTeamMetadata metadata, string sourceDirectory, string destinationDirectory)
    {
        if (metadata.Mounts is not { Count: > 0 } mounts)
            return metadata;

        var source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceDirectory));
        var destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destinationDirectory));

        var rebased = new List<string>(mounts.Count);
        var changed = false;
        foreach (var mountString in mounts)
        {
            if (!MountDefinition.TryParse(mountString, out var mount, out _)
                || mount.PhysicalPath is not { Length: > 0 } physical)
            {
                rebased.Add(mountString);
                continue;
            }

            var full = Path.GetFullPath(physical);
            // Strictly under, via the one containment predicate: the local copy hardcoded a
            // per-OS comparison of its own and knew nothing of AltDirectorySeparatorChar.
            if (full.Length <= source.Length
                || !Orkeon.Domain.FileSystem.PhysicalPathContainment.IsUnder(full, source))
            {
                rebased.Add(mountString);
                continue;
            }

            rebased.Add((mount with
            {
                PhysicalPath = Path.Combine(destination, full[(source.Length + 1)..]),
            }).ToMountString());
            changed = true;
        }

        return changed ? metadata with { Mounts = rebased } : metadata;
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
