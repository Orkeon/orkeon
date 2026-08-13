using Orkeon.Studio.Config.Presentation;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
// Aliased under a distinct name: the bare name `Application` binds to the enclosing
// `Orkeon.Application` namespace here, and the alias marks the Terminal.Gui call sites.
using TerminalApp = Terminal.Gui.App.Application;

namespace Orkeon.Studio.Config.Views;

/// <summary>
/// Creates or edits one <c>Logging:LogLevel</c> entry: a free-text category and a level
/// taken from the closed list the .NET logging stack accepts. The name is checked by the
/// form — the dialog stays open, showing why, until what it holds is acceptable.
/// </summary>
internal sealed class LogCategoryDialog : Window
{
    private readonly Func<string, string, string?>? _validate;
    private readonly TextField _category;
    private readonly ListView _level;
    private readonly Label _problem;

    /// <summary>Builds the dialog, empty for a creation and filled for an edit.</summary>
    /// <param name="category">The category name to start from.</param>
    /// <param name="level">The level to start on; unknown or null stands on the default.</param>
    /// <param name="validate">Checks a candidate row, returning why it is refused, or null.</param>
    public LogCategoryDialog(
        string category = "",
        string? level = null,
        Func<string, string, string?>? validate = null)
    {
        _validate = validate;

        Title = "Log category";
        X = Pos.Center();
        Y = Pos.Center();
        Width = Dim.Percent(70);
        Height = Dim.Percent(70);

        _category = FormLayout.AddField(this, 0, "Category", category);
        FormLayout.AddNote(
            this,
            1,
            "A logger category, e.g. Microsoft.AspNetCore. 'Default' has its own chooser on the screen.");

        _level = FormLayout.AddChoiceList(
            this,
            2,
            LoggingForm.CategoryLevelChoices.Count,
            "Level",
            LoggingForm.CategoryLevelChoices,
            LoggingForm.CategoryLevelIndex(level));

        _problem = FormLayout.AddText(this, 3 + LoggingForm.CategoryLevelChoices.Count, "");

        var accept = new Button { X = FormLayout.Margin, Y = Pos.AnchorEnd(1), Text = "OK", IsDefault = true };
        var cancel = new Button { X = Pos.Right(accept) + 2, Y = Pos.AnchorEnd(1), Text = "Cancel" };

        accept.Accepting += (_, _) => Accept();
        cancel.Accepting += (_, _) => TerminalApp.RequestStop(this);

        Add(accept, cancel);
    }

    /// <summary>The row the user accepted, or null when the dialog was cancelled.</summary>
    public LogCategoryRow? AcceptedCategory { get; private set; }

    /// <summary>Runs the dialog and returns the accepted row, or null.</summary>
    public LogCategoryRow? Show()
    {
        TerminalApp.Run(this);
        return AcceptedCategory;
    }

    private void Accept()
    {
        var category = _category.Text ?? "";
        var level = LoggingForm.CategoryLevelChoices[Math.Clamp(
            FormLayout.SelectedIndex(_level), 0, LoggingForm.CategoryLevelChoices.Count - 1)];

        if (_validate?.Invoke(category, level) is { } error)
        {
            _problem.Text = error;
            return;
        }

        AcceptedCategory = new LogCategoryRow(category.Trim(), level);
        TerminalApp.RequestStop(this);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _category.Dispose();
            _level.Dispose();
            _problem.Dispose();
        }

        base.Dispose(disposing);
    }
}
