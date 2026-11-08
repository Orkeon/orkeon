using System.Diagnostics.CodeAnalysis;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using Orkeon.Domain.Exceptions;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Infrastructure.LLMs.Replay;

/// <summary>
/// The cassette of one run: a JSONL file of the calls a model answered, written (record) or read
/// (replay) through the virtual file system. One line per call: its profile, its key, its occurrence
/// number among the calls of that key, the answer and, for a streamed call, the list of deltas.
/// </summary>
[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
    Justification = "The semaphore never exposes its wait handle, so it holds no unmanaged resource; the cassette lives as long as the factory.")]
internal sealed class LlmCassette
{
    private const string Mark = CassetteRedaction.Mark;

    private static readonly JsonSerializerOptions s_json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly IFileSystemService _fileSystem;
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, int> _recorded = [];
    private readonly Dictionary<string, int> _taken = [];
    private bool _started;
    private Dictionary<string, List<Line>>? _loaded;

    public LlmCassette(IFileSystemService fileSystem, string path)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _fileSystem = fileSystem;
        _path = path;
    }

    /// <summary>Appends one answered call; the first call of the run starts the file afresh.</summary>
    public async Task RecordAsync(
        string profile, string key, LlmResponse? response, IReadOnlyList<LlmStreamEvent>? events,
        IEnumerable<string?> secrets, CancellationToken ct, Exception? failure = null)
    {
        var line = new Line
        {
            Version = 1,
            Profile = profile,
            Key = key,
            Response = response is null ? null : ResponseRecord.From(response),
            Failure = failure is null ? null : FailureRecord.From(failure),
            Deltas = events?.Select(DeltaRecord.From).ToList(),
        };

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            line.Occurrence = _recorded.GetValueOrDefault(key);
            _recorded[key] = line.Occurrence + 1;
            var known = KnownSecrets(secrets);
            JoinSplitSecrets(line.Deltas, known);
            var text = Walk(JsonSerializer.SerializeToNode(line, s_json)!, known).ToJsonString(s_json) + "\n";
            if (!_started)
            {
                await _fileSystem.WriteAllTextAsync(_path, string.Empty, ct).ConfigureAwait(false);
                _started = true;
            }

            await _fileSystem.AppendAllTextAsync(_path, text, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>The next recorded answer of <paramref name="key"/>; none left is a <see cref="ReplayMissException"/>.</summary>
    public async Task<TakenCall> TakeAsync(
        string profile, string key, string call, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _loaded ??= await LoadAsync(ct).ConfigureAwait(false);
            var taken = _taken.GetValueOrDefault(key);
            if (!_loaded.TryGetValue(key, out var lines) || taken >= lines.Count)
            {
                var held = lines?.Count ?? 0;
                throw new ReplayMissException(
                    $"The cassette {_path} holds no answer for call number {taken + 1} of profile '{profile}' "
                    + $"(key {key[..Math.Min(12, key.Length)]}, {held} recorded for it): {call}");
            }

            _taken[key] = taken + 1;
            var line = lines[taken];
            return new TakenCall(
                line.Response?.ToResponse() ?? new LlmResponse(),
                line.Deltas?.Select(d => d.ToEvent()).ToList(),
                line.Failure?.ToException());
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Dictionary<string, List<Line>>> LoadAsync(CancellationToken ct)
    {
        var text = await _fileSystem.TryReadAllTextAsync(_path, ct).ConfigureAwait(false)
            ?? throw new ReplayMissException($"The cassette {_path} does not exist: record one with --llm-record first.");
        var result = new Dictionary<string, List<Line>>(StringComparer.Ordinal);
        var number = 0;
        foreach (var raw in text.Split('\n'))
        {
            number++;
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            Line? line;
            try
            {
                line = JsonSerializer.Deserialize<Line>(raw, s_json);
            }
            catch (JsonException ex)
            {
                throw new ReplayMissException($"Line {number} of the cassette {_path} is not a recorded call ({ex.Message}).", ex);
            }

            if (line?.Key is null || (line.Response is null && line.Failure is null))
                throw new ReplayMissException($"Line {number} of the cassette {_path} is not a recorded call.");
            if (!result.TryGetValue(line.Key, out var list))
                result[line.Key] = list = [];
            list.Add(line);
        }

        return result;
    }

    /// <summary>Redacts every string value of the line, so what is written is valid JSON whatever the values held.</summary>
    private static List<string> KnownSecrets(IEnumerable<string?> secrets) =>
        secrets.Where(x => !string.IsNullOrEmpty(x) && x.Length >= 6).Distinct(StringComparer.Ordinal).ToList()!;

    /// <summary>The text with the configured secrets and every secret-shaped value replaced by <see cref="Mark"/>.</summary>
    private static string RedactText(string text, List<string> secrets)
    {
        foreach (var secret in secrets)
            text = text.Replace(secret, Mark, StringComparison.Ordinal);
        return CassetteRedaction.Text(text);
    }

    /// <summary>
    /// A secret split across deltas: redacted one by one, no delta holds the whole value, so neither
    /// the configured key nor a secret shape matches and the parts would be stored in clear. Each
    /// stream (the content, the reasoning, the arguments of each tool call) is redacted whole; when
    /// that removes more than redacting its deltas one by one, the stream is stored as a single
    /// delta holding the redacted text - the replay yields the same text in fewer pieces.
    /// </summary>
    private static void JoinSplitSecrets(List<DeltaRecord>? deltas, List<string> secrets)
    {
        if (deltas is null)
            return;

        foreach (var stream in deltas.Where(d => d.Delta is not null).GroupBy(d => d.Kind, StringComparer.Ordinal))
            JoinIfSplit(stream.ToList(), d => d.Delta, (d, v) => d.Delta = v, secrets);
        foreach (var stream in deltas.Where(d => d.Index is not null && d.ArgumentsDelta is not null).GroupBy(d => d.Index))
            JoinIfSplit(stream.ToList(), d => d.ArgumentsDelta, (d, v) => d.ArgumentsDelta = v, secrets);
    }

    private static void JoinIfSplit(List<DeltaRecord> stream, Func<DeltaRecord, string?> get, Action<DeltaRecord, string> set, List<string> secrets)
    {
        var parts = stream.Where(d => !string.IsNullOrEmpty(get(d))).ToList();
        if (parts.Count < 2)
            return;

        var whole = RedactText(string.Concat(parts.Select(get)), secrets);
        var pieceByPiece = string.Concat(parts.Select(d => RedactText(get(d)!, secrets)));
        if (string.Equals(whole, pieceByPiece, StringComparison.Ordinal))
            return;

        set(parts[0], whole);
        foreach (var part in parts.Skip(1))
            set(part, string.Empty);
    }

    private static void ReplaceIfChanged(JsonNode before, JsonNode after, Action<JsonNode> assign)
    {
        if (!ReferenceEquals(before, after))
            assign(after);
    }

    private static JsonNode Walk(JsonNode node, List<string> secrets)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var name in obj.Select(p => p.Key).ToList())
                {
                    if (obj[name] is { } child)
                        ReplaceIfChanged(child, Walk(child, secrets), replacement => obj[name] = replacement);
                }

                return obj;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    if (array[i] is { } child)
                        ReplaceIfChanged(child, Walk(child, secrets), replacement => array[i] = replacement);
                }

                return array;
            case JsonValue value when value.TryGetValue<string>(out var text):
                foreach (var secret in secrets)
                    text = text.Replace(secret, Mark, StringComparison.Ordinal);
                return JsonValue.Create(CassetteRedaction.Text(text))!;
            default:
                return node;
        }
    }

    /// <summary>What a cassette held for one call: its answer, its deltas, and the failure it ended with, if any.</summary>
    /// <param name="Response">The answer (empty when the call failed).</param>
    /// <param name="Events">The recorded deltas of a streamed call.</param>
    /// <param name="Failure">The failure to raise, or null.</param>
    internal sealed record TakenCall(LlmResponse Response, IReadOnlyList<LlmStreamEvent>? Events, Exception? Failure);

    private sealed class Line
    {
        public int Version { get; set; }
        public string? Profile { get; set; }
        public string? Key { get; set; }
        public int Occurrence { get; set; }
        public ResponseRecord? Response { get; set; }
        public FailureRecord? Failure { get; set; }
        public List<DeltaRecord>? Deltas { get; set; }
    }

    private sealed class FailureRecord
    {
        public string Kind { get; set; } = "other";
        public string Message { get; set; } = string.Empty;
        public int? Status { get; set; }
        public double? WaitSeconds { get; set; }

        public static FailureRecord From(Exception e) => e switch
        {
            LlmRateLimitException r => new() { Kind = "rate_limit", Message = r.Message, WaitSeconds = r.Wait.TotalSeconds },
            HttpRequestException h => new() { Kind = "http", Message = h.Message, Status = (int?)h.StatusCode },
            SocketException or IOException => new() { Kind = "http", Message = e.Message },
            System.TimeoutException or OperationCanceledException => new() { Kind = "timeout", Message = e.Message },
            _ => new() { Kind = "other", Message = e.Message },
        };

        public Exception ToException() => Kind switch
        {
            "rate_limit" => new LlmRateLimitException(Message, TimeSpan.FromSeconds(WaitSeconds ?? 0)),
            "http" => new HttpRequestException(Message, null, (HttpStatusCode?)Status),
            "timeout" => new System.TimeoutException(Message),
            _ => new InvalidOperationException(Message),
        };
    }

    private sealed class ResponseRecord
    {
        public string Content { get; set; } = string.Empty;
        public int TokensUsed { get; set; }
        public int? PromptTokens { get; set; }
        public int? CompletionTokens { get; set; }
        public int? CacheHitTokens { get; set; }
        public int? CacheMissTokens { get; set; }
        public int? CacheWriteTokens { get; set; }
        public int? CacheWrite1hTokens { get; set; }
        public string? Model { get; set; }
        public Dictionary<string, JsonElement>? Metadata { get; set; }
        public string? RawResponseBody { get; set; }

        public static ResponseRecord From(LlmResponse r) => new()
        {
            Content = r.Content,
            TokensUsed = r.TokensUsed,
            PromptTokens = r.PromptTokens,
            CompletionTokens = r.CompletionTokens,
            CacheHitTokens = r.CacheHitTokens,
            CacheMissTokens = r.CacheMissTokens,
            CacheWriteTokens = r.CacheWriteTokens,
            CacheWrite1hTokens = r.CacheWrite1hTokens,
            Model = r.Model,
            Metadata = r.Metadata.Count == 0 ? null : r.Metadata.ToDictionary(p => p.Key, p => ToElement(p.Value)),
            RawResponseBody = r.RawResponseBody,
        };

        public LlmResponse ToResponse() => new()
        {
            Content = Content,
            TokensUsed = TokensUsed,
            PromptTokens = PromptTokens,
            CompletionTokens = CompletionTokens,
            CacheHitTokens = CacheHitTokens,
            CacheMissTokens = CacheMissTokens,
            CacheWriteTokens = CacheWriteTokens,
            CacheWrite1hTokens = CacheWrite1hTokens,
            Model = Model,
            Metadata = Metadata is null
                ? new Dictionary<string, object>()
                : Metadata.ToDictionary(p => p.Key, p => FromElement(p.Value)),
            RawResponseBody = RawResponseBody,
        };

        private static JsonElement ToElement(object value)
        {
            try
            {
                return JsonSerializer.SerializeToElement(value, s_json);
            }
            catch (NotSupportedException)
            {
                return JsonSerializer.SerializeToElement(value.ToString(), s_json);
            }
        }

        private static object FromElement(JsonElement e) => e.ValueKind switch
        {
            JsonValueKind.String => e.GetString() ?? string.Empty,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => e.TryGetInt64(out var l) ? l : e.GetDouble(),
            _ => e.GetRawText(),
        };
    }

    private sealed class DeltaRecord
    {
        public string Kind { get; set; } = string.Empty;
        public string? Delta { get; set; }
        public int? Index { get; set; }
        public string? CallId { get; set; }
        public string? Name { get; set; }
        public string? ArgumentsDelta { get; set; }

        public static DeltaRecord From(LlmStreamEvent e) => new()
        {
            Kind = e.Kind.ToString(),
            Delta = e.Delta,
            Index = e.ToolCall?.Index,
            CallId = e.ToolCall?.Id,
            Name = e.ToolCall?.Name,
            ArgumentsDelta = e.ToolCall?.ArgumentsDelta,
        };

        public LlmStreamEvent ToEvent() => new()
        {
            Kind = Enum.Parse<LlmStreamEventKind>(Kind),
            Delta = Delta,
            ToolCall = Index is { } index ? new ToolCallDelta(index, CallId, Name, ArgumentsDelta ?? string.Empty) : null,
        };
    }
}
