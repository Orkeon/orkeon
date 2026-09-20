namespace Orkeon.Studio.Wpf.ViewModels.Services;

/// <summary>
/// The system clipboard, behind a seam so a view model can offer "copy the id" (VFS-90) and
/// the tests can read what was copied without a desktop.
/// </summary>
public interface IClipboardService
{
    /// <summary>Puts <paramref name="text"/> on the clipboard.</summary>
    void SetText(string text);
}

/// <summary>A clipboard that keeps the last text in memory — the tests' double, and the default when no desktop is wired.</summary>
public sealed class InMemoryClipboardService : IClipboardService
{
    /// <summary>The last text copied, or null.</summary>
    public string? LastText { get; private set; }

    /// <inheritdoc />
    public void SetText(string text) => LastText = text;
}
