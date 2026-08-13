using Orkeon.Studio.Config.Presentation;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Orkeon.Studio.Config.Views;

/// <summary>
/// The whole document as it would be written — read-only, and deliberately complete: the
/// keys Studio has no form for are here, in place, which is how the user can see they
/// survive editing and saving.
/// </summary>
internal sealed class RawJsonSectionView : SectionView
{
    private readonly ConfigEditorModel _model;
    private readonly TextView _json;

    /// <summary>Builds the screen over the session.</summary>
    public RawJsonSectionView(ConfigEditorModel model)
        : base("Raw JSON (read-only)")
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));

        _json = new TextView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            ReadOnly = true,
            WordWrap = false,
        };

        Add(_json);
        Load();
    }

    /// <inheritdoc />
    public override void Load() => _json.Text = _model.RawJson;

    /// <summary>The view is read-only, so there is nothing to write back.</summary>
    public override void Apply()
    {
        // Intentionally empty: editing happens in the typed screens; this one only shows
        // what the document currently holds, unknown keys included.
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _json.Dispose();

        base.Dispose(disposing);
    }
}
