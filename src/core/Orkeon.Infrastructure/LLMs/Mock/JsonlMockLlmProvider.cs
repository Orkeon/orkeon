using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Infrastructure.LLMs.Mock;

/// <summary>
/// A provider that answers from a JSONL file instead of a model: one <c>{match, regex?, content,
/// toolCalls?}</c> object per line, the first line whose <c>match</c> fits the prompt wins. It makes
/// no network call, so a crew, a YAML file or an <c>.ork.ts</c> script runs offline and gives the same
/// output every time (<c>Provider: mock</c>, <c>orkeon run --llm-mock</c>).
/// </summary>
/// <remarks>
/// <para>
/// <c>match</c> is looked for, ignoring case, in the last user message (the prompt of a
/// <c>GenerateAsync</c> call); with <c>"regex": true</c> it is a regular expression, and <c>$1</c> or
/// <c>${name}</c> in <c>content</c> are replaced by the groups it captured (<c>$$</c> writes a dollar).
/// A line with <c>toolCalls</c> (<c>[{"name": ..., "arguments": {...}}]</c>) answers with those calls,
/// in the OpenAI response shape the framework's tool-call parser reads.
/// </para>
/// <para>
/// The lines that match a prompt are its script, one line per round: the first answers the prompt, and
/// each round of tool results the conversation holds after it moves on to the next. A script of two
/// tool calls and a final answer is three lines that match the same prompt. When the conversation is
/// past the last line, a last line with <c>content</c> keeps answering; a last line with
/// <c>toolCalls</c> fails the call (the script ran out), so a mock never asks for a tool forever.
/// </para>
/// <para>
/// A prompt no line fits, a missing file or a malformed line is a failed call (the response carries
/// the error), never an empty answer. Streaming is emulated: the whole answer is one chunk.
/// </para>
/// </remarks>
public sealed class JsonlMockLlmProvider : ILlmProvider, IStreamingLlmProvider
{
    private static readonly TimeSpan s_regexTimeout = TimeSpan.FromSeconds(1);

    private readonly IFileSystemService _fileSystem;
    private readonly string _path;
    private readonly Lazy<Task<LoadResult>> _entries;

    /// <summary>Creates a provider over the JSONL file at <paramref name="responsesPath"/>.</summary>
    /// <param name="fileSystem">The virtual file system the file is read through.</param>
    /// <param name="responsesPath">The virtual path of the JSONL file; it is read on the first call.</param>
    public JsonlMockLlmProvider(IFileSystemService fileSystem, string responsesPath)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentException.ThrowIfNullOrWhiteSpace(responsesPath);
        _fileSystem = fileSystem;
        _path = responsesPath;
        _entries = new Lazy<Task<LoadResult>>(() => LoadAsync(CancellationToken.None));
    }

    /// <inheritdoc />
    public string Name => "mock";

    /// <summary>
    /// Declares what the file can honour: plain text, and tool calls in the OpenAI response shape. No
    /// response format, thinking, vision, cache or grammar is honoured, so none is declared.
    /// </summary>
    public LlmProviderCapabilities Capabilities { get; } = new()
    {
        ResponseFormat = ResponseFormatSupport.None,
        Thinking = ThinkingSupport.None,
        EffortLevels = [],
    };

    /// <inheritdoc />
    public bool SupportsStreaming => true;

    /// <inheritdoc />
    public Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default) =>
        AnswerAsync(prompt ?? string.Empty, rounds: 0);

    /// <inheritdoc />
    public Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var lastUser = Array.FindLastIndex(messages, m => m.Role == "user");
        var prompt = lastUser < 0 ? string.Empty : messages[lastUser].Content;
        return AnswerAsync(prompt, ToolRounds(messages, lastUser));
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> GenerateStreamingAsync(
        string prompt, LlmConfig? config = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GenerateAsync(prompt, config, cancellationToken).ConfigureAwait(false);
        if (response.Error is { } error)
            throw new InvalidOperationException(error);

        yield return response.Content;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<LlmStreamEvent> ChatStreamingAsync(
        LlmMessage[] messages, LlmConfig? config = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await ChatAsync(messages, config, cancellationToken).ConfigureAwait(false);
        if (response.Error is null && response.Content.Length > 0)
            yield return LlmStreamEvent.Content(response.Content);
        yield return LlmStreamEvent.Complete(response);
    }

    /// <summary>The rounds of tool results after the last user message: each run of tool messages is one.</summary>
    private static int ToolRounds(LlmMessage[] messages, int lastUser)
    {
        var rounds = 0;
        var inRound = false;
        for (var i = lastUser + 1; i < messages.Length && lastUser >= 0; i++)
        {
            if (messages[i].Role == "tool")
            {
                if (!inRound)
                    rounds++;
                inRound = true;
            }
            else if (messages[i].Role == "assistant")
            {
                inRound = false;
            }
        }

        return rounds;
    }

    private async Task<LlmResponse> AnswerAsync(string prompt, int rounds)
    {
        var loaded = await _entries.Value.ConfigureAwait(false);
        if (loaded.Error is not null)
            return Failed(loaded.Error);

        var script = new List<(Entry Entry, string Content)>();
        foreach (var entry in loaded.Entries)
        {
            try
            {
                if (entry.TryMatch(prompt) is { } content)
                    script.Add((entry, content));
            }
            catch (RegexMatchTimeoutException)
            {
                return Failed($"The pattern of line {entry.LineNumber} of {_path} timed out on the prompt: {Excerpt(prompt)}");
            }
        }

        if (script.Count == 0)
            return Failed($"No line of {_path} matches the prompt: {Excerpt(prompt)}");

        var (chosen, text) = script[Math.Min(rounds, script.Count - 1)];
        if (rounds >= script.Count && chosen.ToolCallsBody is not null)
            return Failed($"The lines of {_path} that match the prompt ran out after {script.Count} round(s) of tool results, and the last one still asks for a tool: add a line with the final answer.");

        return new LlmResponse
        {
            Content = text,
            Model = Name,
            RawResponseBody = chosen.ToolCallsBody,
        };
    }

    private static LlmResponse Failed(string error) => new()
    {
        Model = "mock",
        Metadata = new Dictionary<string, object> { [LlmResponseMetadataKeys.Error] = error },
    };

    private static string Excerpt(string text) => text.Length <= 80 ? text : text[..80] + "...";

    private async Task<LoadResult> LoadAsync(CancellationToken ct)
    {
        string? text;
        try
        {
            text = await _fileSystem.TryReadAllTextAsync(_path, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            return LoadResult.Fail($"The mock responses file {_path} cannot be read: {ex.Message}");
        }

        if (text is null)
            return LoadResult.Fail($"The mock responses file {_path} does not exist.");

        var entries = new List<Entry>();
        var number = 0;
        foreach (var line in text.Split('\n'))
        {
            number++;
            if (string.IsNullOrWhiteSpace(line))
                continue;
            try
            {
                entries.Add(Entry.Parse(line, number));
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException)
            {
                return LoadResult.Fail($"{_path} line {number} is not a mock response ({ex.Message}).");
            }
        }

        return new LoadResult(entries, null);
    }

    private sealed record LoadResult(IReadOnlyList<Entry> Entries, string? Error)
    {
        public static LoadResult Fail(string error) => new([], error);
    }

    private sealed class Entry
    {
        private readonly string _match;
        private readonly Regex? _regex;

        private readonly string _content;

        private Entry(string match, Regex? regex, string content, string? toolCallsBody, int lineNumber)
        {
            _match = match;
            _regex = regex;
            _content = content;
            ToolCallsBody = toolCallsBody;
            LineNumber = lineNumber;
        }

        public string? ToolCallsBody { get; }

        public int LineNumber { get; }

        /// <summary>The answer text when the line fits <paramref name="prompt"/>, else null.</summary>
        /// <exception cref="RegexMatchTimeoutException">The line's pattern ran out of time.</exception>
        public string? TryMatch(string prompt)
        {
            if (_regex is null)
                return prompt.Contains(_match, StringComparison.OrdinalIgnoreCase) ? _content : null;

            var match = _regex.Match(prompt);
            return match.Success ? match.Result(_content) : null;
        }

        public static Entry Parse(string line, int lineNumber)
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("an object is expected");

            var match = root.TryGetProperty("match", out var m) && m.ValueKind == JsonValueKind.String
                ? m.GetString()!
                : throw new InvalidOperationException("'match' is required");
            var isRegex = root.TryGetProperty("regex", out var r) && r.ValueKind == JsonValueKind.True;
            var content = root.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String
                ? c.GetString()!
                : string.Empty;

            string? body = null;
            if (root.TryGetProperty("toolCalls", out var calls) && calls.ValueKind == JsonValueKind.Array && calls.GetArrayLength() > 0)
                body = BuildToolCallsBody(calls, content);
            else if (content.Length == 0)
                throw new InvalidOperationException("'content' or 'toolCalls' is required");

            return new Entry(
                match,
                isRegex ? new Regex(match, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, s_regexTimeout) : null,
                content,
                body,
                lineNumber);
        }

        private static string BuildToolCallsBody(JsonElement calls, string content)
        {
            var list = new List<object?>();
            var index = 0;
            foreach (var call in calls.EnumerateArray())
            {
                var name = call.GetProperty("name").GetString()
                    ?? throw new InvalidOperationException("a tool call needs a 'name'");
                var arguments = call.TryGetProperty("arguments", out var a) ? a.GetRawText() : "{}";
                list.Add(new Dictionary<string, object?>
                {
                    ["id"] = $"mock_call_{index++}",
                    ["type"] = "function",
                    ["function"] = new Dictionary<string, object?> { ["name"] = name, ["arguments"] = arguments },
                });
            }

            return JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["choices"] = new List<object?>
                {
                    new Dictionary<string, object?>
                    {
                        ["message"] = new Dictionary<string, object?>
                        {
                            ["role"] = "assistant",
                            ["content"] = content.Length == 0 ? null : content,
                            ["tool_calls"] = list,
                        },
                        ["finish_reason"] = "tool_calls",
                    },
                },
            });
        }
    }
}
