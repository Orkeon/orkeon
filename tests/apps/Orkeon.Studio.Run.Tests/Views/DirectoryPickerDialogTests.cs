using Orkeon.Studio.Run.Views;
using Terminal.Gui.Views;

namespace Orkeon.Studio.Run.Tests.Views;

/// <summary>
/// The folder browser, built without a terminal over a real temporary directory: it lists
/// the entries where it stands, walks into one on Enter and returns where it stands on Use.
/// </summary>
public sealed class DirectoryPickerDialogTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("orkeon-run-picker-");

    public DirectoryPickerDialogTests()
    {
        _root.CreateSubdirectory("alpha");
        _root.CreateSubdirectory("beta");
    }

    public void Dispose() => _root.Delete(recursive: true);

    [Fact]
    public void ShouldShowTheStartFolderAndItsEntries_WhenBuilt()
    {
        using var dialog = new DirectoryPickerDialog(_root.FullName);

        Assert.Contains(ViewTree.All<Label>(dialog), l => l.Text == _root.FullName);
        var rows = ViewTree.Rows(ViewTree.One<ListView>(dialog));
        Assert.Contains(rows, r => r.Contains("alpha", StringComparison.Ordinal));
        Assert.Contains(rows, r => r.Contains("beta", StringComparison.Ordinal));
        Assert.Equal(0, ViewTree.One<ListView>(dialog).SelectedItem);
    }

    [Fact]
    public void ShouldReturnTheCurrentFolder_WhenUseIsPressed()
    {
        using var dialog = new DirectoryPickerDialog(_root.FullName);

        ViewTree.Press(ViewTree.Button(dialog, "Use this folder"));

        Assert.Equal(_root.FullName, dialog.SelectedPath);
    }

    [Fact]
    public void ShouldWalkIntoTheHighlightedEntry_WhenTheListIsAccepted()
    {
        using var dialog = new DirectoryPickerDialog(_root.FullName);
        var list = ViewTree.One<ListView>(dialog);
        var rows = ViewTree.Rows(list);
        list.SelectedItem = rows.FindIndex(r => r.Contains("beta", StringComparison.Ordinal));

        list.InvokeCommand(Terminal.Gui.Input.Command.Accept);
        ViewTree.Press(ViewTree.Button(dialog, "Use this folder"));

        Assert.Equal(Path.Combine(_root.FullName, "beta"), dialog.SelectedPath);
    }
}
