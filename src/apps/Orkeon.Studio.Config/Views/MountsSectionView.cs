using System.Collections.ObjectModel;
using Orkeon.Studio.Config.Presentation;
using Orkeon.Studio.Core.FileSystem;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Orkeon.Studio.Config.Views;

/// <summary>
/// The mount list screen: <c>Orkeon:FileSystem:Mounts</c> as rows, created and edited
/// through <see cref="MountDialog"/> rather than by typing the Docker-style string. An
/// entry Studio cannot parse is still listed — flagged, editable as text elsewhere, and
/// saved back untouched.
/// </summary>
internal sealed class MountsSectionView : SectionView
{
    private readonly MountEditorModel _model;
    private readonly IDirectoryProbe _directories;
    private readonly IDirectoryLister? _lister;
    private readonly ListView _rows;
    private readonly Label _status;

    /// <summary>Builds the screen over the mount list of the session.</summary>
    public MountsSectionView(MountEditorModel model, IDirectoryProbe directories, IDirectoryLister? lister = null)
        : base(MountEditorModel.SectionTitle)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _directories = directories ?? throw new ArgumentNullException(nameof(directories));
        _lister = lister;

        FormLayout.AddNote(
            this,
            0,
            "At least one mount is required: the runtime refuses to start with an empty mount list.");

        _rows = new ListView
        {
            X = FormLayout.Margin,
            Y = 2,
            Width = Dim.Fill(FormLayout.Margin),
            Height = Dim.Fill(4),
        };
        Add(_rows);

        var add = new Button { X = FormLayout.Margin, Y = Pos.AnchorEnd(3), Text = "Add" };
        var edit = new Button { X = Pos.Right(add) + 2, Y = Pos.AnchorEnd(3), Text = "Edit" };
        var remove = new Button { X = Pos.Right(edit) + 2, Y = Pos.AnchorEnd(3), Text = "Remove" };

        add.Accepting += (_, _) => AddMount();
        edit.Accepting += (_, _) => EditSelected();
        remove.Accepting += (_, _) => RemoveSelected();

        _status = new Label { X = FormLayout.Margin, Y = Pos.AnchorEnd(1), Width = Dim.Fill(FormLayout.Margin) };

        Add(add, edit, remove, _status);
        Load();
    }

    /// <inheritdoc />
    public override void Load()
    {
        var rows = _model.Rows;
        var items = rows.Count > 0
            ? rows.Select(row => row.Display).ToList()
            : ["(no mount declared)"];

        _rows.SetSource(new ObservableCollection<string>(items));
        _rows.SelectedItem = 0;
        RefreshStatus();
    }

    /// <summary>The mount list is edited row by row, so there is nothing to read back from widgets.</summary>
    public override void Apply()
    {
        // Intentionally empty: rows are written into the model as they are added, edited
        // or removed — there is no free text on this screen to parse back.
    }

    private void AddMount()
    {
        using var dialog = new MountDialog(new MountForm(_directories), _lister);
        if (dialog.Show() is { } definition)
        {
            _model.Add(definition);
            Load();
        }
    }

    private void EditSelected()
    {
        var rows = _model.Rows;
        var index = FormLayout.SelectedIndex(_rows);
        if (index < 0 || index >= rows.Count)
            return;

        var row = rows[index];
        if (row.Definition is null)
        {
            _status.Text = "This entry does not parse; fix it in the raw JSON view or remove it.";
            return;
        }

        using var dialog = new MountDialog(MountForm.FromDefinition(row.Definition, _directories), _lister);
        if (dialog.Show() is { } definition)
        {
            _model.Replace(index, definition);
            Load();
        }
    }

    private void RemoveSelected()
    {
        var index = FormLayout.SelectedIndex(_rows);
        if (index < 0 || index >= _model.Rows.Count)
            return;

        _model.RemoveAt(index);
        Load();
    }

    private void RefreshStatus()
    {
        var messages = _model.Validate();
        _status.Text = MessageFormatter.Summarize(messages);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _rows.Dispose();
            _status.Dispose();
        }

        base.Dispose(disposing);
    }
}
