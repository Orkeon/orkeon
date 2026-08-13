using System.Globalization;
using Orkeon.Studio.Config.Presentation;
using Orkeon.Studio.Core.Presets;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
// Aliased under a distinct name: the bare name `Application` binds to the enclosing
// `Orkeon.Application` namespace here, and the alias marks the Terminal.Gui call sites.
using TerminalApp = Terminal.Gui.App.Application;

namespace Orkeon.Studio.Config.Views;

/// <summary>
/// The preset chooser: the five presets of <c>orkeon init</c>, with their defaults
/// pre-filled and editable. Applying one rewrites the <c>Llm</c> section only.
/// </summary>
internal sealed class PresetDialog : Window
{
    private readonly PresetForm _form = new();
    private readonly ListView _presets;
    private readonly TextField _baseUrl;
    private readonly TextField _model;
    private readonly TextField _apiKey;
    private readonly TextField _apiKeyEnv;
    private readonly Label _problem;

    /// <summary>The plan the user accepted, or null when the dialog was cancelled.</summary>
    public LlmPresetPlan? AcceptedPlan { get; private set; }

    /// <summary>Builds the chooser.</summary>
    public PresetDialog()
    {
        Title = "Apply a preset";
        X = Pos.Center();
        Y = Pos.Center();
        Width = Dim.Percent(90);
        Height = Dim.Percent(80);

        _presets = FormLayout.AddChoiceList(
            this, 0, PresetForm.Choices.Count, "Preset", PresetForm.Choices, _form.SelectedIndex);

        var next = PresetForm.Choices.Count + 1;
        _baseUrl = FormLayout.AddField(this, next, "Base URL", _form.BaseUrl);
        _model = FormLayout.AddField(this, next + 1, "Model", _form.Model);
        _apiKey = FormLayout.AddField(this, next + 2, "API key (written to the file)", _form.ApiKey, secret: true);
        _apiKeyEnv = FormLayout.AddField(this, next + 3, "…or read it from", _form.ApiKeyEnv);
        FormLayout.AddNote(this, next + 4, string.Create(
            CultureInfo.InvariantCulture,
            $"Leave the key empty to keep it out of the file: the runtime reads {LlmPresets.DefaultApiKeyEnv} natively."));

        _problem = FormLayout.AddText(this, next + 6, "");

        // Switching preset re-applies its defaults, exactly as the `orkeon init` wizard does.
        _presets.ValueChanged += (_, _) =>
        {
            _form.SelectedIndex = FormLayout.SelectedIndex(_presets);
            _baseUrl.Text = _form.BaseUrl;
            _model.Text = _form.Model;
            _apiKey.Text = _form.ApiKey;
            _apiKeyEnv.Text = _form.ApiKeyEnv;
        };

        var apply = new Button { X = FormLayout.Margin, Y = Pos.AnchorEnd(1), Text = "Apply", IsDefault = true };
        var cancel = new Button { X = Pos.Right(apply) + 2, Y = Pos.AnchorEnd(1), Text = "Cancel" };

        apply.Accepting += (_, _) => Accept();
        cancel.Accepting += (_, _) => TerminalApp.RequestStop(this);

        Add(apply, cancel);
    }

    /// <summary>Runs the chooser and returns the accepted plan, or null.</summary>
    public LlmPresetPlan? Show()
    {
        TerminalApp.Run(this);
        return AcceptedPlan;
    }

    private void Accept()
    {
        _form.SelectedIndex = FormLayout.SelectedIndex(_presets);
        _form.BaseUrl = _baseUrl.Text ?? "";
        _form.Model = _model.Text ?? "";
        _form.ApiKey = _apiKey.Text ?? "";
        _form.ApiKeyEnv = _apiKeyEnv.Text ?? "";

        if (!_form.TryBuildPlan(out var plan, out var error))
        {
            _problem.Text = error;
            return;
        }

        AcceptedPlan = plan;
        TerminalApp.RequestStop(this);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _presets.Dispose();
            _baseUrl.Dispose();
            _model.Dispose();
            _apiKey.Dispose();
            _apiKeyEnv.Dispose();
            _problem.Dispose();
        }

        base.Dispose(disposing);
    }
}
