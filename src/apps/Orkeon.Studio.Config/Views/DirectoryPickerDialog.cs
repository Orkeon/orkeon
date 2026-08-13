using System.Collections.ObjectModel;
using Orkeon.Studio.Core.FileSystem;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
// Aliased under a distinct name: the bare name `Application` binds to the enclosing
// `Orkeon.Application` namespace here, and the alias marks the Terminal.Gui call sites.
using TerminalApp = Terminal.Gui.App.Application;

namespace Orkeon.Studio.Config.Views;

/// <summary>
/// The browser behind the mount editor's physical path and the "Open…" action: a mount root
/// is picked from the tree, never typed blind (spec §4.5). Browsing itself is
/// <see cref="DirectoryBrowser"/>'s — this is the rendering only.
/// <para>
/// With no file pattern the current directory is what a confirmation returns, so a folder is
/// chosen by standing in it. Given one, the matching files are listed after the folders and
/// selecting one returns the file: that is how a settings file that is not named
/// <c>appsettings.json</c> can be opened at all.
/// </para>
/// </summary>
internal sealed class DirectoryPickerDialog : Window
{
    private readonly DirectoryBrowser _browser;
    private readonly Label _current;
    private readonly ListView _entries;

    /// <summary>The picked directory or file, or null when the dialog was cancelled.</summary>
    public string? SelectedPath { get; private set; }

    /// <summary>Builds the browser starting at <paramref name="startPath"/>.</summary>
    /// <param name="startPath">Where to start; ignored when it names nothing that exists.</param>
    /// <param name="lister">Directory access (defaults to the real disk).</param>
    /// <param name="fileSearchPattern">Glob of the files to offer, e.g. <c>*.json</c>; null lists folders only.</param>
    public DirectoryPickerDialog(
        string? startPath,
        IDirectoryLister? lister = null,
        string? fileSearchPattern = null)
    {
        _browser = new DirectoryBrowser(startPath, lister, fileSearchPattern);

        Title = _browser.ListsFiles ? "Pick a file" : "Pick a folder";
        X = Pos.Center();
        Y = Pos.Center();
        Width = Dim.Percent(80);
        Height = Dim.Percent(80);

        _current = new Label { X = 1, Y = 0, Width = Dim.Fill(1), Text = _browser.CurrentPath };
        _entries = new ListView
        {
            X = 1,
            Y = 2,
            Width = Dim.Fill(1),
            Height = Dim.Fill(3),
        };
        RefreshEntries();

        // Enter walks into the highlighted folder, or picks the highlighted file; the button
        // below picks where we stand.
        _entries.Accepting += (_, _) => Activate(_entries.SelectedItem ?? -1);

        var select = new Button
        {
            X = 1,
            Y = Pos.AnchorEnd(1),
            Text = _browser.ListsFiles ? "Use the selection" : "Use this folder",
            IsDefault = true,
        };
        var cancel = new Button { X = Pos.Right(select) + 2, Y = Pos.AnchorEnd(1), Text = "Cancel" };

        select.Accepting += (_, _) => Confirm(_entries.SelectedItem ?? -1);
        cancel.Accepting += (_, _) => TerminalApp.RequestStop(this);

        Add(_current, _entries, select, cancel);
    }

    /// <summary>Runs the browser and returns the picked path, or null.</summary>
    public string? Show()
    {
        TerminalApp.Run(this);
        return SelectedPath;
    }

    /// <summary>Test-only: activates the entry at <paramref name="index"/> (enter a folder, pick a file).</summary>
    internal void ActivateForTest(int index) => Activate(index);

    /// <summary>Test-only: what the list currently shows.</summary>
    internal IReadOnlyList<string> EntriesForTest => _browser.Entries;

    private void Activate(int index)
    {
        if (_browser.FilePathAt(index) is { } file)
        {
            SelectedPath = file;
            TerminalApp.RequestStop(this);
            return;
        }

        _browser.Enter(index);
        RefreshEntries();
    }

    /// <summary>
    /// The confirm button: a highlighted file wins over the current directory, so the user
    /// does not have to know that "Enter" and the button mean different things.
    /// </summary>
    private void Confirm(int index)
    {
        SelectedPath = _browser.FilePathAt(index) ?? _browser.CurrentPath;
        TerminalApp.RequestStop(this);
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
            _current.Dispose();
            _entries.Dispose();
        }

        base.Dispose(disposing);
    }
}
