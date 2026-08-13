namespace Orkeon.Studio.Core.Configuration;

/// <summary>
/// Typed view over the <c>LlmLogging</c> section — the knobs <c>RunnerHost</c> binds
/// when a runner is started with <c>--llm-log</c> (the section alone logs nothing).
/// </summary>
public sealed class LlmLoggingSection
{
    /// <summary>Configuration path of the section.</summary>
    public const string SectionPath = "LlmLogging";

    private readonly AppSettingsDocument _document;

    internal LlmLoggingSection(AppSettingsDocument document) => _document = document;

    /// <summary>True when the section carries at least one key.</summary>
    public bool Exists => _document.SectionExists(SectionPath);

    /// <summary>Logs embedding payloads in full rather than summarized (runtime default: true).</summary>
    public bool? FullEmbeddingLog
    {
        get => _document.GetBoolean($"{SectionPath}:FullEmbeddingLog");
        set => _document.SetBoolean($"{SectionPath}:FullEmbeddingLog", value);
    }

    /// <summary>Logs streaming exchanges as well as unary ones (runtime default: true).</summary>
    public bool? LogStreamingExchanges
    {
        get => _document.GetBoolean($"{SectionPath}:LogStreamingExchanges");
        set => _document.SetBoolean($"{SectionPath}:LogStreamingExchanges", value);
    }

    /// <summary>Truncation threshold for logged bodies; 0 means no truncation (runtime default).</summary>
    public int? MaxBodyLengthChars
    {
        get => _document.GetInt32($"{SectionPath}:MaxBodyLengthChars");
        set => _document.SetInt32($"{SectionPath}:MaxBodyLengthChars", value);
    }

    /// <summary>Removes the whole section.</summary>
    public void Remove() => _document.Remove(SectionPath);
}
