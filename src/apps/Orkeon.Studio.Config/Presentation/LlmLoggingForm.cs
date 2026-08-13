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

    /// <summary>Log embedding payloads in full rather than summarized.</summary>
    public bool? FullEmbeddingLog { get; set; }

    /// <summary>Log streaming exchanges as well as unary ones.</summary>
    public bool? LogStreamingExchanges { get; set; }

    /// <summary>Truncation threshold for logged bodies; 0 means no truncation.</summary>
    public string MaxBodyLengthChars { get; set; } = "";

    /// <inheritdoc />
    public void LoadFrom(AppSettingsDocument document)
    {
        var section = document.LlmLogging;
        FullEmbeddingLog = section.FullEmbeddingLog;
        LogStreamingExchanges = section.LogStreamingExchanges;
        MaxBodyLengthChars = FieldText.FromInt32(section.MaxBodyLengthChars);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ApplyTo(AppSettingsDocument document)
    {
        if (!FieldText.TryReadInt32(
                MaxBodyLengthChars,
                "LlmLogging:MaxBodyLengthChars",
                out var maxBodyLength,
                out var error))
        {
            return [error!];
        }

        var section = document.LlmLogging;
        section.FullEmbeddingLog = FullEmbeddingLog;
        section.LogStreamingExchanges = LogStreamingExchanges;
        section.MaxBodyLengthChars = maxBodyLength;

        return [];
    }
}
