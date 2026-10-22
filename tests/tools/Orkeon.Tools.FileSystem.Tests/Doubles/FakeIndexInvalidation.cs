using Orkeon.Analysis.Abstractions.Interfaces;

namespace Orkeon.Tools.FileSystem.Tests.Doubles;

/// <summary>Records every path marked dirty.</summary>
public sealed class FakeIndexInvalidation : IIndexInvalidation
{
    public List<string> Marked { get; } = [];
    public IReadOnlyCollection<string> DirtyPaths => Marked;
    public void MarkDirty(string virtualPath) => Marked.Add(virtualPath);
}
