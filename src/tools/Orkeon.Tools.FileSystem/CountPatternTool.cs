using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;
using Microsoft.Extensions.Logging;

namespace Orkeon.Tools.FileSystem;

// ── Typed Request / Response records ──────────────────────────────────────

/// <summary>Request for CountPatternTool: regex-based occurrence counting in a file.</summary>
public sealed class CountPatternRequest
{
    /// <summary>Virtual path of the file to scan.</summary>
    [JsonPropertyName("path")]
    [FieldSchema(Description = "Virtual file path to scan", Example = "/output/report.md", IsRequired = true)]
    public string Path { get; set; } = string.Empty;

    /// <summary>Regex patterns to count. Each pattern is evaluated independently, occurrences are counted across the full content.</summary>
    [JsonPropertyName("patterns")]
    [FieldSchema(Description = "Regex patterns to count (each evaluated independently over the full file)", IsRequired = true, Example = new[] { "^```[a-z]+", "\\[P[123]\\]" })]
    public IReadOnlyList<string> Patterns { get; init; } = [];

    /// <summary>When true, apply RegexOptions.Multiline so ^/$ match line boundaries. Default: true.</summary>
    [JsonPropertyName("multiline")]
    [FieldSchema(Description = "Apply RegexOptions.Multiline so ^/$ match line starts/ends", IsRequired = false, Example = true, Default = true)]
    public bool Multiline { get; set; } = true;

    /// <summary>
    /// When true, <see cref="PatternCount.Count"/> is the cardinality of the set of matched
    /// substrings (each unique match counted once); when false (default), it is the raw match count.
    /// Experiment 07 friction #6: complex regex with alternation still over-counts when the same
    /// slug appears at multiple call sites; <c>DistinctMatches</c> answers "how many unique X did
    /// you find?" without requiring a custom alternation pattern.
    /// </summary>
    [JsonPropertyName("distinct_matches")]
    [FieldSchema(Description = "Count unique matched substrings instead of raw matches", IsRequired = false, Example = false, Default = false)]
    public bool DistinctMatches { get; set; }

    /// <summary>
    /// When true, <see cref="PatternCount.Matches"/> is populated with the matched substrings
    /// themselves so a downstream task can enumerate them without re-reading the file.
    /// Capped by <see cref="MaxMatchesReturned"/> (default 200).
    /// </summary>
    [JsonPropertyName("include_matches")]
    [FieldSchema(Description = "Return the matched substrings themselves (capped by max_matches_returned)", IsRequired = false, Example = false, Default = false)]
    public bool IncludeMatches { get; set; }

    /// <summary>
    /// Cap on the size of <see cref="PatternCount.Matches"/> when <see cref="IncludeMatches"/>
    /// is true. Prevents context blow-up on very dense documents. Default: 200.
    /// </summary>
    [JsonPropertyName("max_matches_returned")]
    [FieldSchema(Description = "Cap on the returned matches list size (only applies when include_matches=true)", IsRequired = false, Example = 200)]
    public int? MaxMatchesReturned { get; set; }
}

/// <summary>One entry in the CountPatternResponse.</summary>
public sealed class PatternCount
{
    /// <summary>The exact regex that was counted.</summary>
    [JsonPropertyName("pattern")]
    [ReturnSchema(Description = "The regex pattern", Example = "^```[a-z]+")]
    public string Pattern { get; set; } = string.Empty;

    /// <summary>Number of non-overlapping matches observed in the file content.</summary>
    [JsonPropertyName("count")]
    [ReturnSchema(Description = "Number of non-overlapping matches", Example = 9)]
    public int Count { get; set; }

    /// <summary>When a pattern is invalid regex, this field holds the parser error.</summary>
    [JsonPropertyName("error")]
    [ReturnSchema(Description = "Parser error when the pattern is invalid (null otherwise)")]
    public string? Error { get; set; }

    /// <summary>
    /// Matched substrings (populated only when the request set <c>include_matches=true</c>).
    /// In document order when <c>distinct_matches=false</c>, sorted-unique when
    /// <c>distinct_matches=true</c>. Capped by the request's <c>max_matches_returned</c>
    /// (default 200). <c>null</c> when the caller did not opt in.
    /// </summary>
    [JsonPropertyName("matches")]
    [ReturnSchema(Description = "Matched substrings when include_matches=true (capped)")]
    public IReadOnlyList<string>? Matches { get; init; }
}

/// <summary>Response for CountPatternTool.</summary>
public sealed class CountPatternResponse
{
    /// <summary>Per-pattern counts, in the same order as the request.</summary>
    [JsonPropertyName("counts")]
    [ReturnSchema(Description = "Per-pattern counts in request order")]
    public IReadOnlyList<PatternCount> Counts { get; init; } = [];

    /// <summary>Total number of bytes scanned (useful for sanity checks).</summary>
    [JsonPropertyName("bytes_scanned")]
    [ReturnSchema(Description = "Size of the scanned content in bytes", Example = 28267)]
    public int BytesScanned { get; set; }

    /// <summary>True when a leading U+FEFF (UTF-8 BOM as code point) was stripped before regex matching.</summary>
    [JsonPropertyName("bom_stripped")]
    [ReturnSchema(Description = "True if a leading U+FEFF was stripped before matching", Example = false)]
    public bool BomStripped { get; set; }
}

// ── Tool implementation ──────────────────────────────────────────────────

/// <summary>
/// Deterministic regex-occurrence counter.
/// Designed for verification tasks where the LLM must NOT estimate counts.
/// </summary>
[ToolContract("count_pattern",
    Name = "count_pattern",
    Description = "Count regex occurrences in a file (deterministic, per-pattern). Use when verification requires exact counts rather than LLM estimation.")]
public partial class CountPatternTool : FileToolBase<CountPatternRequest, CountPatternResponse>
{
    // R30-P1: U+FEFF (UTF-8 BOM as a Unicode code point).
    private const char Utf8BomCodePoint = '﻿';

    private readonly IFileSystemService _fs;

    /// <summary>Initializes a new instance with VFS support.</summary>
    public CountPatternTool(
        IFileSystemService fileSystemService,
        ILogger<CountPatternTool>? logger = null)
        : base(fileSystemService, logger)
    {
        _fs = fileSystemService ?? throw new ArgumentNullException(nameof(fileSystemService));
    }

    /// <inheritdoc />
    public override string Name => "count_pattern";

    /// <inheritdoc />
    public override string Description =>
        "Count regex occurrences in a file (deterministic, per-pattern). " +
        "Use when verification requires exact counts rather than LLM estimation.";

    /// <summary>Declared access class for permission gates.</summary>
    public override ToolAccess Access => ToolAccess.Read;

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(CountPatternRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Path))
            return "Path parameter is required";
        if (request.Patterns is null || request.Patterns.Count == 0)
            return "At least one pattern is required";
        return null;
    }

    /// <inheritdoc />
    protected override Task<CountPatternResponse> ExecuteTypedAsync(
        CountPatternRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteTypedCoreAsync();

        async Task<CountPatternResponse> ExecuteTypedCoreAsync()
        {
            var validation = ResolveVirtualPath(request.Path, FileAccessRights.Read);
            if (!validation.IsAllowed)
                throw new InvalidOperationException(validation.DenialReason ?? "Invalid or unsafe file path");

            var virtualPath = _fs.ToVirtualPath(validation.ResolvedPath!) ?? request.Path;
            if (!await _fs.ExistsAsync(virtualPath, cancellationToken).ConfigureAwait(false))
                throw new FileNotFoundException($"File not found: {virtualPath}");

            var rawContent = await _fs.TryReadAllTextAsync(virtualPath, cancellationToken).ConfigureAwait(false)
                ?? string.Empty;

            // R30-P1: defensively strip a leading U+FEFF.
            var (content, bomStripped) = StripLeadingBom(rawContent);

            var options = request.Multiline
                ? RegexOptions.Multiline | RegexOptions.CultureInvariant
                : RegexOptions.CultureInvariant;

            var maxMatchesReturned = request.MaxMatchesReturned ?? DefaultMaxMatchesReturned;
            if (maxMatchesReturned < 0) maxMatchesReturned = 0;

            var counts = CountAllPatterns(request, content, options, maxMatchesReturned);

            return new CountPatternResponse
            {
                Counts = counts,
                BytesScanned = content.Length,
                BomStripped = bomStripped,
            };
        }
    }

    private const int DefaultMaxMatchesReturned = 200;

    // R30-P1: strip a single leading U+FEFF (UTF-8 BOM as a code point) so ^ anchors match line 1.
    private static (string Content, bool BomStripped) StripLeadingBom(string content)
    {
        if (content.Length > 0 && content[0] == Utf8BomCodePoint)
            return (content.Substring(1), true);
        return (content, false);
    }

    private static List<PatternCount> CountAllPatterns(
        CountPatternRequest request, string content, RegexOptions options, int maxMatchesReturned)
    {
        var counts = new List<PatternCount>(request.Patterns.Count);
        foreach (var pattern in request.Patterns)
        {
            try
            {
                var regex = new Regex(pattern, options, TimeSpan.FromMilliseconds(500));
                counts.Add(BuildPatternCount(pattern, regex, content, request, maxMatchesReturned));
            }
            catch (ArgumentException ex)
            {
                counts.Add(new PatternCount { Pattern = pattern, Count = 0, Error = ex.Message });
            }
            catch (RegexMatchTimeoutException ex)
            {
                counts.Add(new PatternCount { Pattern = pattern, Count = 0, Error = ex.Message });
            }
        }
        return counts;
    }

    /// <summary>
    /// Computes a single pattern's count + optional match list. Honors the request flags
    /// (DistinctMatches collapses duplicates and sorts; IncludeMatches populates the
    /// Matches list capped by the request cap).
    /// </summary>
    private static PatternCount BuildPatternCount(
        string pattern,
        Regex regex,
        string content,
        CountPatternRequest request,
        int maxMatchesReturned)
    {
        var matches = regex.Matches(content);
        var rawCount = matches.Count;

        if (!request.DistinctMatches && !request.IncludeMatches)
        {
            return new PatternCount { Pattern = pattern, Count = rawCount };
        }

        // Collect matched substrings (kept lazily — small docs are the typical case).
        var matchedValues = new List<string>(rawCount);
        foreach (Match m in matches) matchedValues.Add(m.Value);

        int reportedCount;
        List<string>? reportedMatches = null;

        if (request.DistinctMatches)
        {
            var uniqueSorted = matchedValues
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static s => s, StringComparer.Ordinal)
                .ToList();
            reportedCount = uniqueSorted.Count;
            if (request.IncludeMatches)
            {
                reportedMatches = uniqueSorted.Count > maxMatchesReturned
                    ? uniqueSorted.Take(maxMatchesReturned).ToList()
                    : uniqueSorted;
            }
        }
        else
        {
            reportedCount = rawCount;
            // IncludeMatches=true && DistinctMatches=false → document order
            reportedMatches = matchedValues.Count > maxMatchesReturned
                ? matchedValues.Take(maxMatchesReturned).ToList()
                : matchedValues;
        }

        return new PatternCount
        {
            Pattern = pattern,
            Count = reportedCount,
            Matches = reportedMatches,
        };
    }
}
