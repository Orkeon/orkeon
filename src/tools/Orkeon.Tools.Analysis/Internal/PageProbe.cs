namespace Orkeon.Tools.Analysis.Internal;

/// <summary>
/// The size asked of a store to tell whether a page was cut: one item past the page,
/// so that <c>truncated</c> says that more items exist, never that the page is full (#283).
/// </summary>
internal static class PageProbe
{
    /// <summary>One past <paramref name="pageSize"/>, held at <see cref="int.MaxValue"/> instead of overflowing.</summary>
    public static int OnePast(int pageSize) => pageSize == int.MaxValue ? pageSize : pageSize + 1;
}
