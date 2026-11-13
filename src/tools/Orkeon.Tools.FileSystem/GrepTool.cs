using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Security;
using Orkeon.Rag.Abstractions;
using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Tools.FileSystem;

/// <summary>Request parameters for the grep tool.</summary>
public sealed record GrepRequest
{
    /// <summary>Regular expression searched in each line.</summary>
    [JsonPropertyName("pattern")]
    [FieldSchema(Description = "Regular expression searched in each line (per-match timeout; the scan stops and timed_out is set on a timeout)", Example = "TODO")]
    public string Pattern { get; init; } = "";

    /// <summary>Root directory, or the one file searched alone (virtual path).</summary>
    [JsonPropertyName("path")]
    [FieldSchema(Description = "Directory searched, or one file searched alone (then no glob); a virtual path inside the agent root", Example = "/workspace/src")]
    public string Path { get; init; } = "";

    /// <summary>Optional file filter relative to <see cref="Path"/>.</summary>
    [JsonPropertyName("glob")]
    [FieldSchema(Description = "Optional file filter relative to a folder path (never absolute, never with a file path); all files when absent", IsRequired = false, Example = "**/*.cs")]
    public string? Glob { get; init; }

    /// <summary>Case-insensitive matching.</summary>
    [JsonPropertyName("case_insensitive")]
    [FieldSchema(Description = "Case-insensitive matching", IsRequired = false, Example = false)]
    public bool CaseInsensitive { get; init; }

    /// <summary>Maximum number of hits.</summary>
    [JsonPropertyName("max_results")]
    [FieldSchema(Description = "Maximum number of hits (default: 200)", IsRequired = false, Example = 200)]
    public int MaxResults { get; init; } = 200;

    /// <summary>Maximum number of files scanned.</summary>
    [JsonPropertyName("max_files")]
    [FieldSchema(Description = "Maximum number of files scanned; binary or unreadable files are skipped and counted (default: 2000)", IsRequired = false, Example = 2000)]
    public int MaxFiles { get; init; } = 2000;

    /// <summary>What the response carries: the hit lines, or counts alone.</summary>
    [JsonPropertyName("output_mode")]
    [FieldSchema(Description = "hits: one path:line:text per match (default); the scan is not stopped by max_results, it reads up to max_files or the 20 s budget, so a wide search can come back timed_out or truncated. count: no hit lines, only total_matches, files_matched and the per-file counts (the max_results highest)", IsRequired = false, Example = "count", Default = "hits", Enum = new[] { "hits", "count" })]
    public string OutputMode { get; init; } = GrepOutputModes.Hits;
}

/// <summary>The values of <see cref="GrepRequest.OutputMode"/>.</summary>
public static class GrepOutputModes
{
    /// <summary>One hit per matching line.</summary>
    public const string Hits = "hits";

    /// <summary>Counts only, no hit lines.</summary>
    public const string Count = "count";
}

/// <summary>The number of matching lines in one file.</summary>
public sealed record GrepFileCount
{
    /// <summary>Virtual path of the file.</summary>
    [JsonPropertyName("path")]
    public string Path { get; init; } = "";

    /// <summary>Matching lines in the file.</summary>
    [JsonPropertyName("count")]
    public int Count { get; init; }
}

/// <summary>One matching line.</summary>
public sealed record GrepHit
{
    /// <summary>Virtual path of the file.</summary>
    [JsonPropertyName("path")]
    public string Path { get; init; } = "";

    /// <summary>1-based line number.</summary>
    [JsonPropertyName("line")]
    public int Line { get; init; }

    /// <summary>Line text.</summary>
    [JsonPropertyName("text")]
    public string Text { get; init; } = "";
}

/// <summary>Response of the grep tool.</summary>
public sealed record GrepResponse : IBoundedToolResponse
{
    /// <summary>Matching lines.</summary>
    [JsonPropertyName("hits")]
    [ReturnSchema(Description = "Matching lines")]
    public ImmutableList<GrepHit> Hits { get; init; } = [];

    /// <summary>Matching lines per file in count mode, the <c>max_results</c> highest first; empty in hits mode.</summary>
    [JsonPropertyName("counts")]
    [ReturnSchema(Description = "Count mode: matching lines per file, the max_results highest first (files_matched says how many files matched in all); empty in hits mode")]
    public ImmutableList<GrepFileCount> Counts { get; init; } = [];

    /// <summary>Matching lines over the files scanned, hits included or not.</summary>
    [JsonPropertyName("total_matches")]
    [ReturnSchema(Description = "Matching lines over the files scanned, whether or not they are listed in hits; exact when scan_complete is true")]
    public int TotalMatches { get; init; }

    /// <summary>Files with at least one matching line.</summary>
    [JsonPropertyName("files_matched")]
    [ReturnSchema(Description = "Files with at least one matching line; exact when scan_complete is true")]
    public int FilesMatched { get; init; }

    /// <summary>True when every candidate file was scanned to its end.</summary>
    [JsonPropertyName("scan_complete")]
    [ReturnSchema(Description = "True when every candidate file was scanned to its end (no file over 1 MiB left unread, no max_files cut, no timeout), so total_matches and files_matched are exact")]
    public bool ScanComplete { get; init; }

    /// <summary>True when hits or files were left out.</summary>
    [JsonPropertyName("truncated")]
    [ReturnSchema(Description = "True when hits or files were left out: max_results or the size bound cut the hits, or max_files or a timeout cut the scan")]
    public bool Truncated { get; init; }

    /// <summary>True when the scan stopped on a time bound.</summary>
    [JsonPropertyName("timed_out")]
    [ReturnSchema(Description = "True when the scan stopped on a time bound")]
    public bool TimedOut { get; init; }

    /// <summary>Text files scanned.</summary>
    [JsonPropertyName("files_scanned")]
    [ReturnSchema(Description = "Text files scanned")]
    public int FilesScanned { get; init; }

    /// <summary>Files skipped (binary, unreadable or over 1 MiB).</summary>
    [JsonPropertyName("files_skipped")]
    [ReturnSchema(Description = "Files skipped (binary, unreadable or over 1 MiB)")]
    public int FilesSkipped { get; init; }

    /// <inheritdoc />
    [JsonIgnore]
    public bool AlreadyBounded { get; init; }
}

/// <summary>Searches the lines of text files under a directory with a bounded regex.</summary>
public sealed class GrepTool : FileToolBase<GrepRequest, GrepResponse>
{
    /// <summary>Marker appended to a hit line cut at 500 characters.</summary>
    public const string LineCutMarker = "[...]";

    private const int MaxLineChars = 500;
    private const int MaxTotalChars = 16_000;
    private const long MaxFileBytes = 1_048_576;

    private readonly TimeSpan _matchTimeout;
    private readonly TimeSpan _totalBudget;

    /// <inheritdoc />
    public override string Name => "grep";

    /// <inheritdoc />
    public override string Description => "Which files contain this text? Searches the lines of the text files under path with a regex, glob filtering the files relative to path (a file as path is searched alone, without glob), and returns path:line:text hits with total_matches and files_matched; output_mode \"count\" returns the counts alone, so a \"how many\" question takes one call. At most max_results hits (200) over max_files files (2000) within a 20 s budget; binary and over-1-MiB files are skipped; truncated and scan_complete say when the scan was cut. To find files by name, glob; to search code by structure, structural_search; to search by meaning, codebase_search.";

    /// <summary>Declared access class for permission gates.</summary>
    public override ToolAccess Access => ToolAccess.Read;

    /// <summary>Initializes a new instance of <see cref="GrepTool"/>.</summary>
    public GrepTool(IFileSystemService fileSystemService, IPathValidator pathValidator, ILogger<GrepTool>? logger = null,
        TimeSpan? matchTimeout = null, TimeSpan? totalBudget = null)
        : base(fileSystemService, pathValidator, logger)
    {
        _matchTimeout = matchTimeout ?? TimeSpan.FromSeconds(2);
        _totalBudget = totalBudget ?? TimeSpan.FromSeconds(20);
    }

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(GrepRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrEmpty(request.Pattern))
            return "Pattern cannot be empty";
        if (request.Glob is not null && (request.Glob.StartsWith('/') || request.Glob.StartsWith('\\')))
            return "Glob must be relative to path, not absolute";
        if (request.Glob is not null && request.Glob.Split(['/', '\\']).Contains(".."))
            return "Glob cannot contain a '..' segment";
        if (request.MaxResults <= 0)
            return "max_results must be greater than 0";
        if (request.MaxFiles <= 0)
            return "max_files must be greater than 0";
        if (request.OutputMode is not (GrepOutputModes.Hits or GrepOutputModes.Count))
            return $"output_mode must be \"{GrepOutputModes.Hits}\" or \"{GrepOutputModes.Count}\"";
        if (string.IsNullOrWhiteSpace(request.Path))
            return "Path cannot be empty";

        var validation = ResolveVirtualPath(request.Path, FileAccessRights.Read);
        return validation.IsAllowed ? null : validation.DenialReason ?? "Invalid or unsafe directory path";
    }

    /// <inheritdoc />
    protected override async Task<GrepResponse> ExecuteTypedAsync(GrepRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        // A relative root resolves against the working directory, as ValidateTypedRequest judged it.
        request = request with { Path = AbsolutizePath(request.Path) };
        var regex = BuildRegex(request);

        if (!await FileSystemService.ExistsAsync(request.Path, cancellationToken).ConfigureAwait(false))
            throw new FileNotFoundException($"Path not found: {request.Path}");

        var sizes = new Dictionary<string, long>(StringComparer.Ordinal);
        IEnumerable<string> candidates;
        var kind = await FileSystemService.GetEntryKindAsync(request.Path, cancellationToken).ConfigureAwait(false);
        if (kind == VirtualEntryKind.Directory)
        {
            var location = $"{request.Path.TrimEnd('/')}/{request.Glob ?? "**/*"}";
            var expanded = await SourceGlobExpander.ExpandAsync(FileSystemService, [location], cancellationToken).ConfigureAwait(false);
            candidates = expanded.Select(candidate => candidate.Location);
            await foreach (var entry in FileSystemService
                .EnumerateFilesAsync(request.Path, new VirtualEnumerationOptions(Recursive: true), cancellationToken).ConfigureAwait(false))
                sizes[entry.VirtualPath] = entry.SizeBytes;
        }
        else if (kind == VirtualEntryKind.File)
        {
            // One file searched alone, as rg <file> does (#351); a glob filters a folder's files and has
            // nothing to filter here, so it is refused rather than silently dropped.
            if (request.Glob is not null)
                throw new ArgumentException(
                    $"path names a file ({request.Path}): glob applies to a folder only; drop glob, or pass the folder as path and the file name as glob");
            candidates = [request.Path];
        }
        else
        {
            throw new ArgumentException($"path is neither a folder nor a file: {request.Path}");
        }

        var scan = new GrepScan();
        var collectHits = request.OutputMode == GrepOutputModes.Hits;
        await ScanCandidatesAsync(request, candidates, sizes, regex, collectHits, scan, cancellationToken).ConfigureAwait(false);

        var counts = collectHits ? [] : TopCounts(scan, request.MaxResults);
        return new GrepResponse
        {
            Hits = scan.Hits.ToImmutable(),
            Counts = counts,
            TotalMatches = scan.Matches,
            FilesMatched = scan.FileCounts.Count,
            ScanComplete = !scan.Oversize && !scan.FilesCut && !scan.TimedOut,
            Truncated = scan.HitsCut || scan.FilesCut || scan.TimedOut,
            TimedOut = scan.TimedOut,
            FilesScanned = scan.Scanned,
            FilesSkipped = scan.Skipped,
            AlreadyBounded = scan.Bounded,
        };
    }

    private Regex BuildRegex(GrepRequest request)
    {
        var options = request.CaseInsensitive ? RegexOptions.IgnoreCase | RegexOptions.CultureInvariant : RegexOptions.CultureInvariant;
        try
        {
            return new Regex(request.Pattern, options, _matchTimeout);
        }
        catch (ArgumentException)
        {
            throw new ArgumentException($"Invalid regular expression: {request.Pattern}");
        }
    }

    private async Task ScanCandidatesAsync(
        GrepRequest request, IEnumerable<string> paths, Dictionary<string, long> sizes, Regex regex, bool collectHits,
        GrepScan scan, CancellationToken cancellationToken)
    {
        foreach (var path in paths)
        {
            if (scan.Scanned + scan.Skipped >= request.MaxFiles)
            {
                scan.FilesCut = true;
                return;
            }

            var text = await ReadCandidateAsync(path, sizes, scan, cancellationToken).ConfigureAwait(false);
            if (text is null)
            {
                scan.Skipped++;
                continue;
            }

            scan.Scanned++;
            ScanText(path, text, regex, collectHits ? request.MaxResults : null, scan);
            if (scan.TimedOut) return;
        }
    }

    /// <summary>The per-file counts, highest first, cut to <paramref name="maxResults"/> entries and to <see cref="MaxTotalChars"/>.</summary>
    private static ImmutableList<GrepFileCount> TopCounts(GrepScan scan, int maxResults)
    {
        var counts = ImmutableList.CreateBuilder<GrepFileCount>();
        var chars = 0;
        foreach (var (path, count) in scan.FileCounts.OrderByDescending(c => c.Count).ThenBy(c => c.Path, StringComparer.Ordinal))
        {
            if (counts.Count >= maxResults)
            {
                scan.HitsCut = true;
                break;
            }
            chars += path.Length + 8;
            if (chars > MaxTotalChars)
            {
                scan.Bounded = true;
                scan.HitsCut = true;
                break;
            }
            counts.Add(new GrepFileCount { Path = path, Count = count });
        }
        return counts.ToImmutable();
    }

    /// <summary>The text of <paramref name="path"/>, or null when it is too large, unreadable or binary.</summary>
    private async Task<string?> ReadCandidateAsync(string path, Dictionary<string, long> sizes, GrepScan scan, CancellationToken cancellationToken)
    {
        var size = sizes.TryGetValue(path, out var known)
            ? known
            : (await FileSystemService.TryGetEntryAsync(path, cancellationToken).ConfigureAwait(false))?.SizeBytes;
        if (size > MaxFileBytes) scan.Oversize = true;
        return size is null or > MaxFileBytes
            ? null
            : await ReadTextAsync(path, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Scans the lines of one file, counting its matches and, when <paramref name="maxResults"/> is given, adding
    /// them to the hits of <paramref name="scan"/> until a bound stops that; only a timeout stops the count.
    /// </summary>
    private void ScanText(string path, string text, Regex regex, int? maxResults, GrepScan scan)
    {
        var start = 0;
        var lineNo = 0;
        var matches = 0;
        while (start < text.Length && !scan.TimedOut)
        {
            var end = text.IndexOf('\n', start);
            var next = end < 0 ? text.Length : end + 1;
            var line = text.AsSpan(start, (end < 0 ? text.Length : end) - start).TrimEnd('\r').ToString();
            start = next;
            lineNo++;

            if (scan.Clock.Elapsed > _totalBudget)
            {
                scan.TimedOut = true;
                break;
            }

            if (!IsMatch(regex, line, scan)) continue;
            matches++;
            if (maxResults is { } max)
                AddHit(path, lineNo, line, max, scan);
        }

        if (matches == 0) return;
        scan.Matches += matches;
        scan.FileCounts.Add((path, matches));
    }

    /// <summary>Whether <paramref name="line"/> matches; a match timeout marks the scan timed out.</summary>
    private static bool IsMatch(Regex regex, string line, GrepScan scan)
    {
        try
        {
            return regex.IsMatch(line);
        }
        catch (RegexMatchTimeoutException)
        {
            scan.TimedOut = true;
            return false;
        }
    }

    /// <summary>Adds one hit, cut to <see cref="MaxLineChars"/>, unless a result or size bound is reached.</summary>
    private static void AddHit(string path, int lineNo, string line, int maxResults, GrepScan scan)
    {
        if (scan.HitsCut) return;
        if (line.Length > MaxLineChars)
            line = line[..MaxLineChars] + LineCutMarker;
        if (scan.Hits.Count >= maxResults)
        {
            scan.HitsCut = true;
            return;
        }
        var cost = path.Length + line.Length;
        if (scan.Chars + cost > MaxTotalChars)
        {
            scan.HitsCut = scan.Bounded = true;
            return;
        }
        scan.Chars += cost;
        scan.Hits.Add(new GrepHit { Path = path, Line = lineNo, Text = line });
    }

    /// <summary>The running state of one grep: its hits, counters and what cut it.</summary>
    private sealed class GrepScan
    {
        public ImmutableList<GrepHit>.Builder Hits { get; } = ImmutableList.CreateBuilder<GrepHit>();

        public List<(string Path, int Count)> FileCounts { get; } = [];

        public int Matches { get; set; }

        public Stopwatch Clock { get; } = Stopwatch.StartNew();

        public int Chars { get; set; }

        public int Scanned { get; set; }

        public int Skipped { get; set; }

        /// <summary>A file over 1 MiB was left unread; a binary or unreadable file is skipped by design and does not count.</summary>
        public bool Oversize { get; set; }

        /// <summary>Hits were left out: <c>max_results</c> or the size bound was reached.</summary>
        public bool HitsCut { get; set; }

        /// <summary>Files were left out: <c>max_files</c> was reached.</summary>
        public bool FilesCut { get; set; }

        public bool Bounded { get; set; }

        public bool TimedOut { get; set; }
    }

    private async Task<string?> ReadTextAsync(string virtualPath, CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await FileSystemService.TryReadAllBytesAsync(virtualPath, cancellationToken).ConfigureAwait(false);
            if (bytes is null) return null;
            var (encoding, bom) = EditFileTool.DetectEncoding(bytes);
            var text = encoding.GetString(bytes, bom.Length, bytes.Length - bom.Length);
            return text.Contains('\0', StringComparison.Ordinal) ? null : text;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.DecoderFallbackException or FileAccessDeniedException)
        {
            return null;
        }
    }
}
