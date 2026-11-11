using System.Text.Json;
using Jint;
using Jint.Native;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Scripting.Runtime;

/// <summary>
/// The <c>onContextPressure</c> option of <c>act</c> (#327): once the loop's messages pass a token
/// threshold, the script is handed the loop's older read-tool results and answers, per call, whether the
/// result stays (<c>keep</c>), is cut to its first characters (<c>head</c>) or goes (<c>drop</c>). A pruned
/// result leaves a line that says what was removed and how to get it back; the call itself stays, so the
/// tool-call protocol stays paired. Only the results of read tools (<c>ToolAccess.Read</c>) that ran are
/// offered: calling one again is harmless, calling a write or a command again is not. Results seeded from
/// an earlier <c>act</c> are not offered. No callback, a callback that throws or an answer that is not an
/// object prunes nothing: the loop goes on as before.
/// </summary>
public sealed partial class JsLlmFacade
{
    /// <summary>One tool result of the act loop, as <c>onContextPressure</c> is offered it.</summary>
    /// <param name="Index">Its position in the loop's messages; the loop only ever appends, so it stays valid.</param>
    /// <param name="Id">The id the script answers with: <c>call-1</c>, <c>call-2</c>... in call order.</param>
    /// <param name="ToolName">The tool that ran.</param>
    /// <param name="Arguments">The input it ran with.</param>
    /// <param name="Iteration">The loop iteration that ran it.</param>
    internal sealed record ActToolRecord(int Index, string Id, string ToolName, IReadOnlyDictionary<string, object?> Arguments, int Iteration)
    {
        /// <summary>Whether a verdict already settled the result (kept, cut or removed): it is never offered again.</summary>
        public bool Settled { get; set; }
    }

    /// <summary>The <c>contextPressure</c> option of <c>act</c>, defaults filled in.</summary>
    /// <param name="ThresholdTokens">The estimated size of the loop's messages from which the callback runs.</param>
    /// <param name="KeepRecent">The number of most recent messages (not turns) whose tool results are never offered.</param>
    /// <param name="HeadChars">The characters a <c>head</c> verdict keeps.</param>
    internal sealed record ContextPressureSettings(int ThresholdTokens, int KeepRecent, int HeadChars)
    {
        /// <summary>Half the 200,000-token window the CLI session assumes by default.</summary>
        public const int DefaultThresholdTokens = 100_000;

        /// <summary>The last four messages stay out of reach by default: a model turn and the results it asked for, when it asked for up to three.</summary>
        public const int DefaultKeepRecent = 4;

        /// <summary>Enough of a file or a listing for the model to recognise it.</summary>
        public const int DefaultHeadChars = 1_000;

        /// <summary>Reads <c>options.contextPressure</c>; a missing or invalid field takes its default.</summary>
        public static ContextPressureSettings From(JsValue? options)
        {
            var settings = options is not null && options.IsObject() ? options.Get("contextPressure") : JsValue.Undefined;
            return new ContextPressureSettings(
                Read(settings, "thresholdTokens", DefaultThresholdTokens),
                Read(settings, "keepRecent", DefaultKeepRecent),
                Read(settings, "headChars", DefaultHeadChars));
        }

        private static int Read(JsValue settings, string name, int fallback)
        {
            if (!settings.IsObject()) return fallback;
            var raw = settings.Get(name);
            if (!raw.IsNumber()) return fallback;
            var value = raw.AsNumber();
            return value >= 0 && value <= int.MaxValue ? (int)value : fallback;
        }
    }

    /// <summary>A rough estimate: characters divided by four.</summary>
    private static int EstimateTokens(long chars) => (int)Math.Min(int.MaxValue, chars / 4);

    /// <summary>
    /// The characters of one message the model is sent again every turn: its text, its tool calls and its
    /// reasoning. Images and the tool schemas are not counted, so the estimate is a floor. It is not what
    /// <c>token_budget</c> reports: that tool reads the session buffer, which never sees the act loop's messages.
    /// </summary>
    private static long MessageChars(LlmMessage m) =>
        (long)m.Content.Length + (m.RawToolCalls?.Length ?? 0) + (m.RawThinkingBlocks?.Length ?? 0) + (m.ReasoningContent?.Length ?? 0);

    /// <summary>The input quoted on a pruned result's line, cut so the line stays short.</summary>
    private const int MaxQuotedInputChars = 200;

    /// <summary>
    /// Runs <c>onContextPressure</c> when the loop's messages passed the threshold and older tool results
    /// remain to judge, then applies its verdicts to the messages the next model turn is sent.
    /// </summary>
    private async Task RelieveContextPressureAsync(ActSession session)
    {
        if (!session.HasContextPressureCallback)
            return;
        var messages = session.Messages;
        var used = EstimateTokens(messages.Sum(MessageChars));
        if (used < session.Pressure.ThresholdTokens)
            return;
        var reachable = messages.Count - session.Pressure.KeepRecent;
        var candidates = session.ToolRecords.Where(r => !r.Settled && r.Index < reachable).ToList();
        if (candidates.Count == 0)
            return;

        var payload = JsonSerializer.Serialize(new
        {
            usedTokens = used,
            thresholdTokens = session.Pressure.ThresholdTokens,
            usedTokensByKind = new { tool = ToolTokens(messages) },
            calls = candidates.Select(r => new
            {
                id = r.Id,
                name = r.ToolName,
                input = r.Arguments,
                resultChars = messages[r.Index].Content.Length,
                age = session.Iteration - r.Iteration,
            }),
        });
        var response = await session.CallContextPressureCallbackAsync(payload, _ct).ConfigureAwait(false);
        if (!response.Success || response.Result is not string json)
        {
            if (_logger is not null)
                LogContextPressureIgnored(_logger, _agentName, response.Error ?? "no verdicts");
            return;
        }

        JsonDocument verdicts;
        try { verdicts = JsonDocument.Parse(json); }
        catch (JsonException)
        {
            if (_logger is not null)
                LogContextPressureIgnored(_logger, _agentName, "the verdicts are not JSON");
            return;
        }
        using var disposeVerdicts = verdicts;
        if (verdicts.RootElement.ValueKind != JsonValueKind.Object)
        {
            if (_logger is not null)
                LogContextPressureIgnored(_logger, _agentName, "the verdicts are not an object");
            return;
        }

        foreach (var record in candidates)
        {
            if (!verdicts.RootElement.TryGetProperty(record.Id, out var verdict) || verdict.ValueKind != JsonValueKind.String)
                continue;
            var content = messages[record.Index].Content;
            var pruned = verdict.GetString() switch
            {
                "keep" => content,
                "drop" => Dropped(record, content),
                "head" => Headed(record, content, session.Pressure.HeadChars),
                _ => null,
            };
            if (pruned is null)
                continue;
            // A verdict settles the result: kept, or a cut that would not shrink it, is never offered again either.
            record.Settled = true;
            if (pruned.Length < content.Length)
                messages[record.Index] = messages[record.Index] with { Content = pruned };
        }
    }

    /// <summary>The estimated tokens of the tool results among <paramref name="messages"/>.</summary>
    private static int ToolTokens(List<LlmMessage> messages) =>
        EstimateTokens(messages.Where(m => m.Role == "tool").Sum(m => (long)m.Content.Length));

    /// <summary>The input a pruned line quotes, cut to <see cref="MaxQuotedInputChars"/> characters.</summary>
    private static string QuotedInput(ActToolRecord record)
    {
        var input = JsonSerializer.Serialize(record.Arguments);
        return input.Length <= MaxQuotedInputChars ? input : input[..MaxQuotedInputChars] + "...";
    }

    /// <summary>The line a dropped result leaves: what it was and how to get it back.</summary>
    private static string Dropped(ActToolRecord record, string content) =>
        $"[pruned to save context: the result of {record.ToolName} {QuotedInput(record)} " +
        $"({content.Length} characters) was removed. Call {record.ToolName} again with the same input if you need it.]";

    /// <summary>The result cut to its first <paramref name="headChars"/> characters with a line saying so; the result itself when it is no longer than that.</summary>
    private static string? Headed(ActToolRecord record, string content, int headChars) =>
        content.Length <= headChars
            ? content
            : content[..headChars] +
              $"\n[pruned to save context: {content.Length - headChars} of {content.Length} characters cut here. " +
              $"Call {record.ToolName} {QuotedInput(record)} again for the full result.]";

    [Microsoft.Extensions.Logging.LoggerMessage(
        EventId = 6, Level = Microsoft.Extensions.Logging.LogLevel.Warning,
        Message = "act: agent '{Agent}' onContextPressure pruned nothing: {Reason}")]
    static partial void LogContextPressureIgnored(Microsoft.Extensions.Logging.ILogger logger, string agent, string reason);
}
