using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.Analysis.Internal;

namespace Orkeon.Tools.Analysis;

public sealed class PackageSummaryTool : ToolBase<PackageSummaryRequest, PackageSummaryResponse>
{
    private static readonly HashSet<string> EntryPointNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "main", "__main__", "program", "index",
    };

    private readonly IRaggableStore _store;

    public PackageSummaryTool(IRaggableStore store, ILogger<PackageSummaryTool>? logger = null) : base(logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public override string Name => "package_summary";
    public override string Description => "Details about a package (L1): file count, symbol count, exports, dependencies.";

    /// <summary>Declared access class for permission gates.</summary>
    public override ToolAccess Access => ToolAccess.Read;

    protected override Task<PackageSummaryResponse> ExecuteTypedAsync(PackageSummaryRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteTypedCoreAsync();

        async Task<PackageSummaryResponse> ExecuteTypedCoreAsync()
        {
        var package = await _store.GetAsync(request.Fqn, cancellationToken).ConfigureAwait(false);
        if (package is null) throw await FqnSuggestions.BuildAsync(_store, request.Fqn, cancellationToken).ConfigureAwait(false);

        var children = await _store.GetChildrenAsync(package.Id, cancellationToken).ConfigureAwait(false);
        var modules = children.Where(c => c.Level == NodeLevel.L2_Module).ToList();
        var symbols = new List<RaggableNode>();
        foreach (var module in modules)
        {
            var moduleSymbols = await _store.GetChildrenAsync(module.Id, cancellationToken).ConfigureAwait(false);
            symbols.AddRange(moduleSymbols.Where(c => c.Level == NodeLevel.L3_Symbol));
        }

        var exports = ImmutableArray<string>.Empty;
        var truncated = false;
        if (request.IncludePublicExports)
        {
            var publics = symbols
                .Where(s => s.Modifiers.Contains("public") || s.EffectiveKind is UniversalNodeKind.Class
                    or UniversalNodeKind.Interface or UniversalNodeKind.Function or UniversalNodeKind.Method)
                .Select(s => s.Fqn)
                .ToList();
            if (publics.Count > request.MaxExports)
            {
                truncated = true;
                publics = publics.Take(request.MaxExports).ToList();
            }
            exports = [.. publics];
        }

        var deps = ImmutableArray<string>.Empty;
        if (request.IncludeDependencies)
        {
            deps = await CollectDependenciesAsync(package, modules, cancellationToken).ConfigureAwait(false);
        }

        var entryPoints = modules
            .Where(m => EntryPointNames.Contains(Path.GetFileNameWithoutExtension(m.Name)))
            .Select(m => m.Fqn)
            .ToImmutableArray();

        return new PackageSummaryResponse
        {
            Fqn = package.Fqn,
            Name = package.Name,
            FileCount = modules.Count,
            SymbolCount = symbols.Count,
            EntryPoints = entryPoints,
            PublicExports = exports,
            Dependencies = deps,
            SummaryShort = package.SemanticSummary,
            Truncated = truncated,
        };
        }
    }

    // The index puts Imports edges between modules, never on a package: a package depends on the
    // packages holding the targets of its modules' imports. External targets have no package.
    private async Task<ImmutableArray<string>> CollectDependenciesAsync(
        RaggableNode package, IReadOnlyList<RaggableNode> modules, CancellationToken cancellationToken)
    {
        var targetIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var module in modules)
        {
            var edges = await _store.GetEdgesAsync(module.Id, EdgeKind.Imports, Direction.Forward, cancellationToken).ConfigureAwait(false);
            foreach (var edge in edges) targetIds.Add(edge.ToId);
        }
        if (targetIds.Count == 0) return [];

        var targets = await _store.GetManyAsync(targetIds, cancellationToken).ConfigureAwait(false);
        var parentIds = targets
            .Select(t => t.ParentId)
            .Where(id => !string.IsNullOrEmpty(id) && id != package.Id)
            .Select(id => id!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var parents = await _store.GetManyAsync(parentIds, cancellationToken).ConfigureAwait(false);
        return [.. parents
            .Where(p => p.Level == NodeLevel.L1_Package)
            .Select(p => p.Fqn)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
    }
}
