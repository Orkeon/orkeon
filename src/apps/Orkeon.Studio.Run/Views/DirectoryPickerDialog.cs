using System.Collections.ObjectModel;
using Orkeon.Studio.Core.FileSystem;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using TerminalApp = Terminal.Gui.App.Application;

namespace Orkeon.Studio.Run.Views;

/// <summary>
/// The folder browser behind the launch mount form's physical path: a mount root is picked
/// from the tree, never typed blind (spec §4.5). Browsing itself is
/// <see cref="DirectoryBrowser"/>'s — this is the rendering only. The current directory is
/// what a confirmation returns, so a folder is chosen by standing in it.
/// </summary>
internal sealed class DirectoryPickerDialog : Window
{
    private readonly DirectoryBrowser _browser;
    private readonly Label _current;
    private readonly ListView _entries;

    /// <summary>Builds the browser starting at <paramref name="startPath"/>.</summary>
    internal DirectoryPickerDialog(string? startPath, IDirectoryLister? lister = null)
    {
        _browser = new DirectoryBrowser(startPath, lister);

        Title = "Pick a folder";
        X = Pos.Center();
        Y = Pos.Center();
        Width = Dim.Percent(80);
        Height = Dim.Percent(80);

        _current = new Label { X = 1, Y = 0, Width = Dim.Fill(1), Text = _browser.CurrentPath };
        _entries = new ListView { X = 1, Y = 2, Width = Dim.Fill(1), Height = Dim.Fill(3) };
        RefreshEntries();

        // Enter walks into the highlighted entry; the button below picks where we stand.
        _entries.Accepting += (_, _) =>
        {
            _browser.Enter(_entries.SelectedItem ?? -1);
            RefreshEntries();
        };

        var select = new Button { X = 1, Y = Pos.AnchorEnd(1), Text = "Use this folder", IsDefault = true };
        var cancel = new Button { X = Pos.Right(select) + 2, Y = Pos.AnchorEnd(1), Text = "Cancel" };

        select.Accepting += (_, _) =>
        {
            SelectedPath = _browser.CurrentPath;
            TerminalApp.RequestStop(this);
        };
        cancel.Accepting += (_, _) => TerminalApp.RequestStop(this);

        Add(_current, _entries, select, cancel);
    }

    /// <summary>The picked directory, or null when the dialog was cancelled.</summary>
    internal string? SelectedPath { get; private set; }

    /// <summary>Runs the browser and returns the picked directory, or null.</summary>
    internal string? Show()
    {
        TerminalApp.Run(this, errorHandler: null);
        return SelectedPath;
    }

    private void RefreshEntries()
    {
        _current.Text = _browser.CurrentPath;
        var entries = _browser.Entries;
        _entries.SetSource(new ObservableCollection<string>(entries));
        if (entries.Count > 0)
            _entries.SelectedItem = 0;
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Terminal.Gui's base View.Dispose also disposes the Add()-ed subviews; their
            // IsDisposed guard makes these explicit calls idempotent no-ops, and satisfies the
            // "owned IDisposable field" rule the repo builds with.
            _current.Dispose();
            _entries.Dispose();
        }

        base.Dispose(disposing);
    }
}
