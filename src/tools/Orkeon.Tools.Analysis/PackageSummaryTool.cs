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
            var edges = await _store.GetEdgesAsync(package.Fqn, EdgeKind.Imports, Direction.Forward, cancellationToken).ConfigureAwait(false);
            deps = edges.Select(e => e.ToId).Distinct(StringComparer.Ordinal).ToImmutableArray();
        }

        var entryPoints = modules
            .Where(m => m.Name.Contains("main", StringComparison.OrdinalIgnoreCase)
                || m.Name.Contains("program", StringComparison.OrdinalIgnoreCase)
                || m.Name.Contains("index", StringComparison.OrdinalIgnoreCase))
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
}
