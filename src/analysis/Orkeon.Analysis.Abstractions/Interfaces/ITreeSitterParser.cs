using System.Collections.Immutable;
using Orkeon.Analysis.Abstractions.Models;
using TreeSitter;

namespace Orkeon.Analysis.Abstractions.Interfaces;

public interface ITreeSitterParser : IDisposable
{
    ParsedTree Parse(string sourceCode, string language);
}

public sealed record ParsedTree(
    Tree Tree,
    string Language,
    ImmutableArray<ParseDiagnostic> Diagnostics) : IDisposable
{
    public Node Root => Tree.RootNode;

    public void Dispose() => Tree.Dispose();
}
