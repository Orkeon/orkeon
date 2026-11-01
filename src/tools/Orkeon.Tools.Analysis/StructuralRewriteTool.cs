using System.Collections.Immutable;
using System.Text;
using System.Text.Json.Serialization;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Core.Editing;
using Orkeon.Analysis.Core.Structural;
using Orkeon.Analysis.TreeSitter;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Tools.Analysis;

public sealed record StructuralRewriteRequest
{
    [FieldSchema(Description = "Structural rule object, as structural_search takes it. Give exactly one of pattern, rule or rule_file.", IsRequired = false)]
    [JsonPropertyName("rule")] public System.Text.Json.JsonElement? Rule { get; init; }
    [FieldSchema(Description = "Virtual path of a YAML rule file (id, language, rule, fix) under a mounted root.", IsRequired = false, Example = "/workspace/rules/no-log.yml")]
    [JsonPropertyName("rule_file")] public string? RuleFile { get; init; }
    [FieldSchema(Description = "Code pattern with $NAME / $$$NAME metavariables, as structural_search takes it.", IsRequired = false, Example = "Log.Write($X)")]
    [JsonPropertyName("pattern")] public string? Pattern { get; init; }
    [FieldSchema(Description = "Language of the pattern and of the files rewritten (csharp, typescript, python...). Required with pattern or rule; a rule_file names its own.", IsRequired = false, Example = "csharp")]
    [JsonPropertyName("language")] public string Language { get; init; } = "";
    [FieldSchema(Description = "Replacement template; $NAME and $$$NAME are substituted by their captures. Required with pattern or rule; a rule_file carries its own.", IsRequired = false, Example = "Logger.Info($X)")]
    [JsonPropertyName("fix")] public string? Fix { get; init; }
    [FieldSchema(Description = "Virtual paths of the files or folders rewritten; empty takes the mounted roots.", ItemsType = "string", IsRequired = false)]
    [JsonPropertyName("paths")] public ImmutableList<string> Paths { get; init; } = ImmutableList<string>.Empty;
    [FieldSchema(Description = "Glob kept among the candidate files (**/*.cs).", IsRequired = false)]
    [JsonPropertyName("glob")] public string? Glob { get; init; }
    [FieldSchema(Description = "true (default) returns the diffs without writing; false writes the files.", IsRequired = false, Default = true)]
    [JsonPropertyName("dry_run")] public bool DryRun { get; init; } = true;
    [FieldSchema(Description = "Most files scanned (default 500); truncated says when more were candidates.", IsRequired = false, Default = 500)]
    [JsonPropertyName("max_files")] public int MaxFiles { get; init; } = 500;
}

public sealed record StructuralRewriteFileDto
{
    [JsonPropertyName("path")] public required string Path { get; init; }
    [JsonPropertyName("matches")] public required int Matches { get; init; }
    [JsonPropertyName("applied")] public required int Applied { get; init; }
    [JsonPropertyName("diff")] public required string Diff { get; init; }
    [JsonPropertyName("refused_reason")] public string? RefusedReason { get; init; }
    [JsonPropertyName("skipped")] public ImmutableList<string> Skipped { get; init; } = ImmutableList<string>.Empty;
}

public sealed record StructuralRewriteResponse
{
    [JsonPropertyName("truncated")] public required bool Truncated { get; init; }
    [JsonPropertyName("files_scanned")] public required int FilesScanned { get; init; }
    [JsonPropertyName("files")] public required ImmutableList<StructuralRewriteFileDto> Files { get; init; }
}

public sealed class StructuralRewriteTool : FileToolBase<StructuralRewriteRequest, StructuralRewriteResponse>
{
    private readonly TreeSitterParserPool _pool;
    private readonly IIndexInvalidation? _indexInvalidation;
    private readonly StructuralEditEngine _engine;

    public StructuralRewriteTool(IFileSystemService fs, TreeSitterParserPool pool, IIndexInvalidation? indexInvalidation = null)
        : base(fs)
    {
        _pool = pool;
        _indexInvalidation = indexInvalidation;
        _engine = new StructuralEditEngine(fs, pool);
    }

    public override string Name => "structural_rewrite";
    public override string Description => "Rewrites every node a pattern or rule matches with its fix template, over the files of one language: a dry run by default, returning the per-file diff; dry_run false writes the files. A file whose rewrite no longer parses is refused alone; at most max_files files (500), truncated when cut. To preview the matches first, structural_search; for one replacement by text, edit_file.";
    public override ToolAccess Access => ToolAccess.Edit;

    protected override string? ValidateTypedRequest(StructuralRewriteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return StructuralSearchTool.PathsOutsideRoot(_fileSystemService, request.Paths);
    }

    protected override async Task<StructuralRewriteResponse> ExecuteTypedAsync(StructuralRewriteRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var rule = await StructuralSearchTool.RuleOfAsync(
            _fileSystemService, request.Pattern, request.Rule, request.RuleFile, request.Language, cancellationToken).ConfigureAwait(false);
        if (LanguageRegistry.Resolve(rule.Language) == StructuralFile.MarkdownLanguage)
            throw new ArgumentException("structural_rewrite rewrites source files only: markdown rules are refused.");
        var served = StructuralSearchTool.ServedLanguages.Where(l => l != StructuralFile.MarkdownLanguage);
        var grammar = StructuralSearchTool.GrammarFor(rule.Language)
            ?? throw new ArgumentException($"Unsupported language '{rule.Language}': structural_rewrite serves {string.Join(", ", served)}.");

        var fixText = request.Fix ?? rule.Fix
            ?? throw new ArgumentException("No 'fix' given: pass 'fix' or use a rule that carries one.");
        var fix = new FixTemplate(fixText);
        fix.Validate(rule.MetavariableNames);

        var matcher = new Matcher(_pool);
        var files = new List<StructuralRewriteFileDto>();
        var scanned = 0;
        var truncated = false;

        await foreach (var path in StructuralSearchTool.CandidateFilesAsync(_fileSystemService, request.Paths, request.Glob, grammar, cancellationToken).ConfigureAwait(false))
        {
            if (scanned >= request.MaxFiles)
            {
                truncated = true;
                break;
            }

            var dto = await RewriteFileAsync(path, matcher, rule, fix, grammar.Language, request.DryRun, cancellationToken).ConfigureAwait(false);
            if (dto is null) continue;
            scanned++;
            if (dto.Matches > 0 || dto.RefusedReason is not null) files.Add(dto);
        }

        return new StructuralRewriteResponse { Files = files.ToImmutableList(), Truncated = truncated, FilesScanned = scanned };
    }

    private async Task<StructuralRewriteFileDto?> RewriteFileAsync(
        string path, Matcher matcher, StructuralRule rule, FixTemplate fix, string language, bool dryRun, CancellationToken cancellationToken)
    {
        var rights = ResolveVirtualPath(path, FileAccessRights.Write);
        if (!rights.IsAllowed) return Refused(path, 0, rights.DenialReason ?? "Write access denied.");

        var original = await _fileSystemService.TryReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        if (original is null) return null;
        var matches = matcher.Match(path, original, rule);
        if (matches.Count == 0) return new StructuralRewriteFileDto { Path = path, Matches = 0, Applied = 0, Diff = "" };

        var outcome = _engine.Rewrite(original, matches, fix);
        var introduced = ParseErrorsIntroduced(original, outcome.Bytes, language);
        if (introduced.Count > 0)
            return Refused(path, matches.Count, "Rewrite would introduce parse errors: " + string.Join("; ", introduced));

        var skipped = outcome.Skipped.Select(s => $"{s.Match.Line}:{s.Match.Column} {s.Reason}").ToImmutableList();
        var diff = UnifiedDiff.Write(path, Encoding.UTF8.GetString(original), Encoding.UTF8.GetString(outcome.Bytes));
        if (dryRun)
            return new StructuralRewriteFileDto { Path = path, Matches = matches.Count, Applied = 0, Diff = diff, Skipped = skipped };

        try
        {
            await _engine.WriteAsync(path, outcome.Bytes, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or AggregateException)
        {
            return Refused(path, matches.Count, "Write failed: " + ex.Message);
        }

        _indexInvalidation?.MarkDirty(path);
        return new StructuralRewriteFileDto { Path = path, Matches = matches.Count, Applied = outcome.Applied, Diff = diff, Skipped = skipped };
    }

    private IReadOnlyList<string> ParseErrorsIntroduced(byte[] before, byte[] after, string language)
    {
        var prefix = 0;
        while (prefix < before.Length && prefix < after.Length && before[prefix] == after[prefix]) prefix++;
        var suffix = 0;
        while (suffix < before.Length - prefix && suffix < after.Length - prefix && before[^(suffix + 1)] == after[^(suffix + 1)]) suffix++;
        var changed = new NodeRange(prefix, after.Length - suffix, 0, 0);
        return new TreeSitterParseValidator(_pool, language).Introduced(before, after, changed);
    }

    private static StructuralRewriteFileDto Refused(string path, int matches, string reason)
        => new() { Path = path, Matches = matches, Applied = 0, Diff = "", RefusedReason = reason };
}
