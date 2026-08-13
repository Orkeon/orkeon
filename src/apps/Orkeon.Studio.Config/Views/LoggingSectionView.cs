using Orkeon.Studio.Config.Presentation;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Orkeon.Studio.Config.Views;

/// <summary>
/// The <c>Logging:LogLevel</c> screen: the <c>Default</c> category as a closed list — it
/// alone can be left unset — and the other categories as rows, added, edited and removed
/// through <see cref="LogCategoryDialog"/>. Categories the user does not touch are written
/// back exactly as they were read.
/// </summary>
internal sealed class LoggingSectionView : SectionView
{
    private readonly LoggingForm _form;
    private readonly ListView _level;
    private readonly ListView _categories;
    private readonly Label _status;

    /// <summary>Builds the screen over <paramref name="form"/>.</summary>
    public LoggingSectionView(LoggingForm form)
        : base("Logging")
    {
        _form = form ?? throw new ArgumentNullException(nameof(form));

        _level = FormLayout.AddChoiceList(
            this, 0, LoggingForm.LevelChoices.Count, "Default level", LoggingForm.LevelChoices, _form.LevelChoiceIndex);

        var next = LoggingForm.LevelChoices.Count + 1;
        FormLayout.AddNote(this, next, "Other categories configured in this file:");
        _categories = new ListView
        {
            X = FormLayout.Margin,
            Y = next + 1,
            Width = Dim.Fill(FormLayout.Margin),
            Height = Dim.Fill(4),
        };
        Add(_categories);

        var add = new Button { X = FormLayout.Margin, Y = Pos.AnchorEnd(3), Text = "Add" };
        var edit = new Button { X = Pos.Right(add) + 2, Y = Pos.AnchorEnd(3), Text = "Edit" };
        var remove = new Button { X = Pos.Right(edit) + 2, Y = Pos.AnchorEnd(3), Text = "Remove" };

        add.Accepting += (_, _) => AddCategory();
        edit.Accepting += (_, _) => EditSelected();
        remove.Accepting += (_, _) => RemoveSelected();

        _status = new Label { X = FormLayout.Margin, Y = Pos.AnchorEnd(1), Width = Dim.Fill(FormLayout.Margin) };

        Add(add, edit, remove, _status);
        Load();
    }

    /// <inheritdoc />
    public override void Load()
    {
        FormLayout.SetItems(_level, LoggingForm.LevelChoices, _form.LevelChoiceIndex);
        FormLayout.SetItems(_categories, CategoryLines());
    }

    /// <summary>Only the default level is read back: the rows are written as they are edited.</summary>
    public override void Apply() => _form.SelectLevel(FormLayout.SelectedIndex(_level));

    private List<string> CategoryLines() =>
        _form.Categories.Count > 0
            ? _form.Categories.Select(row => row.Display).ToList()
            : ["(no other category configured)"];

    private void AddCategory()
    {
        using var dialog = new LogCategoryDialog(
            level: LoggingForm.NewCategoryLevel,
            validate: (category, level) => _form.ValidateCategory(category, level));

        if (dialog.Show() is not { } row)
            return;

        _status.Text = _form.TryAddCategory(row.Category, row.Level, out var error) ? "" : error;
        Load();
    }

    private void EditSelected()
    {
        var index = FormLayout.SelectedIndex(_categories);
        if (index < 0 || index >= _form.Categories.Count)
            return;

        var current = _form.Categories[index];
        using var dialog = new LogCategoryDialog(
            current.Category,
            current.Level,
            (category, level) => _form.ValidateCategory(category, level, index));

        if (dialog.Show() is not { } row)
            return;

        _status.Text = _form.TryReplaceCategory(index, row.Category, row.Level, out var error) ? "" : error;
        Load();
    }

    private void RemoveSelected()
    {
        var index = FormLayout.SelectedIndex(_categories);
        if (index < 0 || index >= _form.Categories.Count)
            return;

        _form.RemoveCategoryAt(index);
        _status.Text = "";
        Load();
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _level.Dispose();
            _categories.Dispose();
            _status.Dispose();
        }

        base.Dispose(disposing);
    }
}
