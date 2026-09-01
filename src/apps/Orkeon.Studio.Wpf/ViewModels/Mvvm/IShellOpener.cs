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

/// <summary>
/// An opener that does nothing, mirroring <c>NullPathPicker</c>.
/// <para>
/// Not the same as passing no opener at all: the screens gate their open-the-result buttons on
/// having one, so a null opener hides the very controls a screenshot campaign is there to
/// photograph. This one keeps them on screen and opens nothing.
/// </para>
/// </summary>
public sealed class NullShellOpener : IShellOpener
{
    /// <summary>The shared instance; the type is stateless.</summary>
    public static NullShellOpener Instance { get; } = new();

    /// <inheritdoc />
    public void Open(string path)
    {
    }
}
