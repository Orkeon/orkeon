using System.Collections.ObjectModel;
using Orkeon.Studio.Config.Presentation;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
// Aliased under a distinct name: the bare name `Application` binds to the enclosing
// `Orkeon.Application` namespace here, and the alias marks the Terminal.Gui call sites.
using TerminalApp = Terminal.Gui.App.Application;

namespace Orkeon.Studio.Config.Views;

/// <summary>
/// The folder browser behind the mount editor's physical path: a mount root is picked from
/// the tree, never typed blind (spec §4.5). The current directory is what a confirmation
/// returns, so a folder is chosen by standing in it.
/// </summary>
internal sealed class DirectoryPickerDialog : Window
{
    private readonly DirectoryPickerModel _model;
    private readonly Label _current;
    private readonly ListView _entries;

    /// <summary>The picked directory, or null when the dialog was cancelled.</summary>
    public string? SelectedPath { get; private set; }

    /// <summary>Builds the browser starting at <paramref name="startPath"/>.</summary>
    public DirectoryPickerDialog(string? startPath, IDirectoryLister? lister = null)
    {
        _model = new DirectoryPickerModel(startPath, lister);

        Title = "Pick a folder";
        X = Pos.Center();
        Y = Pos.Center();
        Width = Dim.Percent(80);
        Height = Dim.Percent(80);

        _current = new Label { X = 1, Y = 0, Width = Dim.Fill(1), Text = _model.CurrentPath };
        _entries = new ListView
        {
            X = 1,
            Y = 2,
            Width = Dim.Fill(1),
            Height = Dim.Fill(3),
        };
        RefreshEntries();

        // Enter walks into the highlighted entry; the button below picks where we stand.
        _entries.Accepting += (_, _) =>
        {
            _model.Enter(_entries.SelectedItem ?? -1);
            RefreshEntries();
        };

        var select = new Button { X = 1, Y = Pos.AnchorEnd(1), Text = "Use this folder", IsDefault = true };
        var cancel = new Button { X = Pos.Right(select) + 2, Y = Pos.AnchorEnd(1), Text = "Cancel" };

        select.Accepting += (_, _) =>
        {
            SelectedPath = _model.CurrentPath;
            TerminalApp.RequestStop(this);
        };
        cancel.Accepting += (_, _) => TerminalApp.RequestStop(this);

        Add(_current, _entries, select, cancel);
    }

    /// <summary>Runs the browser and returns the picked directory, or null.</summary>
    public string? Show()
    {
        TerminalApp.Run(this);
        return SelectedPath;
    }

    private void RefreshEntries()
    {
        _current.Text = _model.CurrentPath;
        _entries.SetSource(new ObservableCollection<string>(_model.Entries));
        if (_model.Entries.Count > 0)
            _entries.SelectedItem = 0;
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _current.Dispose();
            _entries.Dispose();
        }

        base.Dispose(disposing);
    }
}
