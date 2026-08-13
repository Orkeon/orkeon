using Orkeon.Studio.Config.Presentation;
using Terminal.Gui.Views;

namespace Orkeon.Studio.Config.Views;

/// <summary>
/// The <c>Orkeon:Rag</c> screen. The profile comes from a closed list — the names
/// <c>RagProfilePresets</c> knows — and the web fallback is shown for what it is: two
/// switches that do nothing apart.
/// </summary>
internal sealed class RagSectionView : SectionView
{
    private readonly RagForm _form;
    private readonly ListView _profile;
    private readonly TextField _provider;
    private readonly TextField _connectionString;
    private readonly CheckBox _hybrid;
    private readonly CheckBox _correctiveWebFallback;
    private readonly CheckBox _webFallback;
    private readonly TextField _maxIterations;

    /// <summary>Builds the screen over <paramref name="form"/>.</summary>
    public RagSectionView(RagForm form)
        : base("RAG")
    {
        _form = form ?? throw new ArgumentNullException(nameof(form));

        _profile = FormLayout.AddChoiceList(
            this, 0, RagForm.ProfileChoices.Count, "Profile", RagForm.ProfileChoices, _form.ProfileChoiceIndex);

        var next = RagForm.ProfileChoices.Count + 1;
        _provider = FormLayout.AddField(this, next, "Document store provider", _form.Provider);
        _connectionString = FormLayout.AddField(this, next + 1, "Connection string", _form.ConnectionString);

        _hybrid = FormLayout.AddOptionalSwitch(
            this, next + 3, "Hybrid retrieval (BM25 + RRF)", _form.HybridRetrievalEnabled);
        _correctiveWebFallback = FormLayout.AddOptionalSwitch(
            this, next + 4, "Corrective web fallback — policy switch", _form.CorrectiveWebFallbackEnabled);
        _webFallback = FormLayout.AddOptionalSwitch(
            this, next + 5, "Web fallback — transport switch", _form.WebFallbackEnabled);
        FormLayout.AddNote(this, next + 6, RagForm.WebFallbackNotice);

        _maxIterations = FormLayout.AddField(this, next + 8, "Corrective max iterations", _form.CorrectiveMaxIterations);
    }

    /// <inheritdoc />
    public override void Load()
    {
        FormLayout.SetItems(_profile, RagForm.ProfileChoices, _form.ProfileChoiceIndex);
        _provider.Text = _form.Provider;
        _connectionString.Text = _form.ConnectionString;
        _hybrid.Value = FormLayout.ToCheckState(_form.HybridRetrievalEnabled);
        _correctiveWebFallback.Value = FormLayout.ToCheckState(_form.CorrectiveWebFallbackEnabled);
        _webFallback.Value = FormLayout.ToCheckState(_form.WebFallbackEnabled);
        _maxIterations.Text = _form.CorrectiveMaxIterations;
    }

    /// <inheritdoc />
    public override void Apply()
    {
        _form.SelectProfile(FormLayout.SelectedIndex(_profile));
        _form.Provider = _provider.Text ?? "";
        _form.ConnectionString = _connectionString.Text ?? "";
        _form.HybridRetrievalEnabled = FormLayout.ToBoolean(_hybrid.Value);
        _form.CorrectiveWebFallbackEnabled = FormLayout.ToBoolean(_correctiveWebFallback.Value);
        _form.WebFallbackEnabled = FormLayout.ToBoolean(_webFallback.Value);
        _form.CorrectiveMaxIterations = _maxIterations.Text ?? "";
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _profile.Dispose();
            _provider.Dispose();
            _connectionString.Dispose();
            _hybrid.Dispose();
            _correctiveWebFallback.Dispose();
            _webFallback.Dispose();
            _maxIterations.Dispose();
        }

        base.Dispose(disposing);
    }
}
