using Orkeon.Studio.Config.Presentation;
using Terminal.Gui.Views;

namespace Orkeon.Studio.Config.Views;

/// <summary>
/// The <c>Logging:LogLevel</c> screen: the <c>Default</c> category as a closed list, and
/// the other configured categories listed read-only — they are preserved on save like
/// every other key Studio does not model.
/// </summary>
internal sealed class LoggingSectionView : SectionView
{
    private readonly LoggingForm _form;
    private readonly ListView _level;
    private readonly ListView _otherCategories;

    /// <summary>Builds the screen over <paramref name="form"/>.</summary>
    public LoggingSectionView(LoggingForm form)
        : base("Logging")
    {
        _form = form ?? throw new ArgumentNullException(nameof(form));

        _level = FormLayout.AddChoiceList(
            this, 0, LoggingForm.LevelChoices.Count, "Default level", LoggingForm.LevelChoices, _form.LevelChoiceIndex);

        var next = LoggingForm.LevelChoices.Count + 1;
        FormLayout.AddNote(this, next, "Other categories configured in this file (kept as they are):");
        _otherCategories = FormLayout.AddChoiceList(this, next + 1, 6, "", OtherCategoriesOrPlaceholder(), 0);
        _otherCategories.CanFocus = false;
    }

    /// <inheritdoc />
    public override void Load()
    {
        FormLayout.SetItems(_level, LoggingForm.LevelChoices, _form.LevelChoiceIndex);
        FormLayout.SetItems(_otherCategories, OtherCategoriesOrPlaceholder());
    }

    /// <inheritdoc />
    public override void Apply() => _form.SelectLevel(FormLayout.SelectedIndex(_level));

    private IReadOnlyList<string> OtherCategoriesOrPlaceholder() =>
        _form.OtherCategories.Count > 0 ? _form.OtherCategories : ["(none)"];

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _level.Dispose();
            _otherCategories.Dispose();
        }

        base.Dispose(disposing);
    }
}
