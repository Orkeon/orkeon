using System.Globalization;
using Orkeon.Studio.Config.Presentation;
using Terminal.Gui.Views;

namespace Orkeon.Studio.Config.Views;

/// <summary>
/// The <c>Llm</c> screen. The provider line is read-only — an <c>appsettings.json</c> has no
/// provider key, the endpoint decides — and the API key field carries the standing
/// recommendation to keep the key in <c>ORKEON_Llm__ApiKey</c> instead of the file.
/// </summary>
internal sealed class LlmSectionView : SectionView
{
    private readonly LlmForm _form;
    private readonly TextField _model;
    private readonly TextField _baseUrl;
    private readonly TextField _apiKey;
    private readonly TextField _temperature;
    private readonly TextField _maxTokens;
    private readonly TextField _timeout;
    private readonly Label _provider;
    private readonly Label _apiKeyWarning;

    /// <summary>Builds the screen over <paramref name="form"/>.</summary>
    public LlmSectionView(LlmForm form)
        : base("LLM")
    {
        _form = form ?? throw new ArgumentNullException(nameof(form));

        _model = FormLayout.AddField(this, 0, "Model", _form.Model);
        _baseUrl = FormLayout.AddField(this, 1, "Base URL", _form.BaseUrl);
        _provider = FormLayout.AddText(this, 2, ProviderLine(_form.DetectedProvider));
        _apiKey = FormLayout.AddField(this, 4, "API key (stored in the file)", _form.ApiKey, secret: true);
        FormLayout.AddNote(this, 5, LlmForm.ApiKeyRecommendation);
        _apiKeyWarning = FormLayout.AddText(this, 6, "");
        _temperature = FormLayout.AddField(this, 8, "Temperature", _form.Temperature);
        _maxTokens = FormLayout.AddField(this, 9, "Max tokens", _form.MaxTokens);
        _timeout = FormLayout.AddField(this, 10, "Timeout (seconds)", _form.TimeoutSeconds);

        // The provider is inferred from the endpoint, so it follows every keystroke in it.
        _baseUrl.TextChanged += (_, _) =>
        {
            _form.BaseUrl = _baseUrl.Text ?? "";
            _provider.Text = ProviderLine(_form.DetectedProvider);
        };

        _apiKey.TextChanged += (_, _) =>
        {
            _form.ApiKey = _apiKey.Text ?? "";
            RefreshApiKeyWarning();
        };

        RefreshApiKeyWarning();
    }

    /// <inheritdoc />
    public override void Load()
    {
        _model.Text = _form.Model;
        _baseUrl.Text = _form.BaseUrl;
        _apiKey.Text = _form.ApiKey;
        _temperature.Text = _form.Temperature;
        _maxTokens.Text = _form.MaxTokens;
        _timeout.Text = _form.TimeoutSeconds;
        _provider.Text = ProviderLine(_form.DetectedProvider);
        RefreshApiKeyWarning();
    }

    /// <inheritdoc />
    public override void Apply()
    {
        _form.Model = _model.Text ?? "";
        _form.BaseUrl = _baseUrl.Text ?? "";
        _form.ApiKey = _apiKey.Text ?? "";
        _form.Temperature = _temperature.Text ?? "";
        _form.MaxTokens = _maxTokens.Text ?? "";
        _form.TimeoutSeconds = _timeout.Text ?? "";
    }

    private void RefreshApiKeyWarning() =>
        _apiKeyWarning.Text = _form.StoresApiKeyInClearText
            ? "WARNING: this key will be written in clear text into the saved file."
            : "";

    private static string ProviderLine(string provider) =>
        string.Create(CultureInfo.InvariantCulture, $"Detected provider: {provider}  (read-only — inferred from the base URL)");

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // The base View.Dispose also disposes the Add()-ed subviews; its IsDisposed
            // guard makes these explicit calls idempotent no-ops.
            _model.Dispose();
            _baseUrl.Dispose();
            _apiKey.Dispose();
            _temperature.Dispose();
            _maxTokens.Dispose();
            _timeout.Dispose();
            _provider.Dispose();
            _apiKeyWarning.Dispose();
        }

        base.Dispose(disposing);
    }
}
