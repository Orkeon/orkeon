using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Orkeon.Studio.Config.Presentation;
using Orkeon.Studio.Core.Llm;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using TerminalApp = Terminal.Gui.App.Application;

namespace Orkeon.Studio.Config.Views;

/// <summary>
/// The <c>Llm</c> screen. The provider line is read-only — an <c>appsettings.json</c> has no
/// provider key, the endpoint decides — and the API key field carries the standing
/// recommendation to keep the key in <c>ORKEON_Llm__ApiKey</c> instead of the file.
/// The "Test connection" button runs the optional connectivity probe of SPEC §4.2: it never
/// blocks the screen, and its verdict changes nothing about what can be saved.
/// </summary>
internal sealed class LlmSectionView : SectionView
{
    private readonly LlmForm _form;
    private readonly ILlmEndpointProbe _probe;
    private readonly TextField _model;
    private readonly TextField _baseUrl;
    private readonly TextField _apiKey;
    private readonly TextField _temperature;
    private readonly TextField _maxTokens;
    private readonly TextField _timeout;
    private readonly Label _provider;
    private readonly Label _apiKeyWarning;
    private readonly Button _testConnection;
    private readonly Label _testResult;

    /// <summary>Builds the screen over <paramref name="form"/>.</summary>
    /// <param name="form">The section's values, as text.</param>
    /// <param name="probe">Runs the optional connectivity test.</param>
    public LlmSectionView(LlmForm form, ILlmEndpointProbe probe)
        : base("LLM")
    {
        _form = form ?? throw new ArgumentNullException(nameof(form));
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));

        _model = FormLayout.AddField(this, 0, "Model", _form.Model);
        _baseUrl = FormLayout.AddField(this, 1, "Base URL", _form.BaseUrl);
        _provider = FormLayout.AddText(this, 2, ProviderLine(_form.DetectedProvider));
        _apiKey = FormLayout.AddField(this, 4, "API key (stored in the file)", _form.ApiKey, secret: true);
        FormLayout.AddNote(this, 5, LlmForm.ApiKeyRecommendation);
        _apiKeyWarning = FormLayout.AddText(this, 6, "");
        _temperature = FormLayout.AddField(this, 8, "Temperature", _form.Temperature);
        _maxTokens = FormLayout.AddField(this, 9, "Max tokens", _form.MaxTokens);
        _timeout = FormLayout.AddField(this, 10, "Timeout (seconds)", _form.TimeoutSeconds);

        _testConnection = new Button { X = FormLayout.Margin, Y = 12, Text = "Test connection" };
        _testConnection.Accepting += (_, _) => TestConnection();
        Add(_testConnection);
        _testResult = FormLayout.AddText(this, 13, "");

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

    /// <summary>
    /// The verdict of the last probe — the text the result label carries. Set before the UI
    /// thread is asked to render it, so it is readable the moment the probe returns.
    /// </summary>
    internal string TestResult { get; private set; } = "";

    /// <summary>
    /// Starts a probe of whatever is currently on screen and returns immediately: the screen
    /// stays usable while the endpoint is being reached, and a failure is a message, never a
    /// reason to refuse anything else.
    /// </summary>
    internal Task TestConnection()
    {
        // The user may have typed a new endpoint without leaving the screen, so read the
        // widgets back into the form before deciding what to probe.
        Apply();
        TestResult = "Testing the connection…";
        _testResult.Text = TestResult;
        return TestConnectionAsync();
    }

    [SuppressMessage("Design", "CA1031",
        Justification = "Fault barrier around a network probe started from a button: an " +
                        "unexpected failure belongs in the result label, not in an unobserved " +
                        "task exception that would tear the UI down.")]
    private async Task TestConnectionAsync()
    {
        string message;
        try
        {
            var result = await _probe.ProbeAsync(_form.ToProbeRequest()).ConfigureAwait(false);
            message = result.Message;
        }
        catch (Exception ex)
        {
            message = string.Create(CultureInfo.InvariantCulture, $"Connection failed: {ex.Message}");
        }

        TestResult = message;
        RenderTestResult(message);
    }

    /// <summary>
    /// Puts the verdict on the label. The probe answers on a background thread, so the update
    /// is marshalled onto the UI thread — except when no main loop is running (a screen built
    /// outside <c>Application.Init</c>, which is how these views are exercised headlessly),
    /// where <c>Invoke</c> would throw and there is no other thread to defer to anyway.
    /// </summary>
    private void RenderTestResult(string message)
    {
        if (TerminalApp.Initialized)
            TerminalApp.Invoke(() => _testResult.Text = message);
        else
            _testResult.Text = message;
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
            _testConnection.Dispose();
            _testResult.Dispose();
        }

        base.Dispose(disposing);
    }
}
