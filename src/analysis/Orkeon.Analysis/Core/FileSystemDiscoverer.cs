using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Analysis.Core;

public sealed class FileSystemDiscoverer : IFileSystemDiscoverer
{
    private static readonly Dictionary<string, string> LanguageByExtension =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [".ts"] = "typescript",
            [".tsx"] = "typescript",
            [".py"] = "python",
            [".cs"] = "csharp",
            [".go"] = "go",
            [".rs"] = "rust",
        };

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
        var ctx = await PrepareAsync(req, ct).ConfigureAwait(false);
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
        var ctx = await PrepareAsync(req, ct).ConfigureAwait(false);

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
                packages.Add(new DetectedPackage(pkgName, pkgDir, marker));
            }
            else if (fileName.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                var pkgDir = Path.GetDirectoryName(relative) ?? string.Empty;
                var pkgName = Path.GetFileNameWithoutExtension(fileName);
                packages.Add(new DetectedPackage(pkgName, pkgDir, "csproj"));
            }

            var discovered = await TryBuildAsync(entry, ctx, stats, ct).ConfigureAwait(false);
            if (discovered is not null) files.Add(discovered);
        }

        return new DiscoveryResult
        {
            Files = files.ToImmutable(),
            Packages = packages.ToImmutable(),
            RootPath = ctx.VirtualRoot,
            FilterStats = stats.Snapshot(),
        };
    }

    private Task<DiscoveryContext> PrepareAsync(DiscoveryRequest req, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(req);
        ArgumentException.ThrowIfNullOrWhiteSpace(req.RootPath);
        return PrepareCoreAsync(req, ct);
    }

    private async Task<DiscoveryContext> PrepareCoreAsync(DiscoveryRequest req, CancellationToken ct)
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

        GitignoreMatcher? matcher = null;
        if (req.RespectGitignore)
        {
            var gitignoreText = await _fs.TryReadAllTextAsync($"{virtualRoot}/.gitignore", ct).ConfigureAwait(false);
            if (gitignoreText is not null)
                matcher = new GitignoreMatcher(SplitLines(gitignoreText));
        }

        return new DiscoveryContext(virtualRoot, physicalRoot, excludeSet, languageFilter, matcher);
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
            if (ctx.Matcher is not null && IsPathIgnored(ctx.Matcher, relative))
            {
                stats.ExcludedByGitignore++;
                continue;
            }

            yield return new DiscoveredEntry(entry, relative);
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

        return new DiscoveredFile(entry.Entry.VirtualPath, entry.Relative, language, size, sha);
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

    private static bool HasExcludedSegment(string relative, HashSet<string> excludeSet)
    {
        if (excludeSet.Count == 0) return false;
        return relative.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(excludeSet.Contains);
    }

    private static bool IsPathIgnored(GitignoreMatcher matcher, string relative)
    {
        if (matcher.IsIgnored(relative, isDirectory: false)) return true;

        var parts = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var dirPath = string.Empty;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            dirPath = dirPath.Length == 0 ? parts[i] : $"{dirPath}/{parts[i]}";
            if (matcher.IsIgnored(dirPath, isDirectory: true)) return true;
        }
        return false;
    }

    private static string[] SplitLines(string text) =>
        text.Split(['\r', '\n'], StringSplitOptions.None);

    private sealed record DiscoveryContext(
        string VirtualRoot,
        string PhysicalRoot,
        HashSet<string> ExcludeSet,
        HashSet<string>? LanguageFilter,
        GitignoreMatcher? Matcher);

    private readonly record struct DiscoveredEntry(VirtualFileEntry Entry, string Relative);

    private sealed class FilterStatsAccumulator
    {
        public int ExcludedByExcludeSet;
        public int ExcludedByGitignore;
        public int ExcludedByLanguage;
        public int ExcludedBySuffix;
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
            TotalEnumerated = TotalEnumerated,
            VfsFileEntriesYielded = VfsFileEntriesYielded,
            PhysicalEntriesProbe = PhysicalEntriesProbe,
            PhysicalRoot = PhysicalRoot,
        };
    }
}
