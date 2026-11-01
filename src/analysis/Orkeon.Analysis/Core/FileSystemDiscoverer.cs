using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.TreeSitter;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Analysis.Core;

public sealed class FileSystemDiscoverer : IFileSystemDiscoverer
{
    private static readonly Dictionary<string, string> LanguageByExtension =
        LanguageRegistry.Shipped
            .SelectMany(g => g.Extensions.Select(e => (Extension: e, g.Language)))
            .ToDictionary(x => x.Extension, x => x.Language, StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlySet<string> AlwaysExcludedFileSuffixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".d.ts", ".min.js"
    };

    private static readonly Dictionary<string, string> PackageMarkers =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["package.json"] = "npm",
            ["pyproject.toml"] = "python",
            ["Cargo.toml"] = "cargo",
            ["go.mod"] = "go",
            ["pom.xml"] = "maven",
            ["build.gradle"] = "gradle",
            ["build.gradle.kts"] = "gradle",
            ["composer.json"] = "composer",
            ["Gemfile"] = "bundler",
            ["CMakeLists.txt"] = "cmake",
        };

    private readonly IFileSystemService _fs;

    public FileSystemDiscoverer(IFileSystemService fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        _fs = fs;
    }

    public async IAsyncEnumerable<DiscoveredFile> DiscoverAsync(
        DiscoveryRequest req, [EnumeratorCancellation] CancellationToken ct)
    {
        var ctx = Prepare(req);
        var stats = new FilterStatsAccumulator();

        await foreach (var entry in EnumerateAsync(ctx, stats, ct).ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();
            var discovered = await TryBuildAsync(entry, ctx, stats, ct).ConfigureAwait(false);
            if (discovered is not null) yield return discovered;
        }
    }

    public async Task<DiscoveryResult> DiscoverAllAsync(DiscoveryRequest req, CancellationToken ct)
    {
        var ctx = Prepare(req);

        var files = ImmutableArray.CreateBuilder<DiscoveredFile>();
        var packages = ImmutableArray.CreateBuilder<DetectedPackage>();
        var stats = new FilterStatsAccumulator();

        await foreach (var entry in EnumerateAsync(ctx, stats, ct).ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();

            var relative = entry.Relative;
            var fileName = Path.GetFileName(relative);

            if (PackageMarkers.TryGetValue(fileName, out var marker))
            {
                var pkgDir = Path.GetDirectoryName(relative) ?? string.Empty;
                var pkgName = string.IsNullOrEmpty(pkgDir)
                    ? TrailingSegment(ctx.VirtualRoot)
                    : Path.GetFileName(pkgDir);
                if (string.IsNullOrEmpty(pkgName)) pkgName = fileName;
                packages.Add(new DetectedPackage(pkgName, pkgDir, marker) { MarkerPath = relative });
            }
            else if (fileName.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                var pkgDir = Path.GetDirectoryName(relative) ?? string.Empty;
                var pkgName = Path.GetFileNameWithoutExtension(fileName);
                packages.Add(new DetectedPackage(pkgName, pkgDir, "csproj") { MarkerPath = relative });
            }

            var discovered = await TryBuildAsync(entry, ctx, stats, ct).ConfigureAwait(false);
            if (discovered is not null) files.Add(discovered);
        }

        return new DiscoveryResult
        {
            Files = files.ToImmutable(),
            Packages = packages.ToImmutable(),
            RootPath = ctx.VirtualRoot,
            IgnoreFileContents = stats.IgnoreFileTexts.ToImmutableDictionary(kv => kv.Path, kv => kv.Text, StringComparer.Ordinal),
            FilterStats = stats.Snapshot(),
        };
    }

    private DiscoveryContext Prepare(DiscoveryRequest req)
    {
        ArgumentNullException.ThrowIfNull(req);
        ArgumentException.ThrowIfNullOrWhiteSpace(req.RootPath);
        return PrepareCore(req);
    }

    private DiscoveryContext PrepareCore(DiscoveryRequest req)
    {
        var virtualRoot = NormalizeVirtualRoot(req.RootPath);

        var validation = _fs.ResolveAndValidate(virtualRoot, FileAccessRights.Read);
        if (!validation.IsAllowed)
            throw new FileAccessDeniedException(
                validation.DenialReason ?? $"Access denied for virtual path '{virtualRoot}'.",
                virtualRoot,
                FileAccessRights.Read);

        var physicalRoot = validation.ResolvedPath!;

        var excludeSet = new HashSet<string>(req.Exclude, StringComparer.OrdinalIgnoreCase);
        var languageFilter = req.Languages.IsDefaultOrEmpty
            ? null
            : new HashSet<string>(req.Languages, StringComparer.OrdinalIgnoreCase);

        return new DiscoveryContext(virtualRoot, physicalRoot, excludeSet, languageFilter, req.RespectGitignore, req.KnownFiles);
    }

    [Orkeon.Compliance.Vfs.SuppressVfsCompliance(
        "OUT-OF-SCOPE: diagnostic probe that counts entries on the already-resolved " +
        "physical root for filter-stats reporting only. Bypasses the VFS by design — " +
        "the whole point is to distinguish 'VFS yielded nothing' from 'disk is empty'.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Best-effort diagnostic probe: any failure counting physical entries leaves the stats unset and must not interrupt VFS-driven discovery.")]
    private async IAsyncEnumerable<DiscoveredEntry> EnumerateAsync(
        DiscoveryContext ctx,
        FilterStatsAccumulator stats,
        [EnumeratorCancellation] CancellationToken ct)
    {
        stats.PhysicalRoot = ctx.PhysicalRoot;
        // OUT-OF-SCOPE: direct-from-disk probe to surface VFS path-mapping issues.
        // The physical root is already resolved + access-validated by PrepareAsync.
        try
        {
            var probeCount = 0;
            var enumOpts = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
                ReturnSpecialDirectories = false,
            };
            foreach (var _ in Directory.EnumerateFileSystemEntries(ctx.PhysicalRoot, "*", enumOpts))
            {
                if (++probeCount >= 10000) break;
            }
            stats.PhysicalEntriesProbe = probeCount;
        }
        catch { /* probe is best-effort */ }

        // The walk first, the ignore files after it: a nested .gitignore governs files the walk may have
        // yielded before it, so the whole tree is known before any path is judged.
        var walked = new List<DiscoveredEntry>();
        var gitignores = new List<string>();
        var options = new VirtualEnumerationOptions(Recursive: true);
        await foreach (var entry in _fs.EnumerateFilesAsync(ctx.VirtualRoot, options, ct).ConfigureAwait(false))
        {
            if (entry.Kind != VirtualEntryKind.File) continue;
            stats.VfsFileEntriesYielded++;
            stats.TotalEnumerated++;

            var relative = ToRelative(ctx.VirtualRoot, entry.VirtualPath);
            if (HasExcludedSegment(relative, ctx.ExcludeSet))
            {
                stats.ExcludedByExcludeSet++;
                continue;
            }

            if (ctx.RespectGitignore && Path.GetFileName(relative) == ".gitignore") gitignores.Add(relative);
            walked.Add(new DiscoveredEntry(entry, relative));
        }

        var ignore = ctx.RespectGitignore
            ? await IgnoreFiles.LoadAsync(_fs, ctx.VirtualRoot, gitignores, ct).ConfigureAwait(false)
            : null;
        if (ignore is not null) stats.IgnoreFileTexts.AddRange(ignore.Contents.Select(kv => (kv.Key, kv.Value)));
        foreach (var entry in walked)
        {
            if (ignore is not null && ignore.IsIgnored(entry.Relative))
            {
                stats.ExcludedByGitignore++;
                continue;
            }

            yield return entry;
        }
    }

    private async Task<DiscoveredFile?> TryBuildAsync(
        DiscoveredEntry entry, DiscoveryContext ctx, FilterStatsAccumulator stats, CancellationToken ct)
    {
        var fileName = Path.GetFileName(entry.Relative);
        if (AlwaysExcludedFileSuffixes.Any(suffix => fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
        {
            stats.ExcludedBySuffix++;
            return null;
        }

        var ext = Path.GetExtension(fileName);
        if (!LanguageByExtension.TryGetValue(ext, out var language))
        {
            stats.ExcludedByLanguage++;
            return null;
        }
        if (ctx.LanguageFilter is not null && !ctx.LanguageFilter.Contains(language))
        {
            stats.ExcludedByLanguage++;
            return null;
        }

        if (ctx.KnownFiles is not null
            && ctx.KnownFiles.TryGetValue(entry.Relative, out var known)
            && known.LastModified is { } knownModified
            && knownModified.UtcTicks == entry.Entry.LastModified.UtcTicks
            && known.SizeBytes == entry.Entry.SizeBytes
            && _fs.ResolveAndValidate(entry.Entry.VirtualPath, FileAccessRights.Read).IsAllowed)
        {
            return new DiscoveredFile(entry.Entry.VirtualPath, entry.Relative, language, known.SizeBytes, known.Sha256)
            {
                LastModified = entry.Entry.LastModified,
            };
        }

        string sha;
        long size;
        try
        {
            var stream = await _fs.OpenReadStreamAsync(entry.Entry.VirtualPath, ct).ConfigureAwait(false);
            await using var __stream = stream.ConfigureAwait(false);
            using var hasher = SHA256.Create();
            var hash = await hasher.ComputeHashAsync(stream, ct).ConfigureAwait(false);
            sha = Convert.ToHexStringLower(hash);
            size = entry.Entry.SizeBytes;
        }
        catch (FileNotFoundException) { return null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (FileAccessDeniedException)
        {
            // The VFS policy refused the read (a mount right, a blocked extension): the file is
            // left out of the index and reported, never a failure of the whole discovery.
            stats.ExcludedByPolicy++;
            stats.ExcludedByPolicyPaths.Add(entry.Entry.VirtualPath);
            return null;
        }

        return new DiscoveredFile(entry.Entry.VirtualPath, entry.Relative, language, size, sha)
        {
            LastModified = entry.Entry.LastModified,
        };
    }

    private static string TrailingSegment(string virtualPath)
    {
        var trimmed = virtualPath.TrimEnd('/');
        var slash = trimmed.LastIndexOf('/');
        return slash < 0 ? trimmed : trimmed[(slash + 1)..];
    }

    private static string NormalizeVirtualRoot(string virtualRoot)
    {
        var trimmed = virtualRoot.TrimEnd('/');
        return string.IsNullOrEmpty(trimmed) ? "/" : trimmed;
    }

    private static string ToRelative(string virtualRoot, string virtualPath)
    {
        if (virtualPath.StartsWith(virtualRoot + "/", StringComparison.Ordinal))
            return virtualPath[(virtualRoot.Length + 1)..];
        if (string.Equals(virtualPath, virtualRoot, StringComparison.Ordinal))
            return string.Empty;
        return virtualPath.TrimStart('/');
    }

    /// <summary>
    /// The language a full index would give <paramref name="virtualPath"/>, or <c>null</c> when
    /// the exclude set, the always-excluded suffixes, the supported extensions or the language
    /// filter keep it out: the one rule a single-file reindex shares with discovery.
    /// </summary>
    internal static string? IndexableLanguage(
        string virtualRoot, string virtualPath, IReadOnlyCollection<string> exclude, ImmutableArray<string> languages)
    {
        var relative = ToRelative(virtualRoot, virtualPath);
        if (HasExcludedSegment(relative, new HashSet<string>(exclude, StringComparer.OrdinalIgnoreCase))) return null;

        var fileName = Path.GetFileName(relative);
        if (AlwaysExcludedFileSuffixes.Any(suffix => fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))) return null;
        if (!LanguageByExtension.TryGetValue(Path.GetExtension(fileName), out var language)) return null;
        return languages.IsDefaultOrEmpty || languages.Contains(language, StringComparer.OrdinalIgnoreCase) ? language : null;
    }

    private static bool HasExcludedSegment(string relative, HashSet<string> excludeSet)
    {
        if (excludeSet.Count == 0) return false;
        return relative.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(excludeSet.Contains);
    }

    private sealed record DiscoveryContext(
        string VirtualRoot,
        string PhysicalRoot,
        HashSet<string> ExcludeSet,
        HashSet<string>? LanguageFilter,
        bool RespectGitignore,
        IReadOnlyDictionary<string, FileStamp>? KnownFiles);

    private readonly record struct DiscoveredEntry(VirtualFileEntry Entry, string Relative);

    private sealed class FilterStatsAccumulator
    {
        public readonly List<(string Path, string Text)> IgnoreFileTexts = [];
        public int ExcludedByExcludeSet;
        public int ExcludedByGitignore;
        public int ExcludedByLanguage;
        public int ExcludedBySuffix;
        public int ExcludedByPolicy;
        public readonly ImmutableArray<string>.Builder ExcludedByPolicyPaths = ImmutableArray.CreateBuilder<string>();
        public int TotalEnumerated;
        public int VfsFileEntriesYielded;
        public int PhysicalEntriesProbe;
        public string PhysicalRoot = "";

        public DiscoveryFilterStats Snapshot() => new()
        {
            ExcludedByExcludeSet = ExcludedByExcludeSet,
            ExcludedByGitignore = ExcludedByGitignore,
            ExcludedByLanguage = ExcludedByLanguage,
            ExcludedBySuffix = ExcludedBySuffix,
            ExcludedByPolicy = ExcludedByPolicy,
            ExcludedByPolicyPaths = ExcludedByPolicyPaths.ToImmutable(),
            TotalEnumerated = TotalEnumerated,
            VfsFileEntriesYielded = VfsFileEntriesYielded,
            PhysicalEntriesProbe = PhysicalEntriesProbe,
            PhysicalRoot = PhysicalRoot,
        };
    }
}
