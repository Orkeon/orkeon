using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Orkeon.Analysis.Core.Editing;
using Orkeon.Analysis.Core.Structural;
using Orkeon.Analysis.TreeSitter;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Tools.Analysis;

public sealed record StructuralSearchRequest
{
    [FieldSchema(Description = "Code pattern with $NAME / $$$NAME metavariables. Give exactly one of pattern, rule or rule_file.", Example = "Log.Write($X)", IsRequired = false)]
    [JsonPropertyName("pattern")] public string? Pattern { get; init; }
    [FieldSchema(Description = "Structural rule object. Keys: kind, pattern, regex, field, inside, has, not, all, any, stopBy. Any other key is refused by name.", Example = "{\"kind\":\"await_expression\",\"inside\":{\"kind\":\"for_statement\",\"stopBy\":\"end\"}}", IsRequired = false)]
    [JsonPropertyName("rule")] public System.Text.Json.JsonElement? Rule { get; init; }
    [FieldSchema(Description = "Virtual path of a YAML rule file (id, language, rule, message, severity, fix) under a mounted root.", Example = "/workspace/rules/no-log.yml", IsRequired = false)]
    [JsonPropertyName("rule_file")] public string? RuleFile { get; init; }
    [JsonPropertyName("language")] public string Language { get; init; } = "";
    [JsonPropertyName("paths")] public ImmutableList<string> Paths { get; init; } = ImmutableList<string>.Empty;
    [JsonPropertyName("glob")] public string? Glob { get; init; }
    [JsonPropertyName("max_results")] public int MaxResults { get; init; } = 100;
    [JsonPropertyName("max_files")] public int MaxFiles { get; init; } = 500;
}

public sealed record StructuralMatchDto
{
    [JsonPropertyName("path")] public required string Path { get; init; }
    [JsonPropertyName("line")] public required int Line { get; init; }
    [JsonPropertyName("column")] public required int Column { get; init; }
    [JsonPropertyName("text")] public required string Text { get; init; }
    [JsonPropertyName("enclosing_symbol")] public string? EnclosingSymbol { get; init; }
    [JsonPropertyName("captures")] public required ImmutableDictionary<string, string> Captures { get; init; }
}

public sealed record StructuralSearchResponse
{
    [JsonPropertyName("matches")] public required ImmutableList<StructuralMatchDto> Matches { get; init; }
    [JsonPropertyName("truncated")] public required bool Truncated { get; init; }
    [JsonPropertyName("files_scanned")] public required int FilesScanned { get; init; }
}

public sealed class StructuralSearchTool : ToolBase<StructuralSearchRequest, StructuralSearchResponse>
{
    private static readonly HashSet<string> Served = new(StringComparer.Ordinal) { "csharp", "typescript", "markdown" };

    private readonly IFileSystemService _fs;
    private readonly TreeSitterParserPool _pool;

    public StructuralSearchTool(IFileSystemService fs, TreeSitterParserPool pool)
    {
        _fs = fs;
        _pool = pool;
    }

    public override string Name => "structural_search";
    public override string Description =>
        "Searches C#, TypeScript or Markdown files for a code pattern parsed with the target grammar: $NAME matches one node and "
        + "captures its text, $$$NAME matches zero or more sibling nodes. Returns path, line, column, text, captures and the "
        + "enclosing symbol of each match.";
    public override ToolAccess Access => ToolAccess.Read;

    protected override async Task<StructuralSearchResponse> ExecuteTypedAsync(StructuralSearchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var rule = await RuleOfAsync(request, cancellationToken).ConfigureAwait(false);
        var language = LanguageRegistry.Resolve(rule.Language);
        var grammar = Served.Contains(language) ? LanguageRegistry.Shipped.FirstOrDefault(g => g.Language == language) : null;
        if (grammar is null)
            throw new ArgumentException($"Unsupported language '{rule.Language}': structural_search serves csharp, typescript and markdown.", nameof(request));

        var matcher = new Matcher(_pool);
        var matches = new List<StructuralMatchDto>();
        var scanned = 0;
        var truncated = false;

        await foreach (var path in CandidateFilesAsync(request, grammar, cancellationToken).ConfigureAwait(false))
        {
            if (scanned >= request.MaxFiles)
            {
                truncated = true;
                break;
            }

            var bytes = await _fs.TryReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            if (bytes is null) continue;
            scanned++;
            foreach (var hit in matcher.Match(path, bytes, rule, () => StructuralFile.FromBytes(_pool, bytes, path)))
            {
                if (matches.Count >= request.MaxResults)
                {
                    truncated = true;
                    break;
                }

                matches.Add(new StructuralMatchDto
                {
                    Path = hit.Path,
                    Line = hit.Line,
                    Column = hit.Column,
                    Text = hit.Text,
                    EnclosingSymbol = hit.EnclosingSelector,
                    Captures = hit.Captures,
                });
            }

            if (truncated) break;
        }

        return new StructuralSearchResponse { Matches = matches.ToImmutableList(), Truncated = truncated, FilesScanned = scanned };
    }

    private async Task<StructuralRule> RuleOfAsync(StructuralSearchRequest request, CancellationToken cancellationToken)
    {
        var given = new[] { request.Pattern is not null, request.Rule is not null, request.RuleFile is not null }.Count(g => g);
        if (given != 1) throw new ArgumentException("Give exactly one of 'pattern', 'rule' or 'rule_file'.", nameof(request));

        if (request.RuleFile is not null)
            return await YamlRuleLoader.LoadAsync(_fs, request.RuleFile, cancellationToken).ConfigureAwait(false);
        if (request.Rule is { } inline)
        {
            if (ToObject(inline) is not Dictionary<string, object?> dictionary)
                throw new ArgumentException("'rule' takes a rule object.", nameof(request));
            return StructuralRule.FromInline(request.Language, dictionary);
        }

        return StructuralRule.FromPattern(request.Language, request.Pattern!);
    }

    private static object? ToObject(System.Text.Json.JsonElement element) => element.ValueKind switch
    {
        System.Text.Json.JsonValueKind.Object => element.EnumerateObject().ToDictionary(p => p.Name, p => ToObject(p.Value)),
        System.Text.Json.JsonValueKind.Array => element.EnumerateArray().Select(ToObject).ToList(),
        System.Text.Json.JsonValueKind.String => element.GetString(),
        _ => null,
    };

    private async IAsyncEnumerable<string> CandidateFilesAsync(
        StructuralSearchRequest request, GrammarInfo grammar, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var roots = request.Paths.Count > 0 ? request.Paths : _fs.GetAvailableMounts().Select(m => m.VirtualPath).ToImmutableList();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var options = new VirtualEnumerationOptions(SearchPattern: request.Glob);
        foreach (var root in roots)
        {
            if (IsOfLanguage(root, grammar))
            {
                if (seen.Add(root)) yield return root;
                continue;
            }

            await foreach (var entry in _fs.EnumerateFilesAsync(root, options, cancellationToken).ConfigureAwait(false))
            {
                if (entry.Kind == VirtualEntryKind.File && IsOfLanguage(entry.VirtualPath, grammar) && seen.Add(entry.VirtualPath))
                    yield return entry.VirtualPath;
            }
        }
    }

    private static bool IsOfLanguage(string path, GrammarInfo grammar)
        => grammar.Extensions.Contains(System.IO.Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
}
