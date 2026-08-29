using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Task;
using Orkeon.Domain.Task.ValueObjects;

namespace Orkeon.Application.Crew.DeliverableResolvers;

/// <summary>
/// Writes the agent's final assistant message to the declared path. Solves the
/// "Gemma triple-quote truncation" bug observed in R12 by removing the need for
/// the model to serialize large markdown payloads inside a tool_call argument.
/// </summary>
public sealed partial class FinalMessageResolver : IDeliverableResolver
{
    private static readonly string[] TrailingTokens =
    [
        "<eos>", "<end_of_turn>", "<|eot_id|>", "<|endoftext|>", "<|im_end|>"
    ];

    private static readonly string[] TrailingQuoteArtifacts =
    [
        "\"\"\")", "\"\"\"}", "\"\"\"'", "\"\"\"", "''')", "'''}", "'''"
    ];

    // R31-P1 / R31-P2: per-format anchor regex used to strip prose preamble
    // before persistence. The supervisor's verify task used to detect this
    // post-hoc and the runner's force_rewrite_from_anchor would clean up
    // (R28-P3), but by then the round status was already `degraded`. By
    // moving the strip-from-anchor logic inside FinalMessageResolver we
    // ensure the file LANDS clean on disk in the first place — no more
    // chain-degraded-on-leaked-prose-preamble.
    //
    // Each regex is anchored start-of-line (RegexOptions.Multiline) and
    // matches the FIRST legal anchor of the deliverable's format. The
    // resolver finds the earliest match and discards everything before it.
    // If no anchor matches the persistence proceeds unchanged (fallback —
    // the runner's post-hoc rewrite still applies as a safety net).
    private static readonly Regex YamlAnchorRegex = new(
        @"^(name:|goal:|process:|verbose:|memory:|memoryProvider:|planning:|circuitBreaker:|llm:|agents:|tasks:|resolution_meta:|# RECOVERED_FROM_MAPPING_|# RESOLVED_TBD_|# Open\s|# TBD_)",
        RegexOptions.Multiline | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    // R32-P2: markdown anchor extended to accept fenced code blocks at the
    // head as well as headings. R31 saw `map_packages` emit a
    // ```json registry_trace
    // {...}
    // ```
    // # Package map
    // sequence; the old regex `^#{1,2} \w` skipped the fence and the H1
    // it preceded was the first match, so the resolver discarded the
    // trace block entirely (R31 had `registry_import_count=0` on 10/10
    // attempts, vs 73-114 in R30). Now any code-fence-with-language
    // (` ```json `, ` ```yaml `, …) or heading is a legal first line,
    // and prose preceding both is the only thing stripped.
    private static readonly Regex MarkdownAnchorRegex = new(
        @"^(#{1,2} [A-Za-z0-9]|```[A-Za-z])",
        RegexOptions.Multiline | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private readonly IFileSystemService _fs;
    private readonly ILogger<FinalMessageResolver> _log;
    private readonly IInlineFqnValidator? _fqnValidator;

    /// <summary>Initializes a new instance of <see cref="FinalMessageResolver"/>.</summary>
    /// <param name="fs">Virtual file system used to write the deliverable.</param>
    /// <param name="log">Logger for diagnostic output.</param>
    /// <param name="fqnValidator">
    /// Optional inline FQN validator. When present, prose-cited FQNs that don't resolve
    /// in the RaggableTree store are surfaced on the result for AUTO_SUMMARY warnings.
    /// </param>
    public FinalMessageResolver(
        IFileSystemService fs,
        ILogger<FinalMessageResolver> log,
        IInlineFqnValidator? fqnValidator = null)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _fqnValidator = fqnValidator;
    }

    /// <inheritdoc />
    public DeliverableSource SupportedSource => DeliverableSource.FinalMessage;

    /// <inheritdoc />
    public System.Threading.Tasks.Task<DeliverableResolutionResult> ResolveAsync(
        CrewTask task,
        string finalAssistantMessage,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(task);
        var deliv = task.Deliverable
            ?? throw new InvalidOperationException("Task.Deliverable is null; FinalMessageResolver requires it.");

        if (string.IsNullOrWhiteSpace(finalAssistantMessage))
        {
            LogEmptyFinalMessage(task.Id.ToString(), deliv.Path);
            return System.Threading.Tasks.Task.FromResult(new DeliverableResolutionResult(
                Persisted: false, Path: deliv.Path, SizeBytes: 0,
                SourceUsed: DeliverableSource.FinalMessage,
                FailureReason: "empty_final_message"));
        }

        return ResolveCoreAsync(task, deliv, finalAssistantMessage, ct);
    }

    private async System.Threading.Tasks.Task<DeliverableResolutionResult> ResolveCoreAsync(
        CrewTask task,
        TaskDeliverable deliv,
        string finalAssistantMessage,
        CancellationToken ct)
    {
        // Always strip the leading U+FEFF emitted by some models (gemma4 via llama.cpp),
        // even when sanitize is disabled. Prevents the encoder from emitting EF BB BF.
        var payload = deliv.Sanitize
            ? Sanitize(finalAssistantMessage)
            : finalAssistantMessage.TrimStart('﻿');

        // R41-P3: unwrap line-fenced payloads BEFORE the preamble strip.
        // R40 a04 D output had every line wrapped in single backticks
        // (`name: claude_code_runtime_replica_resolved` literally on
        // disk). StripPreambleByFormat's regex anchors all start with
        // `name:`, `goal:`, etc. — none of them match a leading
        // backtick, so the strip silently no-op'd and the YAML landed
        // unparseable with `first_line_shape_ok=false`,
        // `resolved_markers=0`. This pass detects the pattern and
        // unwraps line-by-line so the subsequent preamble strip can
        // operate on the real content.
        var (unfencedPayload, lineFenceUnwrapped) = UnwrapLineFence(payload);
        if (lineFenceUnwrapped)
        {
            payload = unfencedPayload;
            LogLineFenceUnwrapped(task.Id.ToString(), deliv.Path);
        }

        // R31-P1 / R31-P2: strip prose preamble before persistence based on
        // declared format. R28-R30 saw repeated leaks where the model
        // prefaces the deliverable with reasoning text like "I now have
        // the full TS architecture analysis. Let me produce..." or
        // "All sources read. S_yaml = ...". The runner's post-hoc
        // force_rewrite_from_anchor caught these but the round
        // already counted as degraded. Stripping here makes the on-disk
        // file canonical from the first persistence.
        var (strippedPayload, preambleStripped, anchorBytesSkipped) =
            StripPreambleByFormat(payload, deliv.Format);
        payload = strippedPayload;

        int bytes;
        try
        {
            bytes = await _fs.WriteAllTextAsync(deliv.Path, payload, ct).ConfigureAwait(false);
        }
        catch (FileAccessDeniedException ex)
        {
            LogAccessDenied(task.Id.ToString(), deliv.Path, ex.Message);
            return new DeliverableResolutionResult(
                Persisted: false, Path: deliv.Path, SizeBytes: 0,
                SourceUsed: DeliverableSource.FinalMessage,
                FailureReason: "access_denied");
        }

        if (_log.IsEnabled(LogLevel.Information))
        {
            var taskId = task.Id.ToString();
            LogPersisted(taskId, deliv.Path, bytes);
        }

        if (preambleStripped)
        {
            LogPreambleStripped(task.Id.ToString(), deliv.Path, anchorBytesSkipped);
        }

        var fqns = await ValidateInlineFqnsAsync(task, deliv, payload, ct).ConfigureAwait(false);

        return new DeliverableResolutionResult(
            Persisted: true, Path: deliv.Path, SizeBytes: bytes,
            SourceUsed: DeliverableSource.FinalMessage,
            UnknownFqns: fqns.Unknown,
            RewrittenFqns: fqns.Rewritten,
            AmbiguousFqns: fqns.Ambiguous);
    }

    /// <summary>
    /// Holds the optional FQN validation outcomes that decorate a successful
    /// deliverable result. All members are null when no validator is configured
    /// or the corresponding category is empty.
    /// </summary>
    private readonly record struct InlineFqnOutcome(
        ImmutableArray<string>? Unknown,
        ImmutableDictionary<string, string>? Rewritten,
        ImmutableArray<DeliverableAmbiguousFqn>? Ambiguous);

    /// <summary>
    /// Runs the optional inline FQN validator over the persisted payload and maps
    /// its result onto an <see cref="InlineFqnOutcome"/>. Validation failures are
    /// logged and swallowed (best-effort) except for cancellation, which propagates.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Best-effort optional validation: an inline FQN validator failure is logged and swallowed (cancellation re-thrown) so a validation fault never fails the already-persisted deliverable.")]
    private async System.Threading.Tasks.Task<InlineFqnOutcome> ValidateInlineFqnsAsync(
        CrewTask task, TaskDeliverable deliv, string payload, CancellationToken ct)
    {
        if (_fqnValidator is null) return default;

        try
        {
            var fqnResult = await _fqnValidator.ValidateAsync(payload, ct).ConfigureAwait(false);
            return new InlineFqnOutcome(
                LogAndMapUnknownFqns(task, deliv, fqnResult),
                LogAndMapRewrittenFqns(task, deliv, fqnResult),
                LogAndMapAmbiguousFqns(task, deliv, fqnResult));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogFqnValidationFailed(task.Id.ToString(), deliv.Path, ex.Message);
            return default;
        }
    }

    private ImmutableArray<string>? LogAndMapUnknownFqns(
        CrewTask task, TaskDeliverable deliv, InlineFqnValidationResult fqnResult)
    {
        if (fqnResult.UnknownFqns.IsDefaultOrEmpty) return null;

        LogUnknownFqns(task.Id.ToString(), deliv.Path,
            fqnResult.UnknownFqns.Length,
            string.Join(", ", fqnResult.UnknownFqns));
        return fqnResult.UnknownFqns;
    }

    private ImmutableDictionary<string, string>? LogAndMapRewrittenFqns(
        CrewTask task, TaskDeliverable deliv, InlineFqnValidationResult fqnResult)
    {
#pragma warning disable S1168 // null means "no rewrites", distinct from an empty rewrite map
        if (fqnResult.Rewrites.Count == 0) return null;
#pragma warning restore S1168

        if (_log.IsEnabled(LogLevel.Information))
        {
            var taskId = task.Id.ToString();
            var rewrites = string.Join(", ", fqnResult.Rewrites.Select(kv => $"{kv.Key}→{kv.Value}"));
            LogRewrittenFqns(taskId, deliv.Path,
                fqnResult.Rewrites.Count,
                rewrites);
        }
        return fqnResult.Rewrites;
    }

    private ImmutableArray<DeliverableAmbiguousFqn>? LogAndMapAmbiguousFqns(
        CrewTask task, TaskDeliverable deliv, InlineFqnValidationResult fqnResult)
    {
        if (fqnResult.Ambiguous.IsDefaultOrEmpty) return null;

        var ambiguousFqns = fqnResult.Ambiguous
            .Select(a => new DeliverableAmbiguousFqn(a.BareFqn, a.Candidates))
            .ToImmutableArray();
        LogAmbiguousFqns(task.Id.ToString(), deliv.Path,
            fqnResult.Ambiguous.Length,
            string.Join(", ", fqnResult.Ambiguous.Select(a =>
                $"{a.BareFqn}∈{{{string.Join("|", a.Candidates)}}}")));
        return ambiguousFqns;
    }

    /// <summary>
    /// R31-P1 / R31-P2: strips prose preamble from a payload by locating the
    /// earliest legal anchor matching the declared format and discarding
    /// everything before it. Returns the stripped payload, a flag indicating
    /// whether anything was stripped, and the byte count of the discarded
    /// prefix (for telemetry).
    ///
    /// <para>Recognised formats: <c>yaml</c>, <c>markdown</c>. Any other
    /// format short-circuits and returns the input unchanged — only formats
    /// with a well-defined first-line shape are eligible for prefix
    /// stripping.</para>
    ///
    /// <para>If no anchor matches, the input is also returned unchanged.
    /// The runner's post-hoc <c>force_rewrite_from_anchor</c> remains as a
    /// safety net (R28-P3) for content the resolver couldn't normalise.</para>
    /// </summary>
    internal static (string payload, bool stripped, int bytesSkipped) StripPreambleByFormat(
        string payload, string? format)
    {
        if (string.IsNullOrEmpty(payload)) return (payload, false, 0);

#pragma warning disable CA1308 // normalized lowercase format token is the switch subject, not a comparison normalization
        var regex = format?.Trim().ToLowerInvariant() switch
#pragma warning restore CA1308
        {
            "yaml" or "yml" => YamlAnchorRegex,
            "markdown" or "md" => MarkdownAnchorRegex,
            _ => null,
        };
        if (regex is null) return (payload, false, 0);

        Match match;
        try
        {
            match = regex.Match(payload);
        }
        catch (RegexMatchTimeoutException)
        {
            return (payload, false, 0);
        }

        if (!match.Success || match.Index == 0)
        {
            // Either no anchor in the payload, or the first character of
            // the payload already IS an anchor — nothing to strip.
            return (payload, false, 0);
        }

        // Don't strip when the prefix is short enough that no realistic
        // preamble would fit. This guards against cosmetic whitespace
        // before a YAML key (e.g. a leading blank line) inflating the
        // "stripped" telemetry without indicating a real regression.
        const int minimumPrefixToStrip = 8;
        if (match.Index < minimumPrefixToStrip)
        {
            return (payload[match.Index..], match.Index > 0, match.Index);
        }

        return (payload[match.Index..], true, match.Index);
    }

    /// <summary>
    /// R41-P3: detect and undo line-fence wrapping where the model has
    /// wrapped every line of the deliverable in a single backtick. Round 40
    /// attempt-04 produced a 28KB YAML where 611/611 lines started AND
    /// ended with a backtick (`` `name: claude_code_runtime_replica` `` etc.),
    /// defeating both the YAML anchor strip and downstream count_pattern
    /// metrics. The runner's force_rewrite_from_anchor safety net could
    /// not help either: with no anchor on a clean line the rewrite was
    /// also a no-op.
    /// <para>Detection rule: at least 90% of non-empty lines start AND end
    /// with a single backtick AND the count of such lines is ≥ 8 (so a
    /// short message containing only `` `code` `` references is not affected).
    /// Triple-fenced markdown blocks are not affected — the heuristic
    /// requires the line to start with a SINGLE backtick followed by a
    /// non-backtick character.</para>
    /// </summary>
    internal static (string payload, bool unwrapped) UnwrapLineFence(string payload)
    {
        if (string.IsNullOrEmpty(payload)) return (payload, false);

        var lines = payload.Split('\n');
        if (!ShouldUnwrapLineFence(lines)) return (payload, false);

        return (RebuildWithoutLineFence(lines, payload.Length), true);
    }

    /// <summary>
    /// Detection pass for <see cref="UnwrapLineFence"/>: returns true when at least
    /// 90% of the (≥ 8) non-empty lines are single-backtick wrapped.
    /// </summary>
    private static bool ShouldUnwrapLineFence(string[] lines)
    {
        int nonEmpty = 0;
        int wrapped = 0;
        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length == 0) continue;
            nonEmpty++;
            if (IsLineFenced(line)) wrapped++;
        }

        if (nonEmpty < 8) return false;
        // Require ≥ 90% of non-empty lines to be single-backtick wrapped.
        return wrapped * 10 >= nonEmpty * 9;
    }

    /// <summary>
    /// Rewrite pass for <see cref="UnwrapLineFence"/>: drops the leading and trailing
    /// backtick from every line-fenced line, preserving the original line endings.
    /// </summary>
    private static string RebuildWithoutLineFence(string[] lines, int capacity)
    {
        var sb = new System.Text.StringBuilder(capacity);
        for (int i = 0; i < lines.Length; i++)
        {
            var rawLine = lines[i];
            var hasCarriageReturn = rawLine.EndsWith('\r');
            var line = hasCarriageReturn ? rawLine[..^1] : rawLine;

            if (IsLineFenced(line))
            {
                line = line[1..^1]; // drop leading and trailing backtick
            }

            sb.Append(line);
            if (hasCarriageReturn) sb.Append('\r');
            if (i < lines.Length - 1) sb.Append('\n');
        }

        return sb.ToString();
    }

    private static bool IsLineFenced(string line) =>
        line.Length >= 2
        && line[0] == '`'
        && line[^1] == '`'
        // Reject triple-fence markers like ```yaml or ```
        && (line.Length < 2 || line[1] != '`')
        && line[^2] != '`';

    /// <summary>
    /// Strips trailing template tokens (<c>&lt;eos&gt;</c>, <c>&lt;|eot_id|&gt;</c>, …) and
    /// orphaned triple-quote fragments left by botched tool_call attempts.
    /// Internal for test visibility only.
    /// </summary>
    internal static string Sanitize(string text)
    {
        // Strip leading U+FEFF. Some local llama.cpp builds (notably gemma4:128K) prepend
        // it to every response; the UTF-8 encoder then emits EF BB BF into the file.
        var trimmed = text.TrimStart('﻿').TrimEnd();

        // Repeat: a run of several <eos> tokens is common on llama.cpp completions.
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var t in TrailingTokens.Where(t => trimmed.EndsWith(t, StringComparison.Ordinal)))
            {
                trimmed = trimmed[..^t.Length].TrimEnd();
                changed = true;
            }
        }

        // Orphan triple quotes left by gemma4 when it "forgets" the `file_write(` prefix.
        var artifact = TrailingQuoteArtifacts.FirstOrDefault(q => trimmed.EndsWith(q, StringComparison.Ordinal));
        if (artifact is not null)
            trimmed = trimmed[..^artifact.Length].TrimEnd();

        return trimmed + "\n";
    }

    // ── Structured logging ─────────────────────────────────────────────────

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "Task {TaskId}: final message is empty, deliverable {Path} not persisted.")]
    partial void LogEmptyFinalMessage(string taskId, string path);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning,
        Message = "Task {TaskId}: access denied for deliverable {Path} ({Reason}).")]
    partial void LogAccessDenied(string taskId, string path, string reason);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information,
        Message = "Task {TaskId}: deliverable persisted via final_message at {Path} ({Bytes} bytes).")]
    partial void LogPersisted(string taskId, string path, int bytes);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning,
        Message = "Task {TaskId}: deliverable {Path} cites {Count} FQN not found in RaggableTree: {Fqns}")]
    partial void LogUnknownFqns(string taskId, string path, int count, string fqns);

    [LoggerMessage(EventId = 6, Level = LogLevel.Information,
        Message = "Task {TaskId}: deliverable {Path} auto-rewrote {Count} bare FQN(s): {Rewrites}")]
    partial void LogRewrittenFqns(string taskId, string path, int count, string rewrites);

    [LoggerMessage(EventId = 7, Level = LogLevel.Warning,
        Message = "Task {TaskId}: deliverable {Path} contains {Count} ambiguous bare FQN(s): {Candidates}")]
    partial void LogAmbiguousFqns(string taskId, string path, int count, string candidates);

    [LoggerMessage(EventId = 5, Level = LogLevel.Warning,
        Message = "Task {TaskId}: inline FQN validation failed for {Path} ({Reason}); continuing.")]
    partial void LogFqnValidationFailed(string taskId, string path, string reason);

    [LoggerMessage(EventId = 8, Level = LogLevel.Warning,
        Message = "Task {TaskId}: stripped {BytesSkipped}-byte prose preamble from final message before persisting {Path} (R31-P1).")]
    partial void LogPreambleStripped(string taskId, string path, int bytesSkipped);

    [LoggerMessage(EventId = 9, Level = LogLevel.Warning,
        Message = "Task {TaskId}: unwrapped line-fenced payload (single-backtick per line) before persisting {Path} (R41-P3).")]
    partial void LogLineFenceUnwrapped(string taskId, string path);
}
