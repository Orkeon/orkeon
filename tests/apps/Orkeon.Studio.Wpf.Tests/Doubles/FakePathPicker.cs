using Orkeon.Studio.Wpf.ViewModels.Services;

namespace Orkeon.Studio.Wpf.Tests.Doubles;

/// <summary>
/// An <see cref="IPathPicker"/> that returns a queued answer instead of opening a dialog. A null
/// answer stands for the user cancelling.
/// </summary>
public sealed class FakePathPicker : IPathPicker
{
    public string? FolderToReturn { get; set; }

    public string? FileToReturn { get; set; }

    public string? SaveFileToReturn { get; set; }

    public List<string> Prompts { get; } = [];

    public string? PickFolder(string title, string? initialPath = null)
    {
        Prompts.Add(title);
        return FolderToReturn;
    }

    public string? PickFile(string title, string filter, string? initialPath = null)
    {
        Prompts.Add(title);
        return FileToReturn;
    }

    public string? PickSaveFile(string title, string filter, string? suggestedPath = null)
    {
        Prompts.Add(title);
        return SaveFileToReturn;
    }
}
