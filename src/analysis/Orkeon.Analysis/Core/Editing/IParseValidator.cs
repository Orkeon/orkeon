namespace Orkeon.Analysis.Core.Editing;

internal interface IParseValidator
{
    /// <summary>The diagnostics the splice introduced: those of <paramref name="after"/> that <paramref name="before"/> did not have.</summary>
    IReadOnlyList<string> Introduced(byte[] before, byte[] after, NodeRange replacement);
}
