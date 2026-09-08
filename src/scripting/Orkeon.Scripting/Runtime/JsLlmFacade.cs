using System.Text.Json;
using Jint;
using Jint.Native;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Tools;
using Orkeon.Scripting.Telemetry;
using ToolCallMode = Orkeon.Domain.Tools.Protocol.ToolCallMode;
using ProtocolToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;

namespace Orkeon.Scripting.Runtime;

/// <summary>
/// Surface exposed to JS as <c>ctx.llm</c>. Wraps an injected
/// <see cref="ILlmProvider"/> when available; otherwise falls back to an "undefined
/// LLM" echo behaviour described in chapter 03 §<c>UndefinedLlm</c>.
/// </summary>
#pragma warning disable IDE1006
#pragma warning disable CS1591 // JS-interop mirror of LlmFacade (ctx.llm) in Typings/context.d.ts; that declaration is the contract scripts read.
public sealed partial class JsLlmFacade
{
    private const int DefaultActMaxIterations = 10;

    private readonly Engine _engine;
    private readonly ILlmProvider? _provider;
    private readonly CancellationToken _hostCt;
    private readonly CancellationTokenSource _cts;
    private readonly CancellationToken _ct;
    private readonly IReadOnlyList<IBaseTool> _tools;
    private readonly Orkeon.Domain.Autonomous.AgentExecutionBudget? _budget;
    private readonly Orkeon.Application.Interfaces.Security.IPermissionGate? _permissionGate;
    private readonly Orkeon.Application.Interfaces.Ports.ILlmDeltaSink? _deltaSink;
    private readonly Microsoft.Extensions.Logging.ILogger? _logger;
    private readonly Orkeon.Application.Interfaces.Ports.ILlmUsageSink? _usageSink;
    private readonly string _crewName;
    private readonly string _agentName;

    internal JsLlmFacade(
        Engine engine,
        ILlmProvider? provider,
        CancellationToken ct,
        IReadOnlyList<IBaseTool>? tools = null,
        Orkeon.Domain.Autonomous.AgentExecutionBudget? budget = null,
        Orkeon.Application.Interfaces.Security.IPermissionGate? permissionGate = null,
        JsLlmObservability? observability = null)
    {
        _engine = engine;
        _provider = provider;
        _hostCt = ct;
        // Linked source so ctx.llm.interrupt() cancels in-flight/future llm calls of this
        // context without touching the host token; host cancellation keeps its hard
        // OperationCanceledException semantics.
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ct = _cts.Token;
        _tools = tools ?? System.Array.Empty<IBaseTool>();
        _budget = budget;
        _permissionGate = permissionGate;
        _deltaSink = observability?.DeltaSink;
        _logger = observability?.Logger;
        _usageSink = observability?.UsageSink;
        _crewName = observability?.CrewName ?? string.Empty;
        _agentName = observability?.AgentName ?? string.Empty;
        embed = EmbedAsync;
        act = ActAsync;
    }

    /// <summary>
    /// Reports one completed LLM call to the host's usage sink. One call per response,
    /// per path — <c>act</c> reports each iteration's response HERE, never inside
    /// <see cref="ChatViaStreamAsync"/> (which only assembles it), so a streamed
    /// iteration counts exactly once. Best-effort: a throwing sink degrades to
    /// unobserved usage, never to a failed LLM call.
    /// <para>
    /// A response that carried NO usage at all is estimated rather than dropped
    /// (<see cref="Orkeon.Infrastructure.CostTracking.LlmUsageEstimator"/>): several OpenAI-compatible endpoints answer without
    /// a <c>usage</c> block, and the meter used to stand at zero through an entire
    /// session because of it. The event then says <c>Estimated</c>, so a screen can mark
    /// the figure and a budget knows what it is consuming. "No usage" still never reads
    /// as "zero tokens" — it reads as an approximation, which is what it is.
    /// </para>
    /// </summary>
    /// <param name="response">The provider's answer; null reports nothing.</param>
    /// <param name="method">The <c>ctx.llm.*</c> entry point, for the operation type.</param>
    /// <param name="promptText">The single prompt sent, when the call had one.</param>
    /// <param name="promptMessages">The conversation sent, when the call had one instead.</param>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Host-sink fault barrier: usage accounting must never fail the LLM call it observes.")]
    private void ReportUsage(
        LlmResponse? response,
        string method,
        string? promptText = null,
        IReadOnlyList<LlmMessage>? promptMessages = null)
    {
        if (_usageSink is null || response is null)
            return;

        var estimated = !Orkeon.Infrastructure.CostTracking.LlmUsageEstimator.Reported(response);
        try
        {
            var prompt = estimated
                ? EstimatePromptTokens(promptText, promptMessages)
                : response.PromptTokens ?? 0;
            _usageSink.Record(new Orkeon.Application.Interfaces.Ports.CostUsageEvent
            {
                CrewId = _crewName,
                AgentId = _agentName,
                Model = response.Model ?? string.Empty,
                Provider = _provider?.Name ?? string.Empty,
                PromptTokens = prompt,
                Estimated = estimated,
                // Providers that report only a grand total leave the split null; the
                // remainder keeps PromptTokens + CompletionTokens == TokensUsed (the
                // sum is what ICostBudgetManager aggregates as TotalTokens). Known
                // bias: the pricing registry then charges that whole total at the
                // OUTPUT rate — a deliberate upper bound (a budget trips too early,
                // never too late), but an overestimate for cost REPORTING on
                // split-less providers.
                CompletionTokens = estimated
                    ? Orkeon.Infrastructure.CostTracking.LlmUsageEstimator.Completion(response)
                    : response.CompletionTokens ?? Math.Max(0, response.TokensUsed - prompt),
                // A partition of PromptTokens (never additive) — null when unreported.
                CacheHitTokens = response.CacheHitTokens,
                CacheMissTokens = response.CacheMissTokens,
                OperationType = method,
            });
        }
        catch
        {
            // Deliberately swallowed — see the fault-barrier contract above.
        }
    }

    /// <summary>
    /// Estimates the prompt token count of a call whose response reported none: from the
    /// conversation when the call sent one, from the single prompt string otherwise.
    /// </summary>
    private static int EstimatePromptTokens(string? promptText, IReadOnlyList<LlmMessage>? promptMessages)
        => promptMessages is not null
            ? Orkeon.Infrastructure.CostTracking.LlmUsageEstimator.Prompt(promptMessages)
            : Orkeon.Infrastructure.CostTracking.LlmUsageEstimator.Prompt(promptText);

    /// <summary>Cancels the in-flight and future llm calls of this context (script-facing).</summary>
    public void interrupt() => _cts.Cancel();

    /// <summary>Whether <see cref="interrupt"/> was requested (or the host token cancelled).</summary>
    public bool isInterrupted => _cts.IsCancellationRequested;

    /// <summary>True when the cancellation is a local interrupt(), not a host cancellation.</summary>
    private bool IsLocalInterrupt => _cts.IsCancellationRequested && !_hostCt.IsCancellationRequested;

    /// <summary>Releases the linked interrupt source. Called by <see cref="JsAgentContext.Dispose"/>.</summary>
    internal void DisposeInterruptSource() => _cts.Dispose();

    public Func<string, JsValue?, Task<string>> complete => async (prompt, options) =>
    {
        using var activity = ScriptingActivitySource.Instance.StartActivity(ScriptingActivitySource.LlmCallSpan);
        activity?.SetTag("llm.method", "complete");
        activity?.SetTag("llm.prompt.length", prompt.Length);
        if (_provider is null) return $"<undefined-llm:{prompt}>";
        var resp = await _provider.GenerateAsync(prompt, ConfigFrom(options), _ct).ConfigureAwait(false);
        activity?.SetTag("llm.response.tokens", resp.TokensUsed);
        ReportUsage(resp, "complete", prompt);
        return RenderAnswer(resp);
    };

    /// <summary>
    /// Renders one provider answer for the script surface, and it is the ONLY place that
    /// decides what an unusable provider looks like — <c>complete</c> and <c>stream</c> both
    /// call it, so the two surfaces cannot drift apart again.
    /// </summary>
    /// <remarks>
    /// A response with no content that carries a provider error is not an empty answer: it is a
    /// call that never happened, and returning its empty <c>Content</c> handed a script the same
    /// bytes as a model with nothing to say. It becomes the same
    /// <c>&lt;undefined-llm:…&gt;</c> marker the missing-provider case already produced, with
    /// the provider's own sentence inside it, so a script can both see it and read why.
    /// </remarks>
    /// <param name="response">The provider's answer.</param>
    private static string RenderAnswer(LlmResponse response)
    {
        if (!string.IsNullOrEmpty(response.Content))
            return response.Content;

        return ProviderErrorOf(response) is { } error
            ? $"<undefined-llm:{error}>"
            : response.Content;
    }

    /// <summary>The provider's own error sentence, when the response carries one.</summary>
    private static string? ProviderErrorOf(LlmResponse response) =>
        response.Metadata.TryGetValue(ProviderErrorMetadataKey, out var value)
        && value?.ToString() is { Length: > 0 } message
            ? message
            : null;

    /// <summary>Metadata key every provider writes its refusal under (<c>LlmResponseMetadata</c>).</summary>
    private const string ProviderErrorMetadataKey = "error";

    public Func<JsValue, JsValue?, Task<JsValue>> chat => async (messages, options) =>
    {
        var msgs = ToMessages(messages);
        if (_provider is null)
            return JsValue.FromObject(_engine, new { content = $"<undefined-llm:chat:{msgs.Length} msgs>", tokensUsed = 0 });
        var resp = await _provider.ChatAsync(msgs, ConfigFrom(options), _ct).ConfigureAwait(false);
        ReportUsage(resp, "chat", promptMessages: msgs);
        return JsValue.FromObject(_engine, new
        {
            content = resp.Content,
            tokensUsed = resp.TokensUsed,
            model = resp.Model ?? string.Empty,
        });
    };

    /// <summary>
    /// Streams the completion as 1+ chunks. Streams per-token when the provider exposes a
    /// real SSE path (<see cref="Orkeon.Application.Interfaces.Ports.IStreamingLlmProvider"/>);
    /// otherwise falls back to a single full-text chunk.
    /// </summary>
    public Func<string, JsValue?, JsValue> stream => (prompt, options) =>
    {
        var observations = new StreamObservations();
        return AsAsyncIterable(StreamCore(prompt, options, observations), observations);
    };

    /// <summary>
    /// CLR-facing sibling of <c>stream</c>: the raw chunk sequence, without the JS
    /// async-iterable wrapper. Exists so C# callers and tests can consume the
    /// sequence directly — but note that asserting on THIS proves nothing about the
    /// script surface. A C# test over this method passed for months while
    /// <c>for await</c> in a script threw "The value is not iterable"; the JS
    /// protocol needs its own test.
    /// </summary>
    internal IAsyncEnumerable<string> StreamChunks(string prompt, JsValue? options)
        => StreamCore(prompt, options, new StreamObservations());

    /// <summary>
    /// Wraps an <see cref="IAsyncEnumerable{T}"/> into a JS async-iterable object so
    /// scripts can write <c>for await (const chunk of ctx.llm.stream(p))</c>.
    /// </summary>
    /// <remarks>
    /// Returning the CLR <see cref="IAsyncEnumerable{T}"/> directly does NOT work:
    /// Jint sees a plain interop object with neither <c>Symbol.asyncIterator</c> nor
    /// <c>Symbol.iterator</c>, and <c>for await</c> fails with "The value is not
    /// iterable" — measured 2026-08-03, while the ambient typings advertised
    /// <c>AsyncIterable&lt;string&gt;</c>. The protocol is therefore built here: an
    /// object whose <c>Symbol.asyncIterator</c> returns an iterator whose
    /// <c>next()</c> resolves to <c>{ value, done }</c>. The enumerator is created
    /// once per call and disposed when the sequence completes or the consumer
    /// breaks out early (<c>return()</c>).
    /// </remarks>
    private JsValue AsAsyncIterable(IAsyncEnumerable<string> source, StreamObservations observations)
    {
        var enumerator = source.GetAsyncEnumerator(_ct);
        var disposed = false;

        async Task<JsValue> DisposeOnceAsync()
        {
            if (!disposed)
            {
                disposed = true;
                await enumerator.DisposeAsync().ConfigureAwait(false);
            }
            return Result(JsValue.Undefined, done: true);
        }

        // `next` pulls one chunk. Disposal happens as soon as the sequence ends so a
        // fully-consumed stream does not depend on the consumer calling `return()`.
        var next = new Func<Task<JsValue>>(async () =>
        {
            if (disposed)
                return Result(JsValue.Undefined, done: true);
            try
            {
                if (await enumerator.MoveNextAsync().ConfigureAwait(false))
                    return Result(enumerator.Current ?? string.Empty, done: false);
            }
            catch
            {
                await DisposeOnceAsync().ConfigureAwait(false);
                throw;
            }
            return await DisposeOnceAsync().ConfigureAwait(false);
        });

        // `return` is what a `break` inside `for await` calls: it must release the
        // enumerator, otherwise an abandoned stream leaks the underlying HTTP read.
        var ret = new Func<Task<JsValue>>(DisposeOnceAsync);

        // `usage` / `reasoningChunks` are getters onto the CLR observations object,
        // so the script reads them when IT is executing (after the loop) instead of
        // the stream calling into the engine from another thread.
        var factory = _engine.Evaluate(
            "(function (next, ret, obs) { return { [Symbol.asyncIterator]() { "
            + "return { next: next, return: ret }; }, "
            + "get usage() { return obs.usage; }, "
            + "get reasoningChunks() { return obs.reasoningChunks; } }; })");
        return _engine.Invoke(factory, next, ret, observations);
    }

    private JsValue Result(JsValue value, bool done)
        => JsValue.FromObject(_engine, new Dictionary<string, object?>
        {
            ["value"] = value.IsUndefined() ? null : value.ToObject(),
            ["done"] = done,
        });

    /// <summary>
    /// The chunk sequence behind <c>stream</c>: visible content deltas, in order.
    /// Reasoning and usage go to <paramref name="observations"/>, never into the chunks.
    /// </summary>
    /// <remarks>
    /// <para>Goes through <see cref="Orkeon.Application.Interfaces.Ports.IStreamingLlmProvider.ChatStreamingAsync"/>
    /// rather than <c>GenerateStreamingAsync</c>. The two read the same SSE stream and
    /// differ in what they ask for and what they keep:</para>
    /// <list type="bullet">
    /// <item><description><b>Usage.</b> The chat path sends
    /// <c>stream_options: { include_usage: true }</c>; the plain path does not. Without it
    /// most providers emit no usage chunk at all, so a streamed call had NO token
    /// accounting — measured on a real agent run whose two streamed calls carried usage only
    /// because Moonshot volunteers it. On OpenAI the same run would have reported nothing,
    /// and the calls that stream are the long, expensive ones.</description></item>
    /// <item><description><b>Reasoning.</b> The plain path yields
    /// <c>delta.content</c> only. A thinking model emits its reasoning as
    /// <c>delta.reasoning_content</c>, so the stream is SILENT for as long as the model
    /// thinks — one deliverable of that run spent 22 673 of its 32 627 completion tokens
    /// there, i.e. most of a nine-minute call during which nothing arrived. A consumer had
    /// no way to tell that from a dead stream. Reasoning deltas are counted, logged through
    /// the HOST logger every <see cref="ReasoningLogEvery"/>, and deliberately NOT yielded
    /// as chunks: they are not part of the answer, and a caller writing chunks to a file
    /// must not find them there.</description></item>
    /// </list>
    /// <para>Nothing here calls back into JS. See
    /// <see cref="StreamObservations"/> for why that constraint is not negotiable.</para>
    /// <para>A provider without a real SSE path still yields a single full-text chunk, and
    /// the observations still carry that response's usage — the side-channel does not
    /// silently stop working on a non-streaming provider.</para>
    /// <para>That single-chunk branch is also what an UNCONFIGURED provider now takes: it
    /// declares <c>SupportsStreaming = false</c> (LLM-00 §8), so the answer a script gets from
    /// <c>for await</c> is the one <c>complete</c> gives it, rendered by the same
    /// <see cref="RenderAnswer"/> — an explicit <c>&lt;undefined-llm:…&gt;</c> naming the
    /// missing credential, never an empty sequence that reads as "the model said nothing".</para>
    /// </remarks>
    private async IAsyncEnumerable<string> StreamCore(
        string prompt, JsValue? options, StreamObservations observations)
    {
        if (_provider is null)
        {
            yield return $"<undefined-llm:{prompt}>";
            yield break;
        }

        var config = ConfigFrom(options);

        if (_provider is Orkeon.Application.Interfaces.Ports.IStreamingLlmProvider sp && sp.SupportsStreaming)
        {
            await foreach (var ev in sp.ChatStreamingAsync([LlmMessage.User(prompt)], config, _ct)
                               .ConfigureAwait(false))
            {
                switch (ev.Kind)
                {
                    case LlmStreamEventKind.ContentDelta when !string.IsNullOrEmpty(ev.Delta):
                        yield return ev.Delta;
                        break;
                    case LlmStreamEventKind.ReasoningDelta:
                        observations.reasoningChunks++;
                        if (observations.reasoningChunks % ReasoningLogEvery == 0)
                            LogReasoningProgress(observations.reasoningChunks);
                        break;
                    case LlmStreamEventKind.Completed:
                        observations.usage = ToStreamUsage(ev.FinalResponse);
                        ReportUsage(ev.FinalResponse, "stream", prompt);
                        break;
                    default:
                        break;
                }
            }
            yield break;
        }

        var resp = await _provider.GenerateAsync(prompt, config, _ct).ConfigureAwait(false);
        yield return RenderAnswer(resp);
        observations.usage = ToStreamUsage(resp);
        ReportUsage(resp, "stream", prompt);
    }

    /// <summary>
    /// Reasoning deltas are numerous and arrive during the phase where nothing else does,
    /// so they get a coarse interval: enough to show life, not enough to flood a log.
    /// </summary>
    private const int ReasoningLogEvery = 200;

    private void LogReasoningProgress(int count)
    {
        if (_logger is not null)
            LogReasoningProgressCore(_logger, count);
    }

    [Microsoft.Extensions.Logging.LoggerMessage(
        EventId = 1, Level = Microsoft.Extensions.Logging.LogLevel.Information,
        Message = "llm.stream: {Count} reasoning delta(s) so far, no visible output yet")]
    static partial void LogReasoningProgressCore(Microsoft.Extensions.Logging.ILogger logger, int count);

    /// <summary>
    /// Maps a response's counts onto the JS-facing shape. Returns null when the provider
    /// reported nothing at all: "no usage" and "zero tokens" must not read the same way.
    /// </summary>
    private static StreamUsage? ToStreamUsage(LlmResponse? response)
    {
        if (response is null)
            return null;
        if (response.TokensUsed == 0 && response.PromptTokens is null && response.CompletionTokens is null)
            return null;
        return new StreamUsage
        {
            tokensUsed = response.TokensUsed,
            promptTokens = response.PromptTokens,
            completionTokens = response.CompletionTokens,
            cacheHitTokens = response.CacheHitTokens,
            model = response.Model ?? string.Empty,
        };
    }

    public Func<string, JsValue, JsValue?, Task<JsValue>> extract => async (prompt, schema, options) =>
    {
        if (_provider is null)
        {
            using var empty = JsonDocument.Parse("{}");
            return JsValue.FromObject(_engine, JsonElementToObject(empty.RootElement));
        }

        // extract's contract is "return structured JSON". Three things make that reliable instead
        // of best-effort: (1) the schema arg — previously ignored — is folded into the prompt so the
        // model knows the target shape; (2) response_format defaults to json_object (unless the caller
        // explicitly asked for text), so OpenAI-compatible providers (DeepSeek/OpenAI/Grok/…) emit raw
        // JSON; (3) the reply is stripped of ```json fences before parsing, tolerating models that wrap
        // the object anyway. Without these, any prose/markdown reply threw and aborted the whole crew.
        var fullPrompt = AugmentExtractPrompt(prompt, schema);
        var resp = await _provider.GenerateAsync(fullPrompt, ConfigForExtract(options), _ct).ConfigureAwait(false);
        ReportUsage(resp, "extract", fullPrompt);
        var content = StripJsonFences(resp.Content);
        try
        {
            using var doc = JsonDocument.Parse(content);
            return JsValue.FromObject(_engine, JsonElementToObject(doc.RootElement));
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"ctx.llm.extract: provider returned non-JSON content. Raw: {Truncate(resp.Content, 200)}", ex);
        }
    };

    /// <summary>
    /// Appends the requested JSON shape (when a schema object is supplied) plus a strict
    /// "JSON only, no prose, no fences" instruction to the caller's prompt.
    /// </summary>
    private static string AugmentExtractPrompt(string prompt, JsValue? schema)
    {
        var schemaText = TryDescribeSchema(schema);
        return schemaText is null
            ? prompt + "\n\nRespond with ONLY a valid JSON object — no prose, no markdown code fences."
            : prompt + "\n\nRespond with ONLY a valid JSON object matching this schema — no prose, no " +
                       $"markdown code fences:\n{schemaText}";
    }

    private static string? TryDescribeSchema(JsValue? schema)
    {
        if (schema is null || schema.IsUndefined() || schema.IsNull() || !schema.IsObject())
            return null;
        try
        {
            return JsonSerializer.Serialize(schema.ToObject());
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Builds the config for <c>extract</c>: caller options win, but the response format defaults to
    /// <c>json_object</c> unless the caller explicitly forced <c>text</c>.
    /// </summary>
    private LlmConfig ConfigForExtract(JsValue? options)
    {
        var cfg = ConfigFrom(options) ?? _provider?.BaseConfig ?? LlmConfig.Default();
        if (cfg.ResponseFormat is not null || CallerForcedText(options))
            return cfg;
        return cfg with { ResponseFormat = new LlmResponseFormat { Type = "json_object" } };
    }

    private static bool CallerForcedText(JsValue? options)
    {
        if (options is null || !options.IsObject()) return false;
        var rf = options.Get("responseFormat");
        return rf.IsString() && string.Equals(rf.AsString(), "text", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Removes a single leading <c>```json</c>/<c>```</c> fence and trailing <c>```</c> (with optional
    /// surrounding whitespace) so a fenced JSON object parses. Leaves unfenced content untouched.
    /// </summary>
    private static string StripJsonFences(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return content;
        var trimmed = content.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal)) return trimmed;
        var firstNewline = trimmed.IndexOf('\n', StringComparison.Ordinal);
        if (firstNewline < 0) return trimmed;
        var body = trimmed[(firstNewline + 1)..];
        var lastFence = body.LastIndexOf("```", StringComparison.Ordinal);
        return (lastFence >= 0 ? body[..lastFence] : body).Trim();
    }

    public Func<string, JsValue, JsValue?, Task<string>> decide => async (prompt, choices, options) =>
    {
        var allowed = ToStringArray(choices);
        if (allowed.Length == 0)
            throw new ArgumentException("ctx.llm.decide requires a non-empty choices array.", nameof(choices));
        var decidePrompt = $"{prompt}\n\nReply with exactly one of: {string.Join(", ", allowed)}";
        var resp = _provider is null
            ? new LlmResponse { Content = allowed[0] }
            : await _provider.GenerateAsync(decidePrompt, ConfigFrom(options), _ct).ConfigureAwait(false);
        if (_provider is not null)
            ReportUsage(resp, "decide", decidePrompt);
        var picked = allowed.FirstOrDefault(c =>
            resp.Content.Trim().StartsWith(c, StringComparison.OrdinalIgnoreCase));
        if (picked is null)
            throw new InvalidOperationException(
                $"ctx.llm.decide: provider returned '{Truncate(resp.Content, 200)}', not one of [{string.Join(", ", allowed)}].");
        return picked;
    };

    public Func<JsValue, JsValue?, Task<JsValue>> embed { get; }

    private async Task<JsValue> EmbedAsync(JsValue text, JsValue? options)
    {
        _ = options;
        await Task.Yield();
        // Embedding interface lives outside ILlmProvider; SCR-09 emits a deterministic
        // 8-dim hash-based vector so scripts can exercise the API without a real model.
        var inputs = text is Jint.Native.Array.ArrayInstance arr
            ? Enumerable.Range(0, (int)Jint.Runtime.TypeConverter.ToInteger(arr.Get("length")))
                .Select(i => arr.Get(i.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToString())
                .ToArray()
            : [text.ToString() ?? string.Empty];
        var vectors = inputs.Select(StubEmbedding).ToArray();
        return JsValue.FromObject(_engine, vectors);
    }

    /// <summary>
    /// Autonomous tool-calling loop: sends the prompt + the agent's tool catalogue to the LLM,
    /// executes each returned tool call against the matching <see cref="IBaseTool"/>, feeds the
    /// result back, and repeats until the model answers without a tool call or the iteration cap
    /// is hit. Uses the provider's <see cref="ILlmProvider.BaseConfig"/> so the configured
    /// model/credentials are preserved while the tool schemas are added per call.
    /// </summary>
    public Func<string, JsValue?, Task<JsValue>> act { get; }

    private async Task<JsValue> ActAsync(string prompt, JsValue? options)
    {
        var maxIterations = ResolveMaxIterations(options);
        var permissionMode = ResolvePermissionMode(options);
        var onDelta = ResolveOnDelta(options);
        if (_provider is null)
            return JsValue.FromObject(_engine, new { output = $"<undefined-llm:act:{prompt}>", iterations = 0 });

        var baseCfg = ConfigFrom(options) ?? _provider.BaseConfig ?? LlmConfig.Default();
        var toolSchemas = _tools.Count > 0 ? _tools.Select(t => t.Schema).ToList() : null;

        // A conversation-level system message wins over any LlmConfig.SystemMessage fallback
        // (OpenAICompatibleProviderBase.PrependConfiguredSystemMessage / Anthropic
        // SeparateSystemMessages both give the in-list message precedence).
        var system = ResolveSystem(options);
        var messages = new List<LlmMessage>(capacity: 2);
        if (system is not null)
            messages.Add(LlmMessage.System(system));
        messages.Add(new LlmMessage { Role = "user", Content = prompt });

        var completedIterations = 0;
        try
        {
            for (var i = 0; i < maxIterations; i++)
            {
                completedIterations = i;
                _ct.ThrowIfCancellationRequested();
                // Pre-flight gate: refuse to pay for another LLM turn once any budget
                // dimension is spent. Throws BudgetExhaustedException (surfaced typed to the
                // host via JsCrew.TryUnwrapTypedHostException).
                _budget?.ThrowIfExhausted();
                using var activity = ScriptingActivitySource.Instance.StartActivity(ScriptingActivitySource.LlmCallSpan);
                activity?.SetTag("llm.method", "act");
                activity?.SetTag("llm.act.iteration", i);

                var cfg = toolSchemas is null
                    ? baseCfg
                    : baseCfg with { Tools = toolSchemas, ToolMode = ToolCallMode.Auto };
                var resp = await SendChatAsync(messages.ToArray(), cfg, onDelta).ConfigureAwait(false);
                _budget?.RecordTokens(resp.TokensUsed);
                ReportUsage(resp, "act", promptMessages: messages);

                var call = TryParseToolCall(resp.RawResponseBody);
                if (call is null)
                    return JsValue.FromObject(_engine, new { output = resp.Content, iterations = i + 1 });

                var (toolName, toolArgs) = call.Value;
                activity?.SetTag("llm.act.tool", toolName);

                var resultText = await ResolveToolResultAsync(toolName, toolArgs, permissionMode, activity).ConfigureAwait(false);

                // Omit the assistant tool-call turn (empty content) to avoid the strict tool-role
                // protocol; feed the result back as a plain user turn. The tool schemas stay in
                // `cfg` every iteration, so the model can chain further calls or answer.
                if (!string.IsNullOrWhiteSpace(resp.Content))
                    messages.Add(new LlmMessage { Role = "assistant", Content = resp.Content });
                messages.Add(new LlmMessage { Role = "user", Content = $"[tool:{toolName}] result:\n{Truncate(resultText, 4000)}" });
            }
        }
        catch (OperationCanceledException) when (IsLocalInterrupt)
        {
            // ctx.llm.interrupt(): graceful settle instead of a crash — the script keeps
            // control and can inspect { interrupted: true }. Host cancellation is excluded
            // by the filter and keeps propagating as OperationCanceledException.
            return JsValue.FromObject(_engine, new
            {
                output = "(interrupted)",
                iterations = completedIterations,
                interrupted = true,
            });
        }

        return JsValue.FromObject(_engine, new
        {
            output = "(max tool-call iterations reached without a final answer)",
            iterations = maxIterations,
            exhausted = true,
        });
    }

    /// <summary>
    /// One chat turn for the tool-call loop. When the caller supplied onDelta — or the host
    /// registered a native delta sink (F5 L3, e.g. the REPL's incremental renderer) — and the
    /// provider streams, consume the SSE chat path: content deltas feed the sink (plain C# call)
    /// and the JS callback (sequentially, on this single enumeration — the Jint engine is never
    /// entered concurrently), and the Completed event yields a response iso-shape with ChatAsync.
    /// Otherwise falls back to the buffered <see cref="ILlmProvider.ChatAsync"/>.
    /// </summary>
    private async Task<LlmResponse> SendChatAsync(LlmMessage[] messages, LlmConfig cfg, JsValue? onDelta)
    {
        if ((onDelta is not null || _deltaSink is not null) &&
            _provider is Orkeon.Application.Interfaces.Ports.IStreamingLlmProvider streamingProvider &&
            streamingProvider.SupportsStreaming)
            return await ChatViaStreamAsync(streamingProvider, messages, cfg, onDelta).ConfigureAwait(false);
        return await _provider!.ChatAsync(messages, cfg, _ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Applies the permission gate to one tool call and, when allowed, executes it. Returns the
    /// text fed back to the model: a <c>DENIED: …</c> refusal on a non-Allow verdict, otherwise
    /// the tool's result string.
    /// </summary>
    private async Task<string> ResolveToolResultAsync(
        string toolName, Dictionary<string, object?> toolArgs, string permissionMode,
        System.Diagnostics.Activity? activity)
    {
        // Resolved before the gate so the tool's self-declared access class
        // (IBaseTool.Access) informs the verdict; unresolved tools stay
        // Unspecified and the gate classifies them fail-closed.
        var tool = FindTool(toolName);
        var verdict = _permissionGate is null
            ? null
            : await _permissionGate.CheckAsync(
                toolName, toolArgs, permissionMode,
                tool?.Access ?? ToolAccess.Unspecified, _ct).ConfigureAwait(false);
        if (verdict is not null && verdict.Action != Orkeon.Application.Interfaces.Security.PermissionAction.Allow)
        {
            // Deny (and Ask without an interactive channel, already downgraded by the
            // gate) becomes a motivated refusal fed back as the tool result — no
            // exception, the model can adapt. Denied calls consume no tool-call budget.
            activity?.SetTag("llm.act.permission", "denied");
            return $"DENIED: {verdict.Message ?? $"tool '{toolName}' is not permitted in mode '{permissionMode}'."}";
        }

        // Recorded here, NOT inside ExecuteToolAsync: its fault barrier would swallow
        // BudgetExhaustedException into an "ERROR:" string fed back to the model.
        _budget?.RecordToolCall();
        return await ExecuteToolAsync(tool, toolName, toolArgs).ConfigureAwait(false);
    }

    private IBaseTool? FindTool(string name)
        => _tools.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Tool-boundary fault barrier: any tool failure is converted to an 'ERROR: ...' string fed back to the model as the tool result, so one faulty tool cannot crash the scripted LLM tool-call loop.")]
    private async Task<string> ExecuteToolAsync(IBaseTool? tool, string name, Dictionary<string, object?> arguments)
    {
        if (tool is null)
            return $"ERROR: tool '{name}' is not available to this agent.";
        try
        {
            var resp = await tool.CallAsync(new ProtocolToolCallRequest(name, arguments), _ct).ConfigureAwait(false);
            if (!resp.Success)
                return $"ERROR: {resp.Error ?? "tool failed"}";
            return resp.Result is null ? "(no output)" : JsonSerializer.Serialize(resp.Result);
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    /// <summary>
    /// Extracts the first tool call from an OpenAI-compatible chat response body
    /// (<c>choices[0].message.tool_calls[0].function.{name,arguments}</c>). Returns null when
    /// the model produced no tool call. Targets OpenAI/DeepSeek/Grok/Azure-shaped responses.
    /// </summary>
    private static (string name, Dictionary<string, object?> args)? TryParseToolCall(string? rawBody)
    {
        if (string.IsNullOrEmpty(rawBody)) return null;
        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            var root = doc.RootElement;
            if (!root.TryGetProperty("choices", out var choices) ||
                choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
                return null;
            var message = choices[0].GetProperty("message");
            if (!message.TryGetProperty("tool_calls", out var toolCalls) ||
                toolCalls.ValueKind != JsonValueKind.Array || toolCalls.GetArrayLength() == 0)
                return null;
            var fn = toolCalls[0].GetProperty("function");
            var name = fn.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            if (string.IsNullOrEmpty(name)) return null;
            var argsRaw = fn.TryGetProperty("arguments", out var a) && a.ValueKind == JsonValueKind.String
                ? a.GetString() ?? "{}"
                : "{}";
            var args = new Dictionary<string, object?>();
            using var argDoc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argsRaw) ? "{}" : argsRaw);
            if (argDoc.RootElement.ValueKind == JsonValueKind.Object)
                foreach (var p in argDoc.RootElement.EnumerateObject())
                    args[p.Name] = JsonElementToObject(p.Value);
            return (name, args);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Consumes the streamed chat completion: every content delta feeds the host's native
    /// delta sink (when registered) and the JS <paramref name="onDelta"/> callback (when
    /// supplied), and the terminal event's response replaces the buffered <c>ChatAsync</c>
    /// result. Callbacks run sequentially on this single enumeration; both consumers must
    /// stay cheap (rendering, logging). The sink's turn terminator is only emitted when at
    /// least one delta was rendered, so tool-call-only turns leave the console untouched.
    /// </summary>
    private async Task<LlmResponse> ChatViaStreamAsync(
        Orkeon.Application.Interfaces.Ports.IStreamingLlmProvider provider,
        LlmMessage[] messages,
        LlmConfig cfg,
        JsValue? onDelta)
    {
        LlmResponse? final = null;
        var sankDeltas = false;
        await foreach (var ev in provider.ChatStreamingAsync(messages, cfg, _ct).ConfigureAwait(false))
        {
            if (ev.Kind == LlmStreamEventKind.ContentDelta && !string.IsNullOrEmpty(ev.Delta))
            {
                if (_deltaSink is not null)
                {
                    _deltaSink.OnDelta(ev.Delta);
                    sankDeltas = true;
                }
                if (onDelta is not null)
                    _engine.Invoke(onDelta, ev.Delta);
            }
            else if (ev.Kind == LlmStreamEventKind.Completed)
            {
                final = ev.FinalResponse;
            }
        }
        if (sankDeltas)
            _deltaSink!.OnTurnCompleted();
        return final ?? new LlmResponse { Content = "" };
    }

    /// <summary>
    /// Reads the optional <c>onDelta</c> act option: a JS function invoked with each streamed
    /// content delta. Null when absent or not callable (the loop then uses buffered ChatAsync).
    /// </summary>
    private static JsValue? ResolveOnDelta(JsValue? options)
    {
        if (options is null || options.IsUndefined() || options.IsNull() || !options.IsObject()) return null;
        var fn = options.Get("onDelta");
        return fn is Jint.Native.Function.Function ? fn : null;
    }

    /// <summary>
    /// Reads the <c>permissionMode</c> act option (posed by the script, e.g. threaded from a
    /// crew input). Defaults to <c>"default"</c> — the most restrictive interactive policy.
    /// </summary>
    private static string ResolvePermissionMode(JsValue? options)
    {
        if (options is null || options.IsUndefined() || options.IsNull()) return "default";
        var raw = options.Get("permissionMode");
        return raw.IsString() && !string.IsNullOrWhiteSpace(raw.AsString()) ? raw.AsString() : "default";
    }

    /// <summary>
    /// Reads the <c>system</c> act option: the system prompt seeded as the first message of
    /// the tool-call conversation. <c>null</c> (the default) keeps the historical single
    /// user-message shape byte for byte.
    /// </summary>
    private static string? ResolveSystem(JsValue? options)
    {
        if (options is null || options.IsUndefined() || options.IsNull()) return null;
        var raw = options.Get("system");
        return raw.IsString() && !string.IsNullOrWhiteSpace(raw.AsString()) ? raw.AsString() : null;
    }

    private static int ResolveMaxIterations(JsValue? options)
    {
        if (options is null || options.IsUndefined() || options.IsNull()) return DefaultActMaxIterations;
        var raw = options.Get("maxIterations");
        if (raw.IsUndefined() || raw.IsNull()) return DefaultActMaxIterations;
        if (raw.IsNumber())
        {
            var v = raw.AsNumber();
            if (v <= 0 || double.IsInfinity(v)) return int.MaxValue;
            return (int)v;
        }
        return DefaultActMaxIterations;
    }

    /// <summary>
    /// Builds the per-call <see cref="LlmConfig"/> from a script options bag, or <c>null</c>
    /// when the bag carries no LLM setting (the provider then uses its own configuration).
    /// </summary>
    /// <remarks>
    /// Call-time settings PATCH the provider's <see cref="ILlmProvider.BaseConfig"/>; they do not
    /// start from a blank one. Downstream, a per-call config REPLACES the provider's wholesale
    /// (<c>HttpLlmProviderBase.CreateHttpClient</c>: <c>requestConfig ?? Config</c>), so building
    /// on <see cref="LlmConfig.Default"/> would hand the transport an empty API key, base URL and
    /// timeout — a script asking for `{ responseFormat: 'json_object' }` would lose its
    /// credentials as a side effect and fail to authenticate. The stub providers used in tests
    /// ignore those fields, which is exactly why the defect stayed invisible.
    /// </remarks>
    private LlmConfig? ConfigFrom(JsValue? options)
    {
        if (options is null || options.IsUndefined() || options.IsNull()) return null;

        LlmConfig? config = null;

        // Top-level overrides (e.g. `{ responseFormat: "json_object" }`) — the most common
        // call-time shape, matching the LLM Response Format quickstart.
        var topRf = options.Get("responseFormat");
        if (topRf.IsString())
        {
            var rfType = topRf.AsString();
            config = Inherited() with
            {
                ResponseFormat = string.Equals(rfType, "text", StringComparison.OrdinalIgnoreCase)
                    ? null
                    : new LlmResponseFormat { Type = rfType },
            };
        }

        // Nested `{ llm: { model: "..." } }` — the per-call model override: the agent's boot
        // provider keeps its credentials and endpoint, only the model name changes.
        var llm = options.Get("llm");
        if (llm.IsObject())
        {
            var model = llm.Get("model");
            if (model.IsString())
            {
                config = (config ?? Inherited()) with
                {
                    Model = model.AsString(),
                };
            }
        }

        return config;

        LlmConfig Inherited() => _provider?.BaseConfig ?? LlmConfig.Default();
    }

    private static LlmMessage[] ToMessages(JsValue messages)
    {
        if (messages is not Jint.Native.Array.ArrayInstance arr)
            return Array.Empty<LlmMessage>();
        var len = (int)Jint.Runtime.TypeConverter.ToInteger(arr.Get("length"));
        var result = new LlmMessage[len];
        for (var i = 0; i < len; i++)
        {
            var m = arr.Get(i.ToString(System.Globalization.CultureInfo.InvariantCulture));
            result[i] = new LlmMessage
            {
                Role = m.Get("role").IsString() ? m.Get("role").AsString() : "user",
                Content = m.Get("content").IsString() ? m.Get("content").AsString() : string.Empty,
            };
        }
        return result;
    }

    private static string[] ToStringArray(JsValue choices)
    {
        if (choices is not Jint.Native.Array.ArrayInstance arr) return Array.Empty<string>();
        var len = (int)Jint.Runtime.TypeConverter.ToInteger(arr.Get("length"));
        var result = new string[len];
        for (var i = 0; i < len; i++)
            result[i] = arr.Get(i.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToString();
        return result;
    }

    private static double[] StubEmbedding(string text)
    {
        var v = new double[8];
        if (string.IsNullOrEmpty(text)) return v;
        for (var i = 0; i < text.Length; i++)
            v[i % 8] += (text[i] % 32) / 32d;
        var norm = Math.Sqrt(v.Sum(x => x * x));
        if (norm > 0) for (var i = 0; i < v.Length; i++) v[i] /= norm;
        return v;
    }

    private static string Truncate(string s, int n)
        => s.Length <= n ? s : s[..n] + "...";

    private static object? JsonElementToObject(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.Object => el.EnumerateObject().ToDictionary(p => p.Name, p => JsonElementToObject(p.Value)),
        JsonValueKind.Array => el.EnumerateArray().Select(JsonElementToObject).ToArray(),
        JsonValueKind.String => el.GetString(),
        JsonValueKind.Number => el.TryGetInt64(out var i) ? (object)i : el.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => el.GetRawText(),
    };
}
#pragma warning restore CS1591
#pragma warning restore IDE1006
