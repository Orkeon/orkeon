using Orkeon.Studio.Wpf.ViewModels.Mvvm;

namespace Orkeon.Studio.Wpf.Tests.Doubles;

/// <summary>An <see cref="IBrowserOpener"/> that records the addresses it was asked to open, and opens nothing.</summary>
public sealed class RecordingBrowserOpener : IBrowserOpener
{
    public List<Uri> Opened { get; } = [];

    public void Open(Uri address) => Opened.Add(address);
}
