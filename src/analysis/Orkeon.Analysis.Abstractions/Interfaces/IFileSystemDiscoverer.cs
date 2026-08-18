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
    string Sha256);

public sealed record DiscoveryRequest
{
    public required string RootPath { get; init; }
    public ImmutableArray<string> Languages { get; init; } = [];
    public ImmutableArray<string> Exclude { get; init; } =
        ["node_modules", "dist", "build", ".git", "target", "bin", "obj"];

    /// <summary>
    /// When <see langword="true"/> (default), a <c>.gitignore</c> file at the
    /// virtual root is parsed and its patterns are used to filter the
    /// enumeration. When <see langword="false"/>, gitignore is ignored —
    /// useful when indexing source trees recovered from sourcemaps or
    /// distribution archives where files would otherwise be filtered out.
    /// </summary>
    public bool RespectGitignore { get; init; } = true;
}

public sealed record DiscoveryResult
{
    public required ImmutableArray<DiscoveredFile> Files { get; init; }
    public required ImmutableArray<DetectedPackage> Packages { get; init; }
    public required string RootPath { get; init; }

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

public sealed record DetectedPackage(string Name, string RelativePath, string MarkerFile);
