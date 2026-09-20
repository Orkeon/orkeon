using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Localization;

namespace Orkeon.Studio.Wpf.ViewModels.Config;

/// <summary>
/// The <c>LlmLogging</c> form (spec §4.1): the HTTP exchange capture switches. The switches are
/// plain booleans that resolve an absent key to the engine's default (STUDIO-22): a check box
/// bound to a nullable starts indeterminate and swallows the first click, and the file never
/// needs to spell a value the engine already applies — a switch set back to its default removes
/// the key.
/// </summary>
public sealed class LlmLoggingSectionViewModel : DocumentSectionViewModel
{
    private readonly IStudioStrings _strings;

    /// <summary>Binds the form to the <c>LlmLogging</c> section of the document.</summary>
    public LlmLoggingSectionViewModel(Func<AppSettingsDocument> document, Action onChanged, IStudioStrings? strings = null)
        : base(document, onChanged)
    {
        _strings = strings ?? EnglishStudioStrings.Instance;
        _strings.CultureChanged += (_, _) => OnPropertyChanged(nameof(MaxBodyLengthCharsDefault));
    }

    private LlmLoggingSection Section => Document.LlmLogging;

    /// <inheritdoc />
    public override bool Exists => Section.Exists;

    /// <summary>Whether embedding payloads are logged in full rather than summarised (engine default: on).</summary>
    public bool FullEmbeddingLog
    {
        get => Section.FullEmbeddingLog ?? LlmLoggingSection.DefaultFullEmbeddingLog;
        set => SetValue(FullEmbeddingLog, value,
            v => Section.FullEmbeddingLog = v == LlmLoggingSection.DefaultFullEmbeddingLog ? null : v);
    }

    /// <summary>Whether streaming exchanges are captured too (engine default: on).</summary>
    public bool LogStreamingExchanges
    {
        get => Section.LogStreamingExchanges ?? LlmLoggingSection.DefaultLogStreamingExchanges;
        set => SetValue(LogStreamingExchanges, value,
            v => Section.LogStreamingExchanges = v == LlmLoggingSection.DefaultLogStreamingExchanges ? null : v);
    }

    /// <summary>How much of each body is kept before truncation.</summary>
    public int? MaxBodyLengthChars
    {
        get => Section.MaxBodyLengthChars;
        set => SetValue(Section.MaxBodyLengthChars, value, v => Section.MaxBodyLengthChars = v);
    }

    /// <summary>The watermark: the engine's 0 means no truncation, so the word rather than the number.</summary>
    public string MaxBodyLengthCharsDefault => _strings[StudioStringKeys.SettingsUnlimited];

    /// <summary>Drops the whole section from the document.</summary>
    public void RemoveSection()
    {
        Section.Remove();
        Refresh();
    }
}
