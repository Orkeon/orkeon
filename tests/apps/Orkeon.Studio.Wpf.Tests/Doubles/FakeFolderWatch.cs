using Orkeon.Studio.Core.FileSystem;

namespace Orkeon.Studio.Wpf.Tests.Doubles;

/// <summary>An <see cref="IFolderWatch"/> over a table of counts: the test drops a file by raising the change.</summary>
public sealed class FakeFolderWatch : IFolderWatch
{
    private readonly Dictionary<string, List<Action>> _watchers = new(StringComparer.Ordinal);

    /// <summary>The files each directory holds; a directory absent from it holds none.</summary>
    public Dictionary<string, int> Counts { get; } = new(StringComparer.Ordinal);

    /// <summary>The directories watched right now.</summary>
    public IEnumerable<string> Watched => _watchers.Where(pair => pair.Value.Count > 0).Select(pair => pair.Key);

    public int CountFiles(string directory) => Counts.GetValueOrDefault(directory);

    public IDisposable Watch(string directory, Action changed)
    {
        if (!_watchers.TryGetValue(directory, out var list))
            _watchers[directory] = list = [];
        list.Add(changed);
        return new Handle(() => list.Remove(changed));
    }

    /// <summary>Sets the count of <paramref name="directory"/> and tells whoever watches it.</summary>
    public void Drop(string directory, int count)
    {
        Counts[directory] = count;
        if (_watchers.TryGetValue(directory, out var list))
        {
            foreach (var changed in list.ToList())
                changed();
        }
    }

    private sealed class Handle(Action release) : IDisposable
    {
        public void Dispose() => release();
    }
}
