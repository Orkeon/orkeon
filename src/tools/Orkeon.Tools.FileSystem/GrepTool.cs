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

    /// <summary>Root directory (virtual path).</summary>
    [JsonPropertyName("path")]
    [FieldSchema(Description = "Root directory searched (virtual path, a mounted directory inside the agent root)", Example = "/workspace/src")]
    public string Path { get; init; } = "";

    /// <summary>Optional file filter relative to <see cref="Path"/>.</summary>
    [JsonPropertyName("glob")]
    [FieldSchema(Description = "Optional file filter relative to path (never absolute); all files when absent", IsRequired = false, Example = "**/*.cs")]
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

    /// <summary>True when hits or files were left out.</summary>
    [JsonPropertyName("truncated")]
    [ReturnSchema(Description = "True when hits or files were left out")]
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
    public override string Description => "Search the lines of text files under a mounted directory (path) with a regex; glob filters files relative to path. Binary, unreadable and over-1-MiB files are skipped; regex timeout, max_results and max_files bound the scan. Returns path:line:text hits.";

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
        Regex regex;
        try
        {
            regex = new Regex(request.Pattern, request.CaseInsensitive ? RegexOptions.IgnoreCase | RegexOptions.CultureInvariant : RegexOptions.CultureInvariant, _matchTimeout);
        }
        catch (ArgumentException)
        {
            throw new ArgumentException($"Invalid regular expression: {request.Pattern}");
        }

        if (!await _fileSystemService.ExistsAsync(request.Path, cancellationToken).ConfigureAwait(false)
            || await _fileSystemService.GetEntryKindAsync(request.Path, cancellationToken).ConfigureAwait(false) != VirtualEntryKind.Directory)
            throw new DirectoryNotFoundException($"Directory not found: {request.Path}");

        var location = $"{request.Path.TrimEnd('/')}/{request.Glob ?? "**/*"}";
        var candidates = await SourceGlobExpander.ExpandAsync(_fileSystemService, [location], cancellationToken).ConfigureAwait(false);

        var sizes = new Dictionary<string, long>(StringComparer.Ordinal);
        await foreach (var entry in _fileSystemService
            .EnumerateFilesAsync(request.Path, new VirtualEnumerationOptions(Recursive: true), cancellationToken).ConfigureAwait(false))
            sizes[entry.VirtualPath] = entry.SizeBytes;

        var scan = new GrepScan();
        foreach (var path in candidates.Select(candidate => candidate.Location))
        {
            if (scan.Scanned + scan.Skipped >= request.MaxFiles)
            {
                scan.Truncated = true;
                break;
            }

            var text = await ReadCandidateAsync(path, sizes, cancellationToken).ConfigureAwait(false);
            if (text is null)
            {
                scan.Skipped++;
                continue;
            }
            scan.Scanned++;

            ScanText(path, text, regex, request.MaxResults, scan);
            if (scan.Stopped) break;
        }

        return new GrepResponse
        {
            Hits = scan.Hits.ToImmutable(),
            Truncated = scan.Truncated,
            TimedOut = scan.TimedOut,
            FilesScanned = scan.Scanned,
            FilesSkipped = scan.Skipped,
            AlreadyBounded = scan.Bounded,
        };
    }

    /// <summary>The text of <paramref name="path"/>, or null when it is too large, unreadable or binary.</summary>
    private async Task<string?> ReadCandidateAsync(string path, Dictionary<string, long> sizes, CancellationToken cancellationToken)
    {
        var size = sizes.TryGetValue(path, out var known)
            ? known
            : (await _fileSystemService.TryGetEntryAsync(path, cancellationToken).ConfigureAwait(false))?.SizeBytes;
        return size is null or > MaxFileBytes
            ? null
            : await ReadTextAsync(path, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Scans the lines of one file, adding its hits to <paramref name="scan"/> until a bound stops it.</summary>
    private void ScanText(string path, string text, Regex regex, int maxResults, GrepScan scan)
    {
        var start = 0;
        var lineNo = 0;
        while (start < text.Length && !scan.Stopped)
        {
            var end = text.IndexOf('\n', start);
            var next = end < 0 ? text.Length : end + 1;
            var line = text.AsSpan(start, (end < 0 ? text.Length : end) - start).TrimEnd('\r').ToString();
            start = next;
            lineNo++;

            if (scan.Clock.Elapsed > _totalBudget)
            {
                scan.TimedOut = true;
                return;
            }

            if (IsMatch(regex, line, scan))
                AddHit(path, lineNo, line, maxResults, scan);
        }
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
        if (line.Length > MaxLineChars)
            line = line[..MaxLineChars] + LineCutMarker;
        if (scan.Hits.Count >= maxResults)
        {
            scan.Truncated = true;
            return;
        }
        var cost = path.Length + line.Length;
        if (scan.Chars + cost > MaxTotalChars)
        {
            scan.Truncated = scan.Bounded = true;
            return;
        }
        scan.Chars += cost;
        scan.Hits.Add(new GrepHit { Path = path, Line = lineNo, Text = line });
    }

    /// <summary>The running state of one grep: its hits, counters and why it stopped.</summary>
    private sealed class GrepScan
    {
        public ImmutableList<GrepHit>.Builder Hits { get; } = ImmutableList.CreateBuilder<GrepHit>();

        public Stopwatch Clock { get; } = Stopwatch.StartNew();

        public int Chars { get; set; }

        public int Scanned { get; set; }

        public int Skipped { get; set; }

        public bool Truncated { get; set; }

        public bool Bounded { get; set; }

        public bool TimedOut { get; set; }

        public bool Stopped => Truncated || TimedOut;
    }

    private async Task<string?> ReadTextAsync(string virtualPath, CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await _fileSystemService.TryReadAllBytesAsync(virtualPath, cancellationToken).ConfigureAwait(false);
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
