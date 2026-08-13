using Orkeon.Studio.Core.Configuration;

namespace Orkeon.Studio.Wpf.ViewModels.Config;

/// <summary>The <c>LlmLogging</c> form (spec §4.1): the HTTP exchange capture switches.</summary>
public sealed class LlmLoggingSectionViewModel : DocumentSectionViewModel
{
    /// <summary>Binds the form to the <c>LlmLogging</c> section of the document.</summary>
    public LlmLoggingSectionViewModel(Func<AppSettingsDocument> document, Action onChanged)
        : base(document, onChanged)
    {
    }

    private LlmLoggingSection Section => Document.LlmLogging;

    /// <inheritdoc />
    public override bool Exists => Section.Exists;

    /// <summary>Whether embedding payloads are logged in full rather than summarised.</summary>
    public bool? FullEmbeddingLog
    {
        get => Section.FullEmbeddingLog;
        set => SetValue(Section.FullEmbeddingLog, value, v => Section.FullEmbeddingLog = v);
    }

    /// <summary>Whether streaming exchanges are captured too.</summary>
    public bool? LogStreamingExchanges
    {
        get => Section.LogStreamingExchanges;
        set => SetValue(Section.LogStreamingExchanges, value, v => Section.LogStreamingExchanges = v);
    }

    /// <summary>How much of each body is kept before truncation.</summary>
    public int? MaxBodyLengthChars
    {
        get => Section.MaxBodyLengthChars;
        set => SetValue(Section.MaxBodyLengthChars, value, v => Section.MaxBodyLengthChars = v);
    }

    /// <summary>Drops the whole section from the document.</summary>
    public void RemoveSection()
    {
        Section.Remove();
        Refresh();
    }
}
