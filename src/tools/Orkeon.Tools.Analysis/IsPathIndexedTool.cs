using Microsoft.Extensions.Logging;
using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Tools.Analysis;

public sealed class IsPathIndexedTool : ToolBase<IsPathIndexedRequest, IsPathIndexedResponse>
{
    private readonly IRaggableStore _store;

    public IsPathIndexedTool(IRaggableStore store, ILogger<IsPathIndexedTool>? logger = null) : base(logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public override string Name => "is_path_indexed";
    public override string Description => "Check whether a virtual path is covered by any indexed root (longest match wins).";

    /// <summary>Declared access class for permission gates.</summary>
    public override ToolAccess Access => ToolAccess.Read;

    protected override Task<IsPathIndexedResponse> ExecuteTypedAsync(IsPathIndexedRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrEmpty(request.VirtualPath))
        {
            return Task.FromResult(new IsPathIndexedResponse { Indexed = false });
        }

        var target = request.VirtualPath.TrimEnd('/');
        IndexedRoot? best = null;
        var bestLen = -1;

        foreach (var root in _store.GetIndexedRoots())
        {
            var prefix = root.VirtualRoot.TrimEnd('/');
            if (string.IsNullOrEmpty(prefix)) continue;
            var isMatch = target.Equals(prefix, StringComparison.Ordinal)
                || target.StartsWith(prefix + "/", StringComparison.Ordinal);
            if (!isMatch) continue;
            if (prefix.Length > bestLen)
            {
                bestLen = prefix.Length;
                best = root;
            }
        }

        if (best is null)
        {
            return Task.FromResult(new IsPathIndexedResponse { Indexed = false });
        }

        return Task.FromResult(new IsPathIndexedResponse
        {
            Indexed = true,
            RootParent = best.VirtualRoot,
            IndexedAt = best.IndexedAt,
        });
    }
}
