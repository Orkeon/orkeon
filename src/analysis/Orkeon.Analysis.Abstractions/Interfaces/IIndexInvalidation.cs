namespace Orkeon.Analysis.Abstractions.Interfaces;

/// <summary>
/// Write-side coordination contract of the code index (PLAN B1): a component that
/// EDITS files marks them dirty here, and the freshness pass reindexes exactly those
/// paths before the next read answers.
/// </summary>
/// <remarks>
/// <para>This is the cheap half of the lazy-freshness design the user chose: marking is
/// synchronous and O(1) per write; the reindex cost is paid only when — and if —
/// someone searches. A turn that edits ten files pays one grouped reindex, not ten.</para>
/// <para>Consumers must treat a path OUTSIDE every indexed root as a no-op: there is
/// nothing stale to report about a file the index never covered. Implementations decide
/// coverage (prefix match on the indexed roots, same rule as <c>is_path_indexed</c>).</para>
/// </remarks>
public interface IIndexInvalidation
{
    /// <summary>Marks a virtual path as edited-but-not-reindexed. No-op outside indexed roots.</summary>
    void MarkDirty(string virtualPath);

    /// <summary>Paths edited since the last (re)index — what a freshness pass must cover.</summary>
    IReadOnlyCollection<string> DirtyPaths { get; }
}
