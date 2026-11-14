using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Orkeon.Scripting.Cli.Commands;

/// <summary>
/// What the reasoning probes (M15-M17) read back after each step: the reasoning fields the
/// provider really put on the wire, and the warnings it logged about them.
/// </summary>
internal interface IReasoningWireProbe
{
    /// <summary>Forgets the previous step: the captured fields and the warnings.</summary>
    void Reset();

    /// <summary>The reasoning fields of the last request, as a compact JSON object, or null when none was seen.</summary>
    string? LastFields { get; }

    /// <summary>The Warning-or-worse messages the provider loggers produced since <see cref="Reset"/>.</summary>
    IReadOnlyList<string> Warnings { get; }

    /// <summary>
    /// Sets the reasoning fields that replace Orkeon's own in every following request body, or
    /// clears the override when null. <see cref="Reset"/> leaves it alone.
    /// </summary>
    /// <param name="fragment">The fields to merge into the body, or null for no override.</param>
    void Override(JsonObject? fragment);

    /// <summary>
    /// What the vendor's last JSON answer itself carried (trace, reasoning tokens, finish reason),
    /// read from the response body before the provider parses it; null when none was read.
    /// </summary>
    VendorAnswer? LastAnswer { get; }
}

/// <summary>
/// The reasoning facts of one vendor answer, read on the wire: whether a non-empty reasoning
/// trace came back in any dialect, the reasoning tokens the usage block reported, and the
/// finish reason. The trace text itself is never kept.
/// </summary>
/// <param name="Trace">Whether the body carried a non-empty reasoning trace.</param>
/// <param name="ReasoningTokens">The reasoning tokens the usage block reported, when it did.</param>
/// <param name="FinishReason">The vendor's finish or stop reason, when the body carried one.</param>
internal sealed record VendorAnswer(bool Trace, long? ReasoningTokens, string? FinishReason);

/// <summary>
/// Keeps, for each JSON request that passes, only the top-level reasoning fields of its body.
/// Headers are never read and nothing else of the body is retained, so no credential and no
/// prompt can end up in a report.
/// </summary>
internal sealed class ReasoningWireRecorder : DelegatingHandler
{
    private static readonly string[] s_topLevelFields =
        ["thinking", "reasoning_effort", "reasoning", "think", "enable_thinking", "thinking_budget"];

    private static readonly JsonSerializerOptions s_writeOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly object _gate = new();
    private string? _last;
    private VendorAnswer? _answer;
    private JsonObject? _override;

    /// <summary>Initializes a recorder in front of a plain <see cref="HttpClientHandler"/>.</summary>
#pragma warning disable CA2000 // The DelegatingHandler owns and disposes its inner handler.
    public ReasoningWireRecorder()
        : base(new HttpClientHandler())
#pragma warning restore CA2000
    {
    }

    /// <summary>Initializes a recorder in front of <paramref name="inner"/>.</summary>
    /// <param name="inner">The handler that really sends the request.</param>
    public ReasoningWireRecorder(HttpMessageHandler inner)
        : base(inner)
    {
    }

    /// <summary>The fields captured from the last request, or null.</summary>
    public string? LastFields
    {
        get { lock (_gate) return _last; }
    }

    /// <summary>What the last JSON answer carried, or null.</summary>
    public VendorAnswer? LastAnswer
    {
        get { lock (_gate) return _answer; }
    }

    /// <summary>Sets the reasoning fields rewritten into every following body; null clears it.</summary>
    /// <param name="fragment">The fields to merge, or null.</param>
    public void Override(JsonObject? fragment)
    {
        lock (_gate)
            _override = fragment?.DeepClone() as JsonObject;
    }

    /// <summary>Forgets the captured fields.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _last = null;
            _answer = null;
        }
    }

    /// <summary>Extracts the reasoning fields of a request body; null for a non-JSON or empty body.</summary>
    /// <param name="body">The request body text.</param>
    /// <returns>A compact JSON object, or null when nothing relevant is there.</returns>
    internal static string? Extract(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            using var stream = new MemoryStream();
            var found = false;
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                foreach (var name in s_topLevelFields)
                {
                    if (!doc.RootElement.TryGetProperty(name, out var value))
                        continue;
                    writer.WritePropertyName(name);
                    value.WriteTo(writer);
                    found = true;
                }

                if (doc.RootElement.TryGetProperty("output_config", out var config)
                    && config.ValueKind == JsonValueKind.Object
                    && config.TryGetProperty("effort", out var effort))
                {
                    writer.WritePropertyName("output_config");
                    writer.WriteStartObject();
                    writer.WritePropertyName("effort");
                    effort.WriteTo(writer);
                    writer.WriteEndObject();
                    found = true;
                }

                if (doc.RootElement.TryGetProperty("chat_template_kwargs", out var kwargs)
                    && kwargs.ValueKind == JsonValueKind.Object
                    && kwargs.TryGetProperty("enable_thinking", out var enable))
                {
                    writer.WritePropertyName("chat_template_kwargs");
                    writer.WriteStartObject();
                    writer.WritePropertyName("enable_thinking");
                    enable.WriteTo(writer);
                    writer.WriteEndObject();
                    found = true;
                }

                writer.WriteEndObject();
            }

            return found ? System.Text.Encoding.UTF8.GetString(stream.ToArray()) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Strips every reasoning field Orkeon may write from a JSON object body, then deep-merges
    /// <paramref name="fragment"/>; null when the body is not a JSON object.
    /// </summary>
    /// <param name="body">The request body text.</param>
    /// <param name="fragment">The fields to merge.</param>
    /// <returns>The rewritten body, or null to leave the request untouched.</returns>
    internal static string? Rewrite(string body, JsonObject fragment)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        JsonObject? root;
        try
        {
            root = JsonNode.Parse(body) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }

        if (root is null)
            return null;

        foreach (var name in s_topLevelFields)
            root.Remove(name);
        RemoveNested(root, "output_config", "effort");
        RemoveNested(root, "chat_template_kwargs", "enable_thinking");
        Merge(root, fragment);
        return root.ToJsonString(s_writeOptions);
    }

    private static void RemoveNested(JsonObject root, string container, string key)
    {
        if (root[container] is not JsonObject inner)
            return;
        inner.Remove(key);
        if (inner.Count == 0)
            root.Remove(container);
    }

    private static void Merge(JsonObject target, JsonObject source)
    {
        foreach (var (key, value) in source)
        {
            if (value is JsonObject sourceChild && target[key] is JsonObject targetChild)
                Merge(targetChild, sourceChild);
            else
                target[key] = value?.DeepClone();
        }
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Content is { } content)
        {
            // Buffering keeps the content readable twice: here, then by the inner handler.
            await content.LoadIntoBufferAsync(cancellationToken).ConfigureAwait(false);
            var body = await content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            JsonObject? fragment;
            lock (_gate)
                fragment = _override;
            if (fragment is not null && Rewrite(body, fragment) is { } rewritten)
            {
                var original = content;
                request.Content = new StringContent(rewritten, Encoding.UTF8, "application/json");
                original.Dispose();
                body = rewritten;
            }

            var fields = Extract(body);
            lock (_gate)
                _last = fields;
        }

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        VendorAnswer? answer = null;
        if (response.IsSuccessStatusCode
            && response.Content.Headers.ContentType?.MediaType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true)
        {
            // Buffered so the provider still reads the whole body after this.
            await response.Content.LoadIntoBufferAsync(cancellationToken).ConfigureAwait(false);
            answer = ReadAnswer(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        }

        lock (_gate)
            _answer = answer;

        return response;
    }

    /// <summary>
    /// Reads the reasoning facts of a vendor answer in every dialect the providers speak: chat
    /// completions (<c>choices[0]</c>), Anthropic messages (<c>content[]</c>, <c>stop_reason</c>),
    /// Mistral's chunked <c>message.content</c>, the OpenAI Responses API (<c>output[]</c>, <c>status</c>), Gemini (<c>candidates[0]</c>,
    /// <c>usageMetadata</c>) and Ollama (<c>message</c>, <c>done_reason</c>).
    /// </summary>
    /// <param name="body">The response body text.</param>
    /// <returns>The facts read, or null for a body that is not a JSON object.</returns>
    internal static VendorAnswer? ReadAnswer(string body)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            return null;
        }

        if (root is not JsonObject answer)
            return null;

        var choice = (answer["choices"] as JsonArray)?.FirstOrDefault() as JsonObject;
        var candidate = (answer["candidates"] as JsonArray)?.FirstOrDefault() as JsonObject;
        var message = choice?["message"] as JsonObject ?? answer["message"] as JsonObject;

        var trace = HasText(message, "reasoning_content")
            || HasText(message, "reasoning")
            || HasText(message, "thinking")
            || (message?["reasoning_details"] is JsonArray { Count: > 0 })
            || (message?["content"] is JsonValue content
                && content.TryGetValue<string>(out var text)
                && text.Contains("<think>", StringComparison.Ordinal))
            || Blocks(answer["content"]).Any(IsThinkingBlock)
            || Blocks(message?["content"]).Any(IsThinkingBlock)
            || Blocks(answer["output"]).Any(b => Type(b) is "reasoning"
                && (b["summary"] is JsonArray { Count: > 0 } || b["content"] is JsonArray { Count: > 0 }))
            || Blocks((candidate?["content"] as JsonObject)?["parts"]).Any(b => b["thought"] is JsonValue thought
                && thought.TryGetValue<bool>(out var isThought) && isThought);

        var usage = answer["usage"] as JsonObject;
        var tokens = Count((usage?["completion_tokens_details"] as JsonObject)?["reasoning_tokens"])
            ?? Count((usage?["output_tokens_details"] as JsonObject)?["reasoning_tokens"])
            ?? Count(usage?["reasoning_tokens"])
            ?? Count((answer["usageMetadata"] as JsonObject)?["thoughtsTokenCount"]);

        var finish = Text(choice?["finish_reason"])
            ?? Text(answer["stop_reason"])
            ?? Text(candidate?["finishReason"])
            ?? Text(answer["done_reason"])
            ?? Text((answer["incomplete_details"] as JsonObject)?["reason"])
            ?? (answer["output"] is JsonArray ? Text(answer["status"]) : null);

        return new VendorAnswer(trace, tokens, finish);
    }

    // Anthropic: {"type":"thinking","thinking":"..."} or redacted_thinking; Mistral: a content
    // chunk {"type":"thinking","thinking":[{"type":"text","text":"..."}]}.
    private static bool IsThinkingBlock(JsonObject block) =>
        Type(block) is "redacted_thinking"
        || (Type(block) is "thinking" && (HasText(block, "thinking") || block["thinking"] is JsonArray { Count: > 0 }));

    private static IEnumerable<JsonObject> Blocks(JsonNode? node) =>
        node is JsonArray array ? array.OfType<JsonObject>() : [];

    private static string? Type(JsonObject block) => Text(block["type"]);

    private static bool HasText(JsonObject? owner, string name) =>
        Text(owner?[name]) is { Length: > 0 } text && !string.IsNullOrWhiteSpace(text);

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static long? Count(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<long>(out var count) ? count : null;
}

/// <summary>Keeps the Warning-or-worse messages of every logger it hands out.</summary>
internal sealed class CapturingLoggerFactory : ILoggerFactory
{
    private readonly object _gate = new();
    private readonly List<string> _messages = [];

    /// <summary>The captured messages, in order.</summary>
    public IReadOnlyList<string> Messages
    {
        get { lock (_gate) return [.. _messages]; }
    }

    /// <summary>Forgets the captured messages.</summary>
    public void Clear()
    {
        lock (_gate)
            _messages.Clear();
    }

    /// <inheritdoc />
    public void AddProvider(ILoggerProvider provider)
    {
    }

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

    /// <inheritdoc />
    public void Dispose()
    {
    }

    private void Add(string message)
    {
        lock (_gate)
            _messages.Add(message);
    }

    private sealed class CapturingLogger(CapturingLoggerFactory owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;
            owner.Add(formatter(state, exception));
        }
    }
}

/// <summary>The wire recorder and the warning capture of one probe run, read as one.</summary>
internal sealed class ReasoningWireProbe : IReasoningWireProbe
{
    private ReasoningWireRecorder? _recorder;
    private JsonObject? _override;

    /// <summary>Initializes the probe; hand <see cref="LoggerFactory"/> and the HTTP factory's recorder to the provider.</summary>
    public ReasoningWireProbe()
    {
        LoggerFactory = new CapturingLoggerFactory();
    }

    /// <summary>The recorder installed in front of the HTTP handler; set by <see cref="CliHttpClientFactory"/>.</summary>
    public ReasoningWireRecorder? Recorder
    {
        get => _recorder;
        internal set
        {
            _recorder = value;
            value?.Override(_override);
        }
    }

    /// <summary>The factory the provider's loggers come from.</summary>
    public CapturingLoggerFactory LoggerFactory { get; }

    /// <inheritdoc />
    public string? LastFields => Recorder?.LastFields;

    /// <inheritdoc />
    public IReadOnlyList<string> Warnings => LoggerFactory.Messages;

    /// <inheritdoc />
    public VendorAnswer? LastAnswer => Recorder?.LastAnswer;

    /// <inheritdoc />
    public void Override(JsonObject? fragment)
    {
        _override = fragment;
        Recorder?.Override(fragment);
    }

    /// <inheritdoc />
    public void Reset()
    {
        Recorder?.Reset();
        LoggerFactory.Clear();
    }
}
