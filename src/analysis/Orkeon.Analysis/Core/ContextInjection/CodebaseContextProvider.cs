using System.Globalization;
using System.Text;
using System.Text.Json;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Queries;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Abstractions.Models;

namespace Orkeon.Analysis.Core.ContextInjection;

public sealed class CodebaseContextProvider : ICodebaseContextProvider
{
    private const int MaxScanNodes = 1000;

    private static readonly JsonSerializerOptions CompactJsonOptions = new()
    {
        WriteIndented = false,
    };

    private readonly IRaggableStore _store;

    public CodebaseContextProvider(IRaggableStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public Task<string> GetContextAsync(CodebaseContextOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);
        return GetContextCoreAsync(options, ct);
    }

    private async Task<string> GetContextCoreAsync(CodebaseContextOptions options, CancellationToken ct)
    {
        var totalNodes = await _store.GetNodeCountAsync(ct).ConfigureAwait(false);
        if (totalNodes == 0) return string.Empty;

        var topN = Math.Clamp(options.TopN, 1, 50);

        var packages = await _store.QueryAsync(
            new NodeQuery { Level = NodeLevel.L1_Package, Take = MaxScanNodes }, ct)
            .ConfigureAwait(false);
        var modules = await _store.QueryAsync(
            new NodeQuery { Level = NodeLevel.L2_Module, Take = MaxScanNodes }, ct)
            .ConfigureAwait(false);
        var symbols = await _store.QueryAsync(
            new NodeQuery { Level = NodeLevel.L3_Symbol, Take = MaxScanNodes }, ct)
            .ConfigureAwait(false);

        var filesByLanguage = modules
            .GroupBy(m => string.IsNullOrEmpty(m.Language) ? "unknown" : m.Language, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var topComplexity = await _store.TopComplexityAsync(
            new ComplexityQuery { Metric = ComplexityMetric.Cyclomatic, TopN = topN }, ct)
            .ConfigureAwait(false);

        var topCentrality = await _store.TopCentralityAsync(
            new CentralityQuery
            {
                Scope = NodeLevel.L3_Symbol,
                Metric = CentralityMetric.InDegreeCalls,
                TopN = topN,
            }, ct).ConfigureAwait(false);

        var patterns = options.IncludePatterns
            ? CollectPatternTags(symbols, topN)
            : [];

        var snapshot = new CodebaseSnapshot(
            packages.Count,
            modules.Count,
            symbols.Count,
            filesByLanguage,
            topComplexity.Select(c => ShortFqn(c.Fqn)).ToArray(),
            topCentrality.Select(c => ShortFqn(c.Fqn)).ToArray(),
            patterns);

#pragma warning disable CA1308 // lowercase is the normalized form matched by the switch arms (format keys), not a comparison normalization
        return options.Format.ToLowerInvariant() switch
        {
#pragma warning restore CA1308
            "json" => RenderJson(snapshot),
            "compact" => RenderCompact(snapshot),
            _ => RenderMarkdown(snapshot),
        };
    }

    private static IReadOnlyList<string> CollectPatternTags(
        IEnumerable<RaggableNode> symbols, int topN)
    {
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in symbols)
        {
            foreach (var tag in node.Tags.Keys)
            {
                if (!tag.StartsWith("pattern-", StringComparison.OrdinalIgnoreCase)) continue;
                var name = tag["pattern-".Length..];
                if (string.IsNullOrEmpty(name)) continue;
                seen[name] = seen.TryGetValue(name, out var n) ? n + 1 : 1;
            }
        }
        return [.. seen
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Take(topN)
            .Select(kv => kv.Key)];
    }

    private static string ShortFqn(string fqn)
    {
        if (string.IsNullOrEmpty(fqn)) return string.Empty;
        var idx = fqn.LastIndexOf("::", StringComparison.Ordinal);
        if (idx >= 0 && idx + 2 < fqn.Length) return fqn[(idx + 2)..];
        var dot = fqn.LastIndexOf('.');
        if (dot >= 0 && dot + 1 < fqn.Length) return fqn[(dot + 1)..];
        return fqn;
    }

    private static string RenderMarkdown(CodebaseSnapshot s)
    {
        var sb = new StringBuilder();
        sb.AppendLine("CONTEXTE CODEBASE (généré automatiquement par RaggableTree) :");
        sb.Append(s.PackageCount).Append(" packages, ");
        sb.Append(FormatFileBreakdown(s.FilesByLanguage, s.ModuleCount)).Append(", ");
        sb.Append(s.SymbolCount).AppendLine(" symboles");

        if (s.TopComplexity.Count > 0)
        {
            sb.Append("Top-").Append(s.TopComplexity.Count)
                .Append(" fichiers par complexité : ")
                .AppendLine(string.Join(", ", s.TopComplexity));
        }
        if (s.TopCentrality.Count > 0)
        {
            sb.Append("Top-").Append(s.TopCentrality.Count)
                .Append(" symboles par couplage : ")
                .AppendLine(string.Join(", ", s.TopCentrality));
        }
        if (s.Patterns.Count > 0)
        {
            sb.Append("Patterns détectés : ")
                .AppendLine(string.Join(", ", s.Patterns));
        }
        return sb.ToString().TrimEnd();
    }

    private static string RenderCompact(CodebaseSnapshot s)
    {
        var sb = new StringBuilder();
        sb.Append("codebase=").Append(s.PackageCount).Append("p/")
            .Append(s.ModuleCount).Append("f/")
            .Append(s.SymbolCount).Append('s');
        if (s.TopComplexity.Count > 0)
        {
            sb.Append(" cx=").Append(string.Join(",", s.TopComplexity));
        }
        if (s.TopCentrality.Count > 0)
        {
            sb.Append(" cpl=").Append(string.Join(",", s.TopCentrality));
        }
        if (s.Patterns.Count > 0)
        {
            sb.Append(" pat=").Append(string.Join(",", s.Patterns));
        }
        return sb.ToString();
    }

    private static string RenderJson(CodebaseSnapshot s)
    {
        var payload = new
        {
            packages = s.PackageCount,
            files = s.ModuleCount,
            symbols = s.SymbolCount,
            filesByLanguage = s.FilesByLanguage,
            topComplexity = s.TopComplexity,
            topCentrality = s.TopCentrality,
            patterns = s.Patterns,
        };
        return JsonSerializer.Serialize(payload, CompactJsonOptions);
    }

    private static string FormatFileBreakdown(IReadOnlyDictionary<string, int> byLanguage, int total)
    {
        if (byLanguage.Count == 0) return $"{total} fichiers";
        if (byLanguage.Count == 1)
        {
            var only = byLanguage.First();
            return string.Format(CultureInfo.InvariantCulture, "{0} fichiers .{1}", only.Value, only.Key);
        }
        var parts = byLanguage
            .OrderByDescending(kv => kv.Value)
            .Select(kv => string.Format(CultureInfo.InvariantCulture, "{0} .{1}", kv.Value, kv.Key));
        return $"{total} fichiers ({string.Join(", ", parts)})";
    }

    private sealed record CodebaseSnapshot(
        int PackageCount,
        int ModuleCount,
        int SymbolCount,
        IReadOnlyDictionary<string, int> FilesByLanguage,
        IReadOnlyList<string> TopComplexity,
        IReadOnlyList<string> TopCentrality,
        IReadOnlyList<string> Patterns);
}
