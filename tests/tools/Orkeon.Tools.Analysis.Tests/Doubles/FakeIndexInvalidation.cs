using Orkeon.Analysis.Abstractions.Interfaces;

namespace Orkeon.Tools.Analysis.Tests.Doubles;

/// <summary>Hand-written fake of <see cref="IIndexInvalidation"/> recording the dirty paths.</summary>
public sealed class FakeIndexInvalidation : IIndexInvalidation
{
    private readonly List<string> _dirty = [];

    public IReadOnlyCollection<string> DirtyPaths => _dirty;

    public void MarkDirty(string virtualPath) => _dirty.Add(virtualPath);
}
