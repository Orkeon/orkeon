namespace Orkeon.Studio.Wpf.ViewModels.Mvvm;

/// <summary>
/// Opens a path in the operating system's shell (Explorer). A port so ViewModels stay
/// process-free and tests observe what would have been opened.
/// </summary>
public interface IShellOpener
{
    /// <summary>Opens <paramref name="path"/> with the shell; silently ignores failures.</summary>
    void Open(string path);
}
