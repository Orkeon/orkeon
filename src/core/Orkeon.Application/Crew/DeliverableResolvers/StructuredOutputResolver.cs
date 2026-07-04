using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Task;
using Orkeon.Domain.Task.ValueObjects;
namespace Orkeon.Application.Crew.DeliverableResolvers;

/// <summary>
/// Persists a JSON deliverable produced by an agent whose final message is expected
/// to satisfy a JSON Schema. Relies on an upstream GBNF grammar injection (see
/// <c>ExecutionOrchestrator</c>) to constrain generation; the resolver itself always
/// re-validates with <see cref="JsonDocument.Parse(string, JsonDocumentOptions)"/>
/// as a safety net, so providers that silently ignore the grammar fail loudly
/// instead of writing garbage.
/// </summary>
public sealed partial class StructuredOutputResolver : IDeliverableResolver
{
    private readonly IFileSystemService _fs;
    private readonly ILogger<StructuredOutputResolver> _log;

    /// <summary>Initializes a new instance of <see cref="StructuredOutputResolver"/>.</summary>
    public StructuredOutputResolver(
        IFileSystemService fs,
        ILogger<StructuredOutputResolver> log)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <inheritdoc />
    public DeliverableSource SupportedSource => DeliverableSource.StructuredOutput;

    /// <inheritdoc />
    public System.Threading.Tasks.Task<DeliverableResolutionResult> ResolveAsync(
        CrewTask task,
        string finalAssistantMessage,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(task);
        var deliv = task.Deliverable
            ?? throw new InvalidOperationException("Task.Deliverable is null; StructuredOutputResolver requires it.");

        if (string.IsNullOrWhiteSpace(finalAssistantMessage))
            return System.Threading.Tasks.Task.FromResult(
                Fail(deliv.Path, "empty_final_message",
                    () => LogEmptyFinalMessage(task.Id.ToString(), deliv.Path, finalAssistantMessage?.Length ?? 0, RawHash(finalAssistantMessage))));

        var expectedRoot = ResolveExpectedRoot(deliv.SchemaInline);
        var requiredTopKeys = ResolveRequiredTopKeys(deliv.SchemaInline);
        var candidates = EnumerateJsonCandidates(finalAssistantMessage, expectedRoot).ToList();
        if (candidates.Count == 0)
            return System.Threading.Tasks.Task.FromResult(
                Fail(deliv.Path, "no_json_payload",
                    () => LogNoJsonFound(task.Id.ToString(), deliv.Path, finalAssistantMessage.Length, RawHash(finalAssistantMessage))));

        var selection = SelectCanonicalCandidate(candidates, requiredTopKeys);
        var canonical = selection.Canonical;
        var partial = selection.Partial;

        if (canonical is null)
        {
            // Even greedy extraction yielded no parseable JSON. Keep the historical
            // hard-fail behavior but enrich the log with raw text length + hash so
            // operators can diagnose without re-running.
            var failureReason = selection.FirstError is not null ? "invalid_json" : "no_json_payload";
            var preview = selection.FirstPreview ?? Preview(candidates[0]);
            return System.Threading.Tasks.Task.FromResult(
                Fail(deliv.Path, failureReason,
                    () => LogInvalidJson(
                        task.Id.ToString(), deliv.Path, selection.FirstError ?? failureReason,
                        preview, finalAssistantMessage.Length, RawHash(finalAssistantMessage))));
        }

        if (partial)
            LogPartialExtraction(task.Id.ToString(), deliv.Path, string.Join(",", requiredTopKeys));

        return PersistPayloadAsync(task.Id.ToString(), deliv.Path, canonical + "\n", partial, ct);
    }

    /// <summary>
    /// Outcome of the two-pass candidate selection: the canonicalised JSON (or null
    /// if none parsed), whether it was a partial salvage, and the first parse error
    /// and preview captured for diagnostics when nothing qualified.
    /// </summary>
    private readonly record struct CandidateSelection(
        string? Canonical, bool Partial, string? FirstError, string? FirstPreview);

    /// <summary>
    /// Picks the canonical JSON candidate. Pass 1 (R30-P2) prefers a candidate that
    /// satisfies the schema's required top-level keys, avoiding regressions where a
    /// prose fragment like <c>missing_in_sec6 = {}</c> was persisted because <c>{}</c>
    /// is syntactically valid JSON. Pass 2 (Experiment 07 friction #3) is a
    /// partial-salvage fallback that persists the best parseable candidate with
    /// <c>partial=true</c> rather than dropping the assistant message. The historical
    /// R32-P1 hard-fail is preserved when no candidate parses at all.
    /// </summary>
    private static CandidateSelection SelectCanonicalCandidate(
        IReadOnlyList<string> candidates, IReadOnlyList<string> requiredTopKeys)
    {
        string? firstError = null;
        string? firstPreview = null;

        foreach (var candidate in candidates)
        {
            var (result, error) = CanonicaliseJson(candidate);
            if (result is null)
            {
                firstError ??= error;
                firstPreview ??= Preview(candidate);
                continue;
            }
            if (requiredTopKeys.Count == 0 || HasAllTopLevelKeys(result, requiredTopKeys))
            {
                return new CandidateSelection(result, false, firstError, firstPreview);
            }
        }

        var salvaged = SalvageFirstParseable(candidates);
        return salvaged is null
            ? new CandidateSelection(null, false, firstError, firstPreview)
            : new CandidateSelection(salvaged, requiredTopKeys.Count > 0, firstError, firstPreview);
    }

    /// <summary>
    /// Returns the first candidate that canonicalises successfully, ignoring required-key
    /// constraints. Used as the partial-salvage fallback when pass 1 found no fully
    /// compliant candidate.
    /// </summary>
    private static string? SalvageFirstParseable(IReadOnlyList<string> candidates)
    {
        foreach (var candidate in candidates)
        {
            var (result, _) = CanonicaliseJson(candidate);
            if (result is not null) return result;
        }
        return null;
    }

    /// <summary>
    /// Reads the top-level <c>"required"</c> array from an inline JSON Schema and
    /// returns its string entries. Best-effort: any parse failure or non-string
    /// item is silently skipped and the caller treats the result as "no required
    /// keys declared." Internal for test visibility.
    /// </summary>
    internal static IReadOnlyList<string> ResolveRequiredTopKeys(string? schemaInline)
    {
        if (string.IsNullOrWhiteSpace(schemaInline)) return Array.Empty<string>();
        try
        {
            using var doc = JsonDocument.Parse(schemaInline);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return Array.Empty<string>();
            if (!doc.RootElement.TryGetProperty("required", out var req)) return Array.Empty<string>();
            if (req.ValueKind != JsonValueKind.Array) return Array.Empty<string>();

            var keys = new List<string>(req.GetArrayLength());
            foreach (var item in req.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String) continue;
                var s = item.GetString();
                if (!string.IsNullOrEmpty(s)) keys.Add(s!);
            }
            return keys;
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static bool HasAllTopLevelKeys(string canonicalJson, IReadOnlyList<string> requiredKeys)
    {
        if (requiredKeys.Count == 0) return true;
        try
        {
            using var doc = JsonDocument.Parse(canonicalJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;
            foreach (var key in requiredKeys)
            {
                if (!doc.RootElement.TryGetProperty(key, out _)) return false;
            }
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static DeliverableResolutionResult Fail(string path, string reason, Action log)
    {
        log();
        return new DeliverableResolutionResult(
            Persisted: false, Path: path, SizeBytes: 0,
            SourceUsed: DeliverableSource.StructuredOutput,
            FailureReason: reason);
    }

    internal static (string? Result, string? Error) CanonicaliseJson(string candidate)
    {
        try
        {
            using var doc = JsonDocument.Parse(candidate);
            // Re-emit compactly so whitespace/fence artefacts don't survive.
            return (doc.RootElement.GetRawText(), null);
        }
        catch (JsonException ex)
        {
            return (null, ex.Message);
        }
    }

    private async System.Threading.Tasks.Task<DeliverableResolutionResult> PersistPayloadAsync(
        string taskId, string path, string payload, bool partial, CancellationToken ct)
    {
        try
        {
            var bytes = await _fs.WriteAllTextAsync(path, payload, ct).ConfigureAwait(false);
            LogPersisted(taskId, path, bytes);
            await WarnOnZeroInventionAsync(taskId, path, payload, ct).ConfigureAwait(false);
            return new DeliverableResolutionResult(
                Persisted: true, Path: path, SizeBytes: bytes,
                SourceUsed: DeliverableSource.StructuredOutput,
                FailureReason: partial ? "partial_extraction" : null,
                PartialExtraction: partial);
        }
        catch (FileAccessDeniedException ex)
        {
            LogAccessDenied(taskId, path, ex.Message);
            return new DeliverableResolutionResult(
                Persisted: false, Path: path, SizeBytes: 0,
                SourceUsed: DeliverableSource.StructuredOutput,
                FailureReason: "access_denied");
        }
    }

    /// <summary>
    /// SHA-256 of the raw assistant message, truncated to 16 hex chars. Lets operators
    /// fingerprint a failed extraction across logs without re-serializing the full body.
    /// </summary>
    internal static string RawHash(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "<empty>";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        var sb = new StringBuilder(16);
        for (var i = 0; i < 8 && i < bytes.Length; i++)
            sb.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    /// <summary>
    /// R36-P3 ground-truth safeguard. When a Crew Supervisor persists a
    /// FINAL_SUMMARY-shaped payload that claims <c>commands_table_rows: 0</c>
    /// while the markdown file referenced by its <c>document</c> field actually
    /// contains numbered table rows, we know the model just zero-invented its
    /// own metric (round-36 attempt-03 regression). The runner cannot safely
    /// auto-correct the JSON — that would mask other failure modes — but it
    /// surfaces the disagreement as a loud warning so the run is diagnosable
    /// without needing to manually compare the JSON to the markdown.
    /// </summary>
    private async System.Threading.Tasks.Task WarnOnZeroInventionAsync(
        string taskId, string jsonPath, string payload, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return;

            // The pattern only applies to FINAL_SUMMARY-shaped payloads:
            // both the `commands_table_rows` metric and the `document` pointer
            // must coexist. Any other deliverable schema is left alone.
            if (!doc.RootElement.TryGetProperty("commands_table_rows", out var rowsProp)) return;
            if (rowsProp.ValueKind != JsonValueKind.Number) return;
            if (!rowsProp.TryGetInt32(out var declaredRows) || declaredRows != 0) return;

            if (!doc.RootElement.TryGetProperty("document", out var docProp)) return;
            if (docProp.ValueKind != JsonValueKind.String) return;
            var documentPath = docProp.GetString();
            if (string.IsNullOrWhiteSpace(documentPath)) return;

            string? content;
            try
            {
                content = await _fs.TryReadAllTextAsync(documentPath!, ct).ConfigureAwait(false);
            }
            catch (FileAccessDeniedException)
            {
                // If we can't read the referenced file (vfs path mismatch,
                // permissions, etc.), stay silent — we have no ground truth.
                return;
            }
            if (content is null) return;

            var actualRows = CountNumberedTableRows(content);
            if (actualRows > 0)
            {
                LogZeroInventionDetected(taskId, jsonPath, documentPath!, actualRows);
            }
        }
        catch (JsonException)
        {
            // The payload was canonicalised before persistence, so a parse
            // error here would be very surprising. Stay defensive: silently
            // skip the safeguard rather than throwing past a successful write.
        }
    }

    /// <summary>
    /// Counts numbered Markdown table rows (lines starting with "| &lt;digits&gt; |").
    /// Mirrors the regex the Supervisor's prompt prescribes
    /// (<c>^\| +\d+ +\| </c>) so the warning fires on the exact same metric
    /// the verifier is supposed to be measuring.
    /// </summary>
    internal static int CountNumberedTableRows(string content)
    {
        if (string.IsNullOrEmpty(content)) return 0;
        var count = 0;
        var lineStart = 0;
        for (var i = 0; i <= content.Length; i++)
        {
            if (i == content.Length || content[i] == '\n')
            {
                var lineEnd = i;
                if (lineEnd > lineStart && content[lineEnd - 1] == '\r') lineEnd--;
                if (LooksLikeNumberedRow(content, lineStart, lineEnd)) count++;
                lineStart = i + 1;
            }
        }
        return count;
    }

    private static bool LooksLikeNumberedRow(string content, int start, int end)
    {
        // Matches a leading pipe, optional spaces, one or more digits, optional spaces, then a pipe.
        var i = start;
        if (i >= end || content[i] != '|') return false;
        i++;
        while (i < end && content[i] == ' ') i++;
        var digitStart = i;
        while (i < end && content[i] >= '0' && content[i] <= '9') i++;
        if (i == digitStart) return false;
        while (i < end && content[i] == ' ') i++;
        return i < end && content[i] == '|';
    }

    /// <summary>
    /// Indicates which JSON root kind the deliverable schema declares — used to bias
    /// candidate ordering when the message contains both <c>[...]</c> and <c>{...}</c>
    /// fragments. <see cref="Any"/> means "no preference".
    /// </summary>
    internal enum JsonRootKind { Any, Object, Array }

    /// <summary>
    /// Reads the top-level <c>"type"</c> of an inline JSON Schema and maps it to a
    /// <see cref="JsonRootKind"/>. Best-effort: any parse failure or unrecognised shape
    /// downgrades to <see cref="JsonRootKind.Any"/>. Internal for test visibility.
    /// </summary>
    internal static JsonRootKind ResolveExpectedRoot(string? schemaInline)
    {
        if (string.IsNullOrWhiteSpace(schemaInline)) return JsonRootKind.Any;
        try
        {
            using var doc = JsonDocument.Parse(schemaInline);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return JsonRootKind.Any;
            if (!doc.RootElement.TryGetProperty("type", out var typeProp)) return JsonRootKind.Any;

            if (typeProp.ValueKind == JsonValueKind.String)
                return MapTypeName(typeProp.GetString());

            if (typeProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in typeProp.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String) continue;
                    var mapped = MapTypeName(item.GetString());
                    if (mapped != JsonRootKind.Any) return mapped;
                }
            }
        }
        catch (JsonException)
        {
            // Schema-bias is best-effort. A malformed schema simply disables the bias.
        }
        return JsonRootKind.Any;
    }

    private static JsonRootKind MapTypeName(string? name) => name switch
    {
        "object" => JsonRootKind.Object,
        "array" => JsonRootKind.Array,
        _ => JsonRootKind.Any,
    };

    /// <summary>
    /// Back-compat shim: returns the first extracted JSON candidate without parsing.
    /// New code should call <see cref="EnumerateJsonCandidates"/> and try each candidate.
    /// </summary>
    internal static string? ExtractJsonPayload(string text)
        => EnumerateJsonCandidates(text, JsonRootKind.Any).FirstOrDefault();

    /// <summary>
    /// Yields JSON candidate substrings extracted from a free-form assistant message,
    /// in the order they should be tried. Handles raw JSON (starts with <c>{</c> or
    /// <c>[</c>), markdown code fences (<c>```json … ```</c>), and prose-wrapped
    /// payloads (every balanced <c>{…}</c>/<c>[…]</c> block in scan order).
    /// When <paramref name="expectedRoot"/> is <see cref="JsonRootKind.Object"/> or
    /// <see cref="JsonRootKind.Array"/>, candidates of the matching root kind are tried
    /// before the others — this avoids picking up a stray <c>[P1]</c> bracket from
    /// markdown when the schema declares <c>type: object</c>. Internal for test visibility.
    /// </summary>
    internal static IEnumerable<string> EnumerateJsonCandidates(string text, JsonRootKind expectedRoot)
    {
        // TrimStart('\uFEFF') is defensive: some local models (gemma4 via llama.cpp) emit
        // U+FEFF at the start of every response, which Trim() does not always consume and
        // which would otherwise survive into the written JSON as EF BB BF.
        var trimmed = text.TrimStart('\uFEFF').Trim();
        if (trimmed.Length == 0) yield break;

        var seen = new HashSet<string>(StringComparer.Ordinal);

        if ((trimmed[0] == '{' || trimmed[0] == '[') && seen.Add(trimmed))
        {
            yield return trimmed;
        }

        var fence = FindCodeFence(trimmed);
        if (fence is not null && seen.Add(fence)) yield return fence;

        foreach (var block in EnumerateBalancedBlocks(trimmed, expectedRoot).Where(seen.Add))
        {
            yield return block;
        }
    }

    private static string? FindCodeFence(string text)
    {
        const string openJson = "```json";
        const string openBare = "```";

        var bodyStart = LocateFenceBodyStart(text, openJson, openBare);
        if (bodyStart < 0) return null;

        var end = text.IndexOf(openBare, bodyStart, StringComparison.Ordinal);
        return end < 0 ? null : text[bodyStart..end].Trim();
    }

    private static int LocateFenceBodyStart(string text, string openJson, string openBare)
    {
        var start = text.IndexOf(openJson, StringComparison.OrdinalIgnoreCase);
        if (start >= 0) return start + openJson.Length;

        start = text.IndexOf(openBare, StringComparison.Ordinal);
        return start >= 0 ? start + openBare.Length : -1;
    }

    private static IEnumerable<string> EnumerateBalancedBlocks(string text, JsonRootKind preferred)
    {
        var blocks = ScanAllBalancedBlocks(text).ToList();

        if (preferred == JsonRootKind.Object)
        {
            foreach (var b in blocks.Where(b => b.kind == '{')) yield return b.block;
            foreach (var b in blocks.Where(b => b.kind == '[')) yield return b.block;
        }
        else if (preferred == JsonRootKind.Array)
        {
            foreach (var b in blocks.Where(b => b.kind == '[')) yield return b.block;
            foreach (var b in blocks.Where(b => b.kind == '{')) yield return b.block;
        }
        else
        {
            foreach (var b in blocks) yield return b.block;
        }
    }

    private static IEnumerable<(int start, char kind, string block)> ScanAllBalancedBlocks(string text)
    {
        // Cap on the number of candidates we will yield, to bound work on pathological
        // inputs (e.g. a message that is mostly Markdown checklists with hundreds of
        // `[x]` brackets). 32 candidates is more than enough for any realistic LLM
        // response that mixes prose and JSON.
        const int maxCandidates = 32;
        var emitted = 0;
        var i = 0;

        while (i < text.Length && emitted < maxCandidates)
        {
            var nextObj = text.IndexOf('{', i);
            var nextArr = text.IndexOf('[', i);
            if (nextObj < 0 && nextArr < 0) yield break;

            int next;
            char openChar, closeChar;
            if (nextObj >= 0 && (nextArr < 0 || nextObj < nextArr))
            { next = nextObj; openChar = '{'; closeChar = '}'; }
            else
            { next = nextArr; openChar = '['; closeChar = ']'; }

            var block = ScanBalancedBlock(text, next, openChar, closeChar);
            if (block is null)
            {
                // Unbalanced opener — skip past it and keep scanning.
                i = next + 1;
                continue;
            }

            yield return (next, openChar, block);
            emitted++;
            i = next + block.Length;
        }
    }

    private static string? ScanBalancedBlock(string text, int open, char openChar, char closeChar)
    {
        var depth = 0;
        var inString = false;
        for (var i = open; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                inString = AdvanceInString(text, c, ref i);
                continue;
            }
            if (c == '"') { inString = true; continue; }
            if (c == openChar) depth++;
            else if (c == closeChar && --depth == 0)
                return text[open..(i + 1)];
        }
        return null;
    }

    /// <summary>
    /// Processes a single character while inside a JSON string literal. Skips the
    /// character after a backslash escape and returns whether the scanner is still
    /// inside the string (<c>false</c> once the closing quote is consumed).
    /// </summary>
    private static bool AdvanceInString(string text, char c, ref int i)
    {
        if (c == '\\' && i + 1 < text.Length) { i++; return true; }
        return c != '"';
    }

    private static string Preview(string candidate)
    {
        const int max = 80;
        var s = candidate.Length <= max ? candidate : candidate[..max] + "…";
        return s.Replace('\n', ' ').Replace('\r', ' ');
    }

    // ── Structured logging ─────────────────────────────────────────────────

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "Task {TaskId}: final message is empty, structured deliverable {Path} not persisted (raw_len={RawLength}, raw_hash={RawHash}).")]
    partial void LogEmptyFinalMessage(string taskId, string path, int rawLength, string rawHash);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning,
        Message = "Task {TaskId}: no JSON payload detected in final message for {Path} (raw_len={RawLength}, raw_hash={RawHash}).")]
    partial void LogNoJsonFound(string taskId, string path, int rawLength, string rawHash);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning,
        Message = "Task {TaskId}: final message for {Path} is not valid JSON ({Reason}). First rejected candidate begins: {Preview} (raw_len={RawLength}, raw_hash={RawHash}).")]
    partial void LogInvalidJson(string taskId, string path, string reason, string preview, int rawLength, string rawHash);

    [LoggerMessage(EventId = 7, Level = LogLevel.Warning,
        Message = "Task {TaskId}: partial extraction persisted at {Path} — chosen JSON candidate does not satisfy required top-level keys: {RequiredKeys}. Downstream consumers must handle partial_extraction=true.")]
    partial void LogPartialExtraction(string taskId, string path, string requiredKeys);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning,
        Message = "Task {TaskId}: access denied for structured deliverable {Path} ({Reason}).")]
    partial void LogAccessDenied(string taskId, string path, string reason);

    [LoggerMessage(EventId = 5, Level = LogLevel.Information,
        Message = "Task {TaskId}: structured deliverable persisted at {Path} ({Bytes} bytes).")]
    partial void LogPersisted(string taskId, string path, int bytes);

    [LoggerMessage(EventId = 6, Level = LogLevel.Warning,
        Message = "Task {TaskId}: R36-P3 zero-invention detected. {JsonPath} declares commands_table_rows=0 but {DocumentPath} actually contains {ActualRows} numbered table rows. The Supervisor agent ignored its own count_pattern result and emitted 0; the FINAL_SUMMARY scoring is wrong but the underlying deliverable is intact. Strengthen the supervisor's count_pattern_fidelity prompt or treat this run as a self-grading regression.")]
    partial void LogZeroInventionDetected(string taskId, string jsonPath, string documentPath, int actualRows);
}