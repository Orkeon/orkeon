namespace Orkeon.Analysis.Core.Editing;

internal sealed class NoParseValidator : IParseValidator
{
    public IReadOnlyList<string> Introduced(byte[] before, byte[] after, NodeRange replacement) => [];
}
