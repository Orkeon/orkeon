using Orkeon.Studio.Wpf.ViewModels.Mvvm;

namespace Orkeon.Studio.Wpf.Tests.Doubles;

/// <summary>An <see cref="IShellOpener"/> that records what would have been opened.</summary>
public sealed class RecordingShellOpener : IShellOpener
{
    public List<string> Opened { get; } = [];

    public void Open(string path) => Opened.Add(path);
}
