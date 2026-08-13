namespace Orkeon.Studio.Wpf.ViewModels.Services;

/// <summary>
/// The file and folder browse dialogs, seen from a ViewModel. Spec §4.5 requires a physical mount
/// path to be picked in a browser rather than typed blind, so the ViewModel has to be able to ask
/// for one — without ever naming a WPF dialog type.
/// </summary>
public interface IPathPicker
{
    /// <summary>Asks for an existing folder. Returns <see langword="null"/> when the user cancels.</summary>
    string? PickFolder(string title, string? initialPath = null);

    /// <summary>Asks for an existing file. Returns <see langword="null"/> when the user cancels.</summary>
    /// <param name="title">The dialog caption.</param>
    /// <param name="filter">A Win32 dialog filter, e.g. <c>JSON files|*.json</c>.</param>
    /// <param name="initialPath">A path to open the dialog at; it need not exist.</param>
    string? PickFile(string title, string filter, string? initialPath = null);

    /// <summary>Asks where to write a file. Returns <see langword="null"/> when the user cancels.</summary>
    string? PickSaveFile(string title, string filter, string? suggestedPath = null);
}

/// <summary>
/// A picker that always cancels. It is the default so a ViewModel can be constructed — and asserted
/// on — without a window, and so a missing wiring degrades to "nothing happened" rather than a crash.
/// </summary>
public sealed class NullPathPicker : IPathPicker
{
    /// <summary>The shared instance; the type is stateless.</summary>
    public static NullPathPicker Instance { get; } = new();

    /// <inheritdoc />
    public string? PickFolder(string title, string? initialPath = null) => null;

    /// <inheritdoc />
    public string? PickFile(string title, string filter, string? initialPath = null) => null;

    /// <inheritdoc />
    public string? PickSaveFile(string title, string filter, string? suggestedPath = null) => null;
}
