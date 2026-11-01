using System.Collections.Immutable;

namespace Orkeon.Analysis.Abstractions.Interfaces;

/// <summary>
/// Discovers the source files of a codebase eligible for indexing (discovery phase).
/// </summary>
public interface IFileSystemDiscoverer
{
    IAsyncEnumerable<DiscoveredFile> DiscoverAsync(DiscoveryRequest req, CancellationToken ct);
    Task<DiscoveryResult> DiscoverAllAsync(DiscoveryRequest req, CancellationToken ct);
}

public sealed record DiscoveredFile(
    string VirtualPath,
    string RelativePath,
    string Language,
    long SizeBytes,
    string Sha256)
{
    /// <summary>The last write time the file system reported when the file was discovered.</summary>
    public DateTimeOffset LastModified { get; init; }
}

/// <summary>
/// What is known of a file from an earlier discovery: its SHA-256, its size and its last write time. A null
/// <see cref="LastModified"/> means the time cannot be trusted (the file was written too close to the discovery that
/// hashed it), so the file is hashed again.
/// </summary>
public sealed record FileStamp(string Sha256, long SizeBytes, DateTimeOffset? LastModified);

public sealed record DiscoveryRequest
{
    public required string RootPath { get; init; }
    public ImmutableArray<string> Languages { get; init; } = [];
    public ImmutableArray<string> Exclude { get; init; } =
        ["node_modules", "dist", "build", ".git", "target", "bin", "obj"];

    /// <summary>
    /// When <see langword="true"/> (default), the ignore files under the virtual root filter the
    /// enumeration as git applies them: <c>.git/info/exclude</c>, the root <c>.gitignore</c> and each
    /// nested <c>.gitignore</c> for the paths under its folder. When <see langword="false"/>, they are
    /// ignored — useful when indexing source trees recovered from sourcemaps or distribution archives
    /// where files would otherwise be filtered out.
    /// </summary>
    public bool RespectGitignore { get; init; } = true;

    /// <summary>
    /// Files known from an earlier discovery, by path relative to the root. A file whose size and last write time
    /// are those of its stamp takes the stamp's SHA-256 without being read again, as git trusts its index.
    /// </summary>
    public IReadOnlyDictionary<string, FileStamp>? KnownFiles { get; init; }
}

public sealed record DiscoveryResult
{
    public required ImmutableArray<DiscoveredFile> Files { get; init; }
    public required ImmutableArray<DetectedPackage> Packages { get; init; }
    public required string RootPath { get; init; }

    /// <summary>The content of each ignore file that governed the discovery (the exclude file, each <c>.gitignore</c> read), by path under the root; empty when ignore rules are not respected.</summary>
    public ImmutableDictionary<string, string> IgnoreFileContents { get; init; } = ImmutableDictionary<string, string>.Empty;

    /// <summary>
    /// Diagnostic counters describing why entries discovered by the underlying
    /// filesystem enumeration were dropped before reaching <see cref="Files"/>.
    /// Lets callers distinguish "the mount has nothing" from "everything was
    /// filtered out". All counters default to 0 when the implementation does
    /// not track them.
    /// </summary>
    public DiscoveryFilterStats FilterStats { get; init; } = new();
}

public sealed record DiscoveryFilterStats
{
    public int ExcludedByExcludeSet { get; init; }
    public int ExcludedByGitignore { get; init; }
    public int ExcludedByLanguage { get; init; }
    public int ExcludedBySuffix { get; init; }
    /// <summary>
    /// Number of files the file system policy refused to read (a mount right, a blocked
    /// extension): left out of <see cref="DiscoveryResult.Files"/> instead of failing the discovery.
    /// </summary>
    public int ExcludedByPolicy { get; init; }
    /// <summary>Virtual paths of the files counted by <see cref="ExcludedByPolicy"/>, in discovery order.</summary>
    public ImmutableArray<string> ExcludedByPolicyPaths { get; init; } = [];
    public int TotalEnumerated { get; init; }
    /// <summary>
    /// Number of file entries the VFS <c>IFileSystemService.EnumerateFilesAsync</c>
    /// actually yielded (before any kind/language/suffix filter). When this is 0 but
    /// <see cref="PhysicalEntriesProbe"/> &gt; 0, the VFS path mapping is broken (mount
    /// table mismatch, case-sensitivity, etc.) rather than the disk being empty.
    /// </summary>
    public int VfsFileEntriesYielded { get; init; }
    /// <summary>
    /// Direct-from-disk probe: counts entries under the resolved physical root via
    /// <c>Directory.EnumerateFileSystemEntries(... AllDirectories)</c>. Bypasses the
    /// VFS so callers can detect VFS layer bugs.
    /// </summary>
    public int PhysicalEntriesProbe { get; init; }
    /// <summary>Resolved physical root (best-effort, redacted of nothing — diagnostic only).</summary>
    public string PhysicalRoot { get; init; } = "";
}

/// <summary>A package found by discovery: <see cref="MarkerFile"/> names its ecosystem (<c>npm</c>, <c>csproj</c>).</summary>
public sealed record DetectedPackage(string Name, string RelativePath, string MarkerFile)
{
    /// <summary>Path of the marker file under the root (<c>src/App/App.csproj</c>, <c>package.json</c>).</summary>
    public string MarkerPath { get; init; } = "";
}
