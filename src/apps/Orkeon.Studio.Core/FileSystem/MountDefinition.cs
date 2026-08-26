using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
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
    /// <summary>Physical directory on disk, picked from a folder browser.</summary>
    public required string PhysicalPath { get; init; }

    /// <summary>Virtual path the mount is exposed under — a name, always starting with <c>/</c>.</summary>
    public required string VirtualPath { get; init; }

    /// <summary>Default rights of the mount.</summary>
    public MountRights Rights { get; init; } = MountRights.ReadOnly;

    /// <summary>Optional per-sub-path rights overrides.</summary>
    public IReadOnlyList<SubPathRightsOverride> Overrides { get; init; } = [];

    /// <summary>Virtual paths the UIs offer as suggestions.</summary>
    public static IReadOnlyList<string> SuggestedVirtualPaths { get; } =
        ["/workspace", "/output", "/tmp"];

    /// <summary>
    /// Serializes to the mount string format the runtime parses. Each path segment goes through
    /// <see cref="FileSystemMount.Quote"/> — a folder holding a <c>:</c> or a <c>;</c> is legal on
    /// every OS the picker browses, and writing it bare produced a spec this type's own
    /// <see cref="Parse"/> then refused.
    /// </summary>
    public string ToMountString()
    {
        var builder = new StringBuilder()
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
    /// <c>/llm-logs</c>). A user mount claiming one is refused by the engine at launch, so
    /// the editor and the folder picker refuse it here rather than letting a folder happening
    /// to be named <c>crew</c> produce a team that will not start.
    /// </summary>
    public static bool IsReservedVirtualPath(string? virtualPath) =>
        virtualPath is not null
        && Launch.MountAutoInjection.ReservedVirtualRoots.Contains(
            virtualPath.TrimEnd('/'), StringComparer.Ordinal);
}
