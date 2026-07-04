namespace Orkeon.Domain.FileSystem;

/// <summary>Maps a physical directory to a virtual path with access rights.</summary>
public sealed record FileSystemMount
{
    /// <summary>Physical base path on disk.</summary>
    public string BasePath { get; }
    /// <summary>Virtual path prefix used to reference this mount.</summary>
    public string VirtualPath { get; }
    /// <summary>Default access rights for the mount.</summary>
    public FileAccessRights DefaultRights { get; }
    /// <summary>Sub-path access right overrides, sorted by path length descending.</summary>
    public IReadOnlyList<SubPathOverride> Overrides { get; }
    /// <summary>Controls visibility in GetAvailableMounts(); Internal mounts are hidden from agents.</summary>
    public MountVisibility Visibility { get; }

    /// <summary>Creates a new file system mount with the specified parameters.</summary>
    public FileSystemMount(
        string basePath,
        string virtualPath,
        FileAccessRights defaultRights,
        IReadOnlyList<SubPathOverride>? overrides = null,
        MountVisibility visibility = MountVisibility.AgentFacing)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(basePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualPath);

        if (!IsValidVirtualPath(virtualPath))
            throw new FormatException(
                $"Virtual path must start with '/' (Unix) or be a Windows drive path: '{virtualPath}'");

        BasePath = basePath;
        VirtualPath = virtualPath;
        DefaultRights = defaultRights;
        Overrides = overrides is { Count: > 0 }
            ? overrides
                .OrderByDescending(o => o.RelativePath.Length)
                .ToList()
                .AsReadOnly()
            : Array.Empty<SubPathOverride>();
        Visibility = visibility;
    }

    /// <summary>Parses a mount definition string into a <see cref="FileSystemMount"/> instance.</summary>
    public static FileSystemMount Parse(string mountString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mountString);

        // Split into main part and override parts (separated by ';')
        var segments = mountString.Split(';');
        var mainPart = segments[0];

        // Split main part into basePath, virtualPath, rights
        var parts = SplitMainPart(mainPart);
        if (parts.Length != 3)
            throw new FormatException(
                $"Invalid mount format. Expected '<physical>:<virtual>:<rights>', got: '{mainPart}'");

        var basePath = parts[0];
        var virtualPath = parts[1];
        var defaultRights = ParseRights(parts[2]);

        if (!IsValidVirtualPath(virtualPath))
            throw new FormatException(
                $"Virtual path must start with '/' (Unix) or be a Windows drive path: '{virtualPath}'");

        // Parse overrides
        var overrides = new List<SubPathOverride>();
        for (var i = 1; i < segments.Length; i++)
        {
            var overrideParts = segments[i].Split(':');
            if (overrideParts.Length != 2)
                throw new FormatException(
                    $"Invalid override format. Expected '<subpath>:<rights>', got: '{segments[i]}'");

            overrides.Add(new SubPathOverride(overrideParts[0], ParseRights(overrideParts[1])));
        }

        return new FileSystemMount(basePath, virtualPath, defaultRights, overrides);
    }

    /// <summary>Resolves the effective access rights for a relative path within this mount.</summary>
    public FileAccessRights ResolveRights(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        // Normalize: replace backslash with forward slash, trim trailing slash
        var normalized = relativePath.Replace('\\', '/').TrimEnd('/');

        foreach (var ov in Overrides) // Already sorted by length descending
        {
            // Boundary check: exact match or starts with override + "/"
            if (string.Equals(normalized, ov.RelativePath, StringComparison.Ordinal) ||
                normalized.StartsWith(ov.RelativePath + "/", StringComparison.Ordinal))
            {
                return ov.Rights;
            }
        }

        return DefaultRights;
    }

    /// <summary>
    /// True for Unix-style virtual paths (<c>/foo</c>) and Windows drive-letter
    /// paths (<c>C:\foo</c> or <c>C:/foo</c>). The latter is required when
    /// physical and virtual paths are identity-mapped on Windows
    /// (e.g. RunnerExecution auto-injection of the runner's own config dir).
    /// </summary>
    private static bool IsValidVirtualPath(string virtualPath)
    {
        if (string.IsNullOrEmpty(virtualPath))
            return false;
        if (virtualPath.StartsWith('/'))
            return true;
        return virtualPath.Length >= 3
            && char.IsLetter(virtualPath[0])
            && virtualPath[1] == ':'
            && (virtualPath[2] == '\\' || virtualPath[2] == '/');
    }

    private static string[] SplitMainPart(string mainPart)
    {
        // Iteratively peel off path/rights segments. At each step, detect a
        // Windows drive-letter prefix ("X:\" or "X:/") so its embedded ':' is
        // NOT treated as a segment separator. This covers single-Windows-path
        // mounts ("C:\src:/virtual:ro") AND identity-mapped Windows mounts
        // where both physical and virtual paths carry drive letters
        // ("C:\src:C:\src:ro" — emitted by RunnerExecution auto-injection).
        var parts = new List<string>();
        var rest = mainPart;

        while (rest.Length > 0)
        {
            var isWindowsPath = rest.Length >= 3
                && char.IsLetter(rest[0])
                && rest[1] == ':'
                && (rest[2] == '\\' || rest[2] == '/');

            // For a Windows-path segment, look for the NEXT ':' starting at
            // index 2 (skipping the drive-letter colon). Otherwise, look from
            // the start.
            var searchFrom = isWindowsPath ? 2 : 0;
            var nextColon = rest.IndexOf(':', searchFrom);

            if (nextColon < 0)
            {
                parts.Add(rest);
                break;
            }

            parts.Add(rest[..nextColon]);
            rest = rest[(nextColon + 1)..];
        }

        return parts.ToArray();
    }

    private static FileAccessRights ParseRights(string rights)
    {
#pragma warning disable CA1308 // lowercase is the normalized form matched by the switch arms (rights tokens), not a comparison normalization
        return rights.Trim().ToLowerInvariant() switch
        {
#pragma warning restore CA1308
            "ro" => FileAccessRights.ReadOnly,
            "rw" => FileAccessRights.ReadWrite,
            "rwnd" => FileAccessRights.ReadWriteNoDelete,
            _ => throw new FormatException($"Unknown rights value: '{rights}'. Valid values: ro, rw, rwnd")
        };
    }
}
