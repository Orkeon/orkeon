using Orkeon.Studio.Config.Presentation;
using Terminal.Gui.Views;

namespace Orkeon.Studio.Config.Views;

/// <summary>The <c>LlmLogging</c> screen: what an exchange log contains once a run turns it on.</summary>
internal sealed class LlmLoggingSectionView : SectionView
{
    private readonly LlmLoggingForm _form;
    private readonly CheckBox _fullEmbeddingLog;
    private readonly CheckBox _logStreamingExchanges;
    private readonly TextField _maxBodyLength;

    /// <summary>Builds the screen over <paramref name="form"/>.</summary>
    public LlmLoggingSectionView(LlmLoggingForm form)
        : base("LLM logging")
    {
        _form = form ?? throw new ArgumentNullException(nameof(form));

        FormLayout.AddNote(this, 0, LlmLoggingForm.ActivationNotice);
        _fullEmbeddingLog = FormLayout.AddOptionalSwitch(
            this, 2, FullEmbeddingLogCaption, _form.FullEmbeddingLog);
        _logStreamingExchanges = FormLayout.AddOptionalSwitch(
            this, 3, LogStreamingExchangesCaption, _form.LogStreamingExchanges);
        _maxBodyLength = FormLayout.AddField(this, 5, "Max body length (chars, 0 = no limit)", _form.MaxBodyLengthChars);

        FormLayout.TrackUnreadable(_fullEmbeddingLog, FullEmbeddingLogCaption,
            () => _form.FullEmbeddingLogAsWritten, () => _form.FullEmbeddingLogAsWritten = null);
        FormLayout.TrackUnreadable(_logStreamingExchanges, LogStreamingExchangesCaption,
            () => _form.LogStreamingExchangesAsWritten, () => _form.LogStreamingExchangesAsWritten = null);
    }

    private const string FullEmbeddingLogCaption = "Log embedding payloads in full";
    private const string LogStreamingExchangesCaption = "Log streaming exchanges";

    /// <inheritdoc />
    public override void Load()
    {
        // The state first: setting it raises a change, which must not clear the text just loaded.
        var fullEmbeddingLog = _form.FullEmbeddingLogAsWritten;
        var logStreamingExchanges = _form.LogStreamingExchangesAsWritten;
        _fullEmbeddingLog.Value = FormLayout.ToCheckState(_form.FullEmbeddingLog);
        _logStreamingExchanges.Value = FormLayout.ToCheckState(_form.LogStreamingExchanges);
        _form.FullEmbeddingLogAsWritten = fullEmbeddingLog;
        _form.LogStreamingExchangesAsWritten = logStreamingExchanges;
        _fullEmbeddingLog.Text = FormLayout.SwitchCaption(FullEmbeddingLogCaption, fullEmbeddingLog);
        _logStreamingExchanges.Text = FormLayout.SwitchCaption(LogStreamingExchangesCaption, logStreamingExchanges);
        _maxBodyLength.Text = _form.MaxBodyLengthChars;
    }

    /// <inheritdoc />
    public override void Apply()
    {
        _form.FullEmbeddingLog = FormLayout.ToBoolean(_fullEmbeddingLog.Value);
        _form.LogStreamingExchanges = FormLayout.ToBoolean(_logStreamingExchanges.Value);
        _form.MaxBodyLengthChars = _maxBodyLength.Text ?? "";
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _fullEmbeddingLog.Dispose();
            _logStreamingExchanges.Dispose();
            _maxBodyLength.Dispose();
        }

        base.Dispose(disposing);
    }
}
