namespace Orkeon.Cli.TerminalGui.Layout;

/// <summary>What <see cref="InlinePanelView"/> paints: rows for a width and a height.</summary>
internal interface IPanelRows
{
    /// <summary>The rows for <paramref name="width"/> columns and at most <paramref name="maxRows"/> rows.</summary>
    IReadOnlyList<IReadOnlyList<RowSegment>> Rows(int width, int maxRows);
}
