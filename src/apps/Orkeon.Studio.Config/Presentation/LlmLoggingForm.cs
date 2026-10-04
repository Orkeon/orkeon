using Orkeon.Studio.Core.Configuration;

namespace Orkeon.Studio.Config.Presentation;

/// <summary>
/// The <c>LlmLogging</c> section. The knobs only take effect when a run is started with
/// <c>--llm-log</c>; the editor says so rather than implying the section logs by itself.
/// </summary>
internal sealed class LlmLoggingForm : ISettingsForm
{
    /// <inheritdoc />
    public string Title => "LLM logging";

    /// <summary>Shown above the fields.</summary>
    public const string ActivationNotice =
        "These knobs shape HTTP exchange logging; the logging itself is turned on per run with `orkeon run --llm-log`.";

    private const string FullEmbeddingLogPath = "LlmLogging:FullEmbeddingLog";
    private const string LogStreamingExchangesPath = "LlmLogging:LogStreamingExchanges";
    private const string MaxBodyLengthCharsPath = "LlmLogging:MaxBodyLengthChars";

    /// <summary>Log embedding payloads in full rather than summarized.</summary>
    public bool? FullEmbeddingLog { get; set; }

    /// <summary>Log streaming exchanges as well as unary ones.</summary>
    public bool? LogStreamingExchanges { get; set; }

    /// <summary>Truncation threshold for logged bodies; 0 means no truncation.</summary>
    public string MaxBodyLengthChars { get; set; } = "";

    /// <summary>
    /// The text <c>LlmLogging:FullEmbeddingLog</c> holds when it reads as no boolean (<c>"yes"</c>,
    /// <c>1</c>), else null (STUDIO-55): kept, shown beside the switch left « unset », and refused
    /// when applied — until the switch changes state, which the view says by clearing it.
    /// </summary>
    public string? FullEmbeddingLogAsWritten { get; set; }

    /// <summary>The same for <c>LlmLogging:LogStreamingExchanges</c>.</summary>
    public string? LogStreamingExchangesAsWritten { get; set; }

    /// <inheritdoc />
    public void LoadFrom(AppSettingsDocument document)
    {
        var section = document.LlmLogging;
        FullEmbeddingLog = section.FullEmbeddingLog;
        LogStreamingExchanges = section.LogStreamingExchanges;
        FullEmbeddingLogAsWritten = FieldText.UnreadableSwitch(document, FullEmbeddingLogPath);
        LogStreamingExchangesAsWritten = FieldText.UnreadableSwitch(document, LogStreamingExchangesPath);
        MaxBodyLengthChars = document.GetWritten(MaxBodyLengthCharsPath);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ApplyTo(AppSettingsDocument document)
    {
        var errors = new List<string>();
        if (!FieldText.TryReadInt32(MaxBodyLengthChars, MaxBodyLengthCharsPath, out var maxBodyLength, out var error))
            errors.Add(error!);
        FieldText.RefuseUnreadableSwitch(FullEmbeddingLogAsWritten, FullEmbeddingLogPath, errors);
        FieldText.RefuseUnreadableSwitch(LogStreamingExchangesAsWritten, LogStreamingExchangesPath, errors);
        if (errors.Count > 0)
            return errors;

        var section = document.LlmLogging;
        section.FullEmbeddingLog = FullEmbeddingLog;
        section.LogStreamingExchanges = LogStreamingExchanges;
        section.MaxBodyLengthChars = maxBodyLength;

        return [];
    }
}
