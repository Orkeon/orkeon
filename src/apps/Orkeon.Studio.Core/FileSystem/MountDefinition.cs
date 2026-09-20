using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using Orkeon.Domain.Common;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Studio.Core.FileSystem;

/// <summary>Access-right override for a sub-path of a mount.</summary>
/// <param name="RelativePath">Path relative to the mount root.</param>
/// <param name="Rights">Rights granted on that sub-path.</param>
public sealed record SubPathRightsOverride(string RelativePath, MountRights Rights);

/// <summary>
/// One entry of <c>Orkeon:FileSystem:Mounts</c>, edited as fields rather than as the
/// Docker-style string. Serialization produces
/// <c>&lt;physical&gt;:&lt;virtual&gt;:&lt;rights&gt;[;&lt;subpath&gt;:&lt;rights&gt;]*</c>
/// and parsing goes through <see cref="FileSystemMount.Parse"/> itself, so what the
/// editor accepts and what the runtime accepts cannot drift apart.
/// </summary>
public sealed record MountDefinition
{
    /// <summary>
    /// The entry's identity (VFS-90): the <c>&lt;ulid&gt;|</c> prefix of the mount string, what a
    /// team's sidecar and a crew's <c>mounts:</c> block name this entry by. Null for an entry
    /// declared without one; the editors assign one on save.
    /// </summary>
    public MountId? Id { get; init; }

    /// <summary>Physical directory on disk, picked from a folder browser.</summary>
    public required string PhysicalPath { get; init; }

    /// <summary>Virtual path the mount is exposed under — a name, always starting with <c>/</c>.</summary>
    public required string VirtualPath { get; init; }

    /// <summary>Default rights of the mount.</summary>
    public MountRights Rights { get; init; } = MountRights.ReadOnly;

    /// <summary>Optional per-sub-path rights overrides.</summary>
    public IReadOnlyList<SubPathRightsOverride> Overrides { get; init; } = [];

    /// <summary>The last six characters of <see cref="Id"/> — what a row shows; null without an id.</summary>
    public string? ShortId => Id?.ToString() is { } text ? text[^Math.Min(6, text.Length)..] : null;

    /// <summary>This entry under a fresh id (VFS-90): what the editors assign on save.</summary>
    public MountDefinition WithFreshId() => this with { Id = MountId.Create() };

    /// <summary>This entry without its id — the form a team-local (<c>./x</c>) entry keeps (D-07).</summary>
    public MountDefinition WithoutId() => this with { Id = null };

    /// <summary>
    /// The virtual root as the engine compares it: trimmed, trailing slash dropped, <c>/</c>
    /// kept as is. The mount-string grammar spells <c>/output</c> and <c>/output/</c> as one
    /// root, and Studio must agree with it.
    /// </summary>
    public static string NormalizeRoot(string virtualPath)
    {
        ArgumentNullException.ThrowIfNull(virtualPath);

        var trimmed = virtualPath.Trim();
        return trimmed.Length > 1 ? trimmed.TrimEnd('/') : trimmed;
    }

    /// <summary>
    /// Whether two entries declare the same thing, ids aside: the same folder (normalized,
    /// compared the way <see cref="PhysicalPathContainment"/> compares paths on this
    /// platform), the same root, the same rights and sub-path overrides. What an older
    /// sidecar's copy of a settings entry is matched by, and what the editors reuse rather
    /// than declare twice.
    /// </summary>
    public bool SameDeclaration(MountDefinition other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return string.Equals(NormalizeFolder(PhysicalPath), NormalizeFolder(other.PhysicalPath), PhysicalPathContainment.Comparison)
            && SameRootAs(other.VirtualPath)
            && Rights == other.Rights
            && Overrides.SequenceEqual(other.Overrides);
    }

    /// <summary>Whether both entries carry the same id.</summary>
    public bool SameIdentity(MountDefinition other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Id is not null && other.Id is not null && Id.Equals(other.Id);
    }

    /// <summary>Whether this entry claims <paramref name="virtualPath"/>, as the engine compares roots.</summary>
    public bool SameRootAs(string virtualPath) =>
        virtualPath is not null
        && string.Equals(NormalizeRoot(VirtualPath), NormalizeRoot(virtualPath), StringComparison.Ordinal);

    /// <summary>The folder less the trailing separator a picker or a hand edit may have left.</summary>
    public static string NormalizeFolder(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return path.Trim().TrimEnd('/', '\\');
    }

    /// <summary>
    /// Virtual paths the UIs offer as suggestions — and pre-fill a new mount row with.
    /// <para>
    /// None of them may be a root a command mounts for itself. The list used to open on
    /// <c>/workspace</c> and offer <c>/output</c>, which are two of the three roots
    /// <c>orkeon forge</c> claims for its trial bench, so a user who accepted what Studio
    /// proposed got a settings file the forge then refused at host build. The editor already
    /// holds that line for the runner's own roots through <see cref="IsValidVirtualPath"/>;
    /// a suggestion the engine goes on to refuse is worse than no suggestion.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> SuggestedVirtualPaths { get; } =
        ["/data", "/docs", "/tmp"];

    /// <summary>
    /// The virtual name a picked folder is offered under: the folder's own name, lowercased and
    /// rooted — or the first free entry of <see cref="SuggestedVirtualPaths"/> when that name is
    /// unusable (empty, reserved, refused by the domain rule) or already spent. One derivation
    /// for the settings' « Allow a folder » and the wizard's disk pick (STUDIO-19), so the two
    /// doors cannot name one folder two ways.
    /// </summary>
    /// <param name="physicalPath">The folder the OS dialog returned.</param>
    /// <param name="takenVirtualPaths">Virtual paths already spent — the settings entries, the team's.</param>
    public static string SuggestVirtualPath(string physicalPath, IEnumerable<string> takenVirtualPaths)
    {
        ArgumentNullException.ThrowIfNull(physicalPath);
        ArgumentNullException.ThrowIfNull(takenVirtualPaths);

        var taken = takenVirtualPaths.ToHashSet(StringComparer.Ordinal);
        // Either separator, whatever the platform: the domain parser reads a Windows path on
        // Linux too, and the name a mount is offered under must not depend on where it is read.
        var trimmed = physicalPath.Trim().TrimEnd('/', '\\');
        var name = trimmed[(trimmed.LastIndexOfAny(['/', '\\']) + 1)..];
#pragma warning disable CA1308 // virtual paths are lowercase by convention, not a normalization round-trip
        var candidate = "/" + (name is { Length: > 0 } ? name.ToLowerInvariant() : "docs");
#pragma warning restore CA1308
        if (IsValidVirtualPath(candidate) && !taken.Contains(candidate))
            return candidate;

        return SuggestedVirtualPaths.FirstOrDefault(s => !taken.Contains(s)) ?? SuggestedVirtualPaths[0];
    }

    /// <summary>
    /// Serializes to the mount string format the runtime parses. Each path segment goes through
    /// <see cref="FileSystemMount.Quote"/> — a folder holding a <c>:</c> or a <c>;</c> is legal on
    /// every OS the picker browses, and writing it bare produced a spec this type's own
    /// <see cref="Parse"/> then refused.
    /// </summary>
    public string ToMountString()
    {
        var builder = new StringBuilder();
        if (Id is not null)
            builder.Append(Id.ToString()).Append(FileSystemMount.IdSeparator);

        builder
            .Append(FileSystemMount.Quote(PhysicalPath))
            .Append(':')
            .Append(FileSystemMount.Quote(VirtualPath))
            .Append(':')
            .Append(MountRightsTokens.ToToken(Rights));

        foreach (var item in Overrides)
        {
            builder.Append(';')
                .Append(FileSystemMount.Quote(item.RelativePath))
                .Append(':')
                .Append(MountRightsTokens.ToToken(item.Rights));
        }

        return builder.ToString();
    }

    /// <summary>Parses a mount string through the domain parser.</summary>
    /// <exception cref="FormatException">The string is not a valid mount definition.</exception>
    public static MountDefinition Parse(string mountString) =>
        FromDomain(FileSystemMount.Parse(mountString));

    /// <summary>
    /// Parses a mount string, reporting the domain parser's own message instead of
    /// throwing — the form-validation path of the UIs.
    /// </summary>
    public static bool TryParse(
        string? mountString,
        [NotNullWhen(true)] out MountDefinition? definition,
        out string? error)
    {
        definition = null;
        error = null;

        if (string.IsNullOrWhiteSpace(mountString))
        {
            error = "The mount definition is empty.";
            return false;
        }

        try
        {
            definition = Parse(mountString);
            return true;
        }
        catch (FormatException ex)
        {
            error = ex.Message;
            return false;
        }
        catch (ArgumentException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Projects a parsed domain mount back onto the editable shape.</summary>
    public static MountDefinition FromDomain(FileSystemMount mount)
    {
        ArgumentNullException.ThrowIfNull(mount);

        if (!MountRightsTokens.TryFromFileAccessRights(mount.DefaultRights, out var rights))
        {
            throw new FormatException(string.Create(
                CultureInfo.InvariantCulture,
                $"Mount rights '{mount.DefaultRights}' have no token in the mount string format."));
        }

        var overrides = new List<SubPathRightsOverride>(mount.Overrides.Count);
        foreach (var item in mount.Overrides)
        {
            if (!MountRightsTokens.TryFromFileAccessRights(item.Rights, out var overrideRights))
            {
                throw new FormatException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Override rights '{item.Rights}' have no token in the mount string format."));
            }

            overrides.Add(new SubPathRightsOverride(item.RelativePath, overrideRights));
        }

        return new MountDefinition
        {
            Id = mount.Id,
            PhysicalPath = mount.BasePath,
            VirtualPath = mount.VirtualPath,
            Rights = rights,
            Overrides = overrides,
        };
    }

    /// <summary>Builds the domain mount this definition describes (a real round-trip through the parser).</summary>
    /// <exception cref="FormatException">The definition does not serialize to a parsable mount string.</exception>
    public FileSystemMount ToDomainMount() => FileSystemMount.Parse(ToMountString());

    /// <summary>
    /// True when the virtual path satisfies the domain rule (starts with <c>/</c>). Asks the
    /// domain type rather than restating the rule.
    /// </summary>
    public static bool IsValidVirtualPath(string? virtualPath)
    {
        if (string.IsNullOrWhiteSpace(virtualPath))
            return false;

        if (IsReservedVirtualPath(virtualPath))
            return false;

        try
        {
            _ = new FileSystemMount("/", virtualPath, FileAccessRights.ReadOnly);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// The virtual roots a runner mounts for itself (<c>/crew</c>, <c>/script</c>,
    /// <c>/llm-logs</c>, <c>/sandbox</c> - <see cref="Orkeon.Constants.FileSystem.RunnerVirtualRoots.All"/>, asked for as a
    /// set precisely so this list cannot fall behind it). A user mount claiming one is refused by
    /// the engine at launch, so
    /// the editor and the folder picker refuse it here rather than letting a folder happening
    /// to be named <c>crew</c> produce a team that will not start.
    /// </summary>
    public static bool IsReservedVirtualPath(string? virtualPath) =>
        virtualPath is not null
        && Launch.MountAutoInjection.ReservedVirtualRoots.Contains(
            virtualPath.TrimEnd('/'), StringComparer.Ordinal);
}
