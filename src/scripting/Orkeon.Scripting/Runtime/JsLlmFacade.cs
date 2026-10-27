using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using Orkeon.Application.Crew.Execution;
using Orkeon.Constants.Llm;
using Jint;
using Jint.Native;
using Orkeon.Domain.Execution;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Tools;
using Orkeon.Scripting.Exceptions;
using Orkeon.Scripting.Internal;
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
    private readonly Orkeon.Application.Interfaces.Security.IToolInvocationPipeline? _toolInvocation;

    /// <summary>The configured result limits and fan-out bound of the act loop.</summary>
    public ToolResultLimitsOptions ToolLimits { get; init; } = new();
    private readonly Orkeon.Application.Interfaces.Ports.ILlmDeltaSink? _deltaSink;
    private readonly Microsoft.Extensions.Logging.ILogger? _logger;
    private readonly string _crewName;
    private readonly string _agentName;
    private readonly UnknownAgentTools? _unknownTools;
    private readonly BackgroundAgents? _background;
    private readonly AgentInstanceRun? _instance;

    // The run scope of the agent body this facade belongs to (JsAgentContext), attached once and
    // ended by the context's Dispose; null for a context outside any agent body (a crew hook, a
    // script's top level), where each act() opens and ends its own.
    private AgentRunScope? _agentRunScope;

    /// <summary>Builds the facade of one execution context.</summary>
    /// <param name="engine">The engine the facade's JS functions belong to.</param>
    /// <param name="provider">The provider every call goes to.</param>
    /// <param name="ct">The context's token.</param>
    /// <param name="tools">The tools <c>act</c> offers.</param>
    /// <param name="guards">
    /// What <c>act</c> checks before running a tool: the run's budget, the host's permission
    /// gate, and the agent's tools the host does not offer (GAP-27).
    /// </param>
    /// <param name="observability">The sinks and the attribution of the calls.</param>
    internal JsLlmFacade(
        Engine engine,
        ILlmProvider? provider,
        CancellationToken ct,
        IReadOnlyList<IBaseTool>? tools = null,
        JsLlmActGuards? guards = null,
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
        _budget = guards?.Budget;
        _permissionGate = guards?.PermissionGate;
        _unknownTools = guards?.UnknownTools;
        _background = guards?.Background;
        _instance = guards?.Instance;
        _deltaSink = observability?.DeltaSink;
        _logger = observability?.Logger;
        _crewName = observability?.CrewName ?? string.Empty;
        _agentName = observability?.AgentName ?? string.Empty;
        _toolInvocation = observability?.ToolInvocation;
        embed = EmbedAsync;
    }

    /// <summary>
    /// Opens the attribution scope of one <c>ctx.llm.*</c> call: the method's name as the kind
    /// of work, and the crew and agent this context runs for. The call itself is reported by
    /// the metered provider the host hands the engine — once, whatever path it takes, a
    /// streamed <c>act</c> turn included (STUDIO-42). The facade used to report each response
    /// itself, and nothing else in the runtime did.
    /// </summary>
    /// <param name="method">The <c>ctx.llm.*</c> entry point, reported as the operation.</param>
    private IDisposable BeginUsageScope(string method) =>
        Orkeon.Application.Interfaces.Ports.LlmUsageScope.Begin(method, crewId: _crewName, agentId: _agentName);

    /// <summary>Cancels the in-flight and future llm calls of this context (script-facing).</summary>
    public void interrupt() => _cts.Cancel();

    /// <summary>Whether <see cref="interrupt"/> was requested (or the host token cancelled).</summary>
    public bool isInterrupted => _cts.IsCancellationRequested;

    /// <summary>
    /// What a script can learn about the host's permission gate (#128), read-only:
    /// <c>ctx.llm.permissionGate.enabled</c> is false when the host registered none, and every tool
    /// call of <see cref="act"/> then runs unchecked, whatever <c>permissionMode</c> the script poses.
    /// </summary>
    public JsPermissionGateState permissionGate => _permissionGate is null ? JsPermissionGateState.Off : JsPermissionGateState.On;

    /// <summary>True when the cancellation is a local interrupt(), not a host cancellation.</summary>
    private bool IsLocalInterrupt => _cts.IsCancellationRequested && !_hostCt.IsCancellationRequested;

    /// <summary>Releases the linked interrupt source. Called by <see cref="JsAgentContext.Dispose"/>.</summary>
    internal void DisposeInterruptSource() => _cts.Dispose();

    /// <summary>
    /// Attaches the run scope of the agent body that owns this facade: every <c>act</c> of the body
    /// runs its tools inside it, so what they register lives until the body's attempt closes.
    /// Called by the <see cref="JsAgentContext"/> constructor.
    /// </summary>
    internal void AttachRunScope(AgentRunScope scope) => _agentRunScope = scope;

    // chat {model} spans with the gen_ai.* attributes (Orkeon.Constants.Llm.GenAiAttributes),
    // like the C# execution loop: one backend view for both runtimes.
    private Activity? StartChatActivity(string method)
    {
        // A provider configured without a model runs its own default, which only it knows.
        var model = _provider?.BaseConfig?.Model is { } configured && !string.IsNullOrWhiteSpace(configured) ? configured : null;
        var activity = ScriptingActivitySource.Instance.StartActivity(
            GenAiAttributes.SpanName(ScriptingActivitySource.LlmCallSpan, model), ActivityKind.Client);
        if (activity is null) return null;
        activity.SetTag(GenAiAttributes.OperationName, GenAiAttributes.OperationChat);
        activity.SetTag(GenAiAttributes.ProviderName, Application.Telemetry.OrkeonActivitySources.ProviderName(_provider?.Name));
        activity.SetTag(GenAiAttributes.RequestModel, model);
        activity.SetTag(GenAiAttributes.AgentName, _agentName);
        activity.SetTag("orkeon.llm.method", method);
        return activity;
    }

    private static void CompleteChatActivity(Activity? activity, LlmResponse response)
    {
        if (activity is null) return;
        activity.SetTag(GenAiAttributes.ResponseModel, response.Model);
        activity.SetTag(GenAiAttributes.UsageInputTokens, response.PromptTokens);
        activity.SetTag(GenAiAttributes.UsageOutputTokens, response.CompletionTokens);
    }

    public Func<string, JsValue?, Task<string>> complete => async (prompt, options) =>
    {
        using var activity = StartChatActivity("complete");
        activity?.SetTag("orkeon.llm.prompt.length", prompt.Length);
        if (_provider is null) return $"<undefined-llm:{prompt}>";
        using var usage = BeginUsageScope("complete");
        var resp = await _provider.GenerateAsync(prompt, ConfigFrom(options), _ct).ConfigureAwait(false);
        CompleteChatActivity(activity, resp);
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

    // Every async surface below resolves a CLR object, never a JsValue built here: the
    // continuation after ConfigureAwait(false) is on a thread-pool thread, and only the
    // engine's own event loop may convert it. See IteratorResult for the incident.
    public Func<JsValue, JsValue?, Task<object>> chat => async (messages, options) =>
    {
        var msgs = ToMessages(messages);
        if (_provider is null)
            return new { content = $"<undefined-llm:chat:{msgs.Length} msgs>", tokensUsed = 0 };
        using var usage = BeginUsageScope("chat");
        var resp = await _provider.ChatAsync(msgs, ConfigFrom(options), _ct).ConfigureAwait(false);
        return new
        {
            content = resp.Content,
            tokensUsed = resp.TokensUsed,
            model = resp.Model ?? string.Empty,
        };
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
        var (next, ret) = AsyncIteratorCallbacks(source, _ct);

        // `usage` / `reasoningChunks` are getters onto the CLR observations object,
        // so the script reads them when IT is executing (after the loop) instead of
        // the stream calling into the engine from another thread.
        return _engine.Invoke(JsTrampolineFactories.StreamIterable.For(_engine), next, ret, observations);
    }

    /// <summary>
    /// The bare protocol over a CLR sequence — what <see cref="ActSession.deltas"/> is built
    /// with: the iterator without the side-channel getters of <c>stream()</c>.
    /// </summary>
    private JsValue AsAsyncIterable(IAsyncEnumerable<string> source, CancellationToken ct)
    {
        var (next, ret) = AsyncIteratorCallbacks(source, ct);
        return _engine.Invoke(JsTrampolineFactories.AsyncIterable.For(_engine), next, ret);
    }

    // Both factories are evaluated once per engine (JsTrampolineFactories) and invoked once per
    // sequence: a script streaming in a loop must not re-parse the same source every turn.
    internal const string StreamIterableFactorySource = """
        (next, ret, obs) => ({
            [Symbol.asyncIterator]() { return { next: next, return: ret }; },
            get usage() { return obs.usage; },
            get reasoningChunks() { return obs.reasoningChunks; }
        })
        """;

    /// <summary>
    /// The bare async-iterable protocol over a CLR sequence: what <c>stream()</c> returns
    /// minus its side-channel getters. <see cref="ActSession.deltas"/> is built with it.
    /// </summary>
    internal const string AsyncIterableFactorySource = """
        (next, ret) => ({ [Symbol.asyncIterator]() { return { next: next, return: ret }; } })
        """;

    /// <summary>
    /// The <c>next</c> / <c>return</c> pair of one async iterator over <paramref name="source"/>,
    /// enumerated under <paramref name="ct"/>. Both resolve a CLR <see cref="IteratorResult"/>.
    /// </summary>
    private static (Func<Task<object>> next, Func<Task<object>> ret) AsyncIteratorCallbacks(
        IAsyncEnumerable<string> source, CancellationToken ct)
    {
        var enumerator = source.GetAsyncEnumerator(ct);
        var disposed = false;

        async Task<object> DisposeOnceAsync()
        {
            if (!disposed)
            {
                disposed = true;
                await enumerator.DisposeAsync().ConfigureAwait(false);
            }
            return IteratorResult(null, done: true);
        }

        // `next` pulls one chunk. Disposal happens as soon as the sequence ends so a
        // fully-consumed stream does not depend on the consumer calling `return()`.
        // Both callbacks resolve to a CLR object, never a JsValue: see IteratorResult.
        var next = new Func<Task<object>>(async () =>
        {
            if (disposed)
                return IteratorResult(null, done: true);
            try
            {
                if (await enumerator.MoveNextAsync().ConfigureAwait(false))
                    return IteratorResult(enumerator.Current ?? string.Empty, done: false);
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
        var ret = new Func<Task<object>>(DisposeOnceAsync);

        return (next, ret);
    }

    /// <summary>
    /// One <c>{ value, done }</c> step of the async-iterator protocol, as a CLR object.
    /// </summary>
    /// <remarks>
    /// Deliberately NOT a <see cref="JsValue"/>. <c>next()</c> resolves after
    /// <c>ConfigureAwait(false)</c>, i.e. on a thread-pool thread, and
    /// <see cref="JsValue.FromObject(Engine, object)"/> enters the engine — Jint's
    /// <c>Engine</c> is single-threaded, and seven streams consumed under one
    /// <c>Promise.all</c> converted their steps concurrently and crashed with a
    /// NullReferenceException inside <c>DefaultObjectConverter</c> (flaky in CI,
    /// 2026-09-11). Handing Jint the CLR value instead lets its task-to-promise bridge
    /// (<c>RegisterPromiseWithClrValue</c>) run the conversion on the engine's own event
    /// loop, which is the only thread allowed to touch it. A dictionary rather than a
    /// record so the member names are <c>value</c>/<c>done</c> under any member-naming
    /// policy the engine is configured with.
    /// </remarks>
    private static Dictionary<string, object?> IteratorResult(string? value, bool done)
        => new()
        {
            ["value"] = value,
            ["done"] = done,
        };

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

        // Open before the first yield: the metered provider reads the attribution when the
        // stream starts, and this iterator resumes from a yield without it.
        using var usage = BeginUsageScope("stream");

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

    public Func<string, JsValue, JsValue?, Task<object?>> extract => async (prompt, schema, options) =>
    {
        if (_provider is null)
        {
            using var empty = JsonDocument.Parse("{}");
            return JsonElementToObject(empty.RootElement);
        }

        // extract's contract is "return structured JSON". Three things make that reliable instead
        // of best-effort: (1) the schema arg — previously ignored — is folded into the prompt so the
        // model knows the target shape; (2) response_format defaults to json_object (unless the caller
        // explicitly asked for text), so OpenAI-compatible providers (DeepSeek/OpenAI/Grok/…) emit raw
        // JSON; (3) the reply is stripped of ```json fences before parsing, tolerating models that wrap
        // the object anyway. Without these, any prose/markdown reply threw and aborted the whole crew.
        var fullPrompt = AugmentExtractPrompt(prompt, schema);
        using var usage = BeginUsageScope("extract");
        var resp = await _provider.GenerateAsync(fullPrompt, ConfigForExtract(options), _ct).ConfigureAwait(false);
        var content = StripJsonFences(resp.Content);
        try
        {
            using var doc = JsonDocument.Parse(content);
            return JsonElementToObject(doc.RootElement);
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
        var cfg = ConfigFrom(options) ?? _provider?.BaseConfig ?? LlmConfig.OnProfile();
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
        using var usage = BeginUsageScope("decide");
        var resp = _provider is null
            ? new LlmResponse { Content = allowed[0] }
            : await _provider.GenerateAsync(decidePrompt, ConfigFrom(options), _ct).ConfigureAwait(false);
        var picked = allowed.FirstOrDefault(c =>
            resp.Content.Trim().StartsWith(c, StringComparison.OrdinalIgnoreCase));
        if (picked is null)
            throw new InvalidOperationException(
                $"ctx.llm.decide: provider returned '{Truncate(resp.Content, 200)}', not one of [{string.Join(", ", allowed)}].");
        return picked;
    };

    public Func<JsValue, JsValue?, Task<object>> embed { get; }

    private async Task<object> EmbedAsync(JsValue text, JsValue? options)
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
        return vectors;
    }

    /// <summary>
    /// Autonomous tool-calling loop: sends the prompt + the agent's tool catalogue to the LLM,
    /// executes each returned tool call against the matching <see cref="IBaseTool"/>, feeds the
    /// result back, and repeats until the model answers without a tool call or the iteration cap
    /// is hit. Uses the provider's <see cref="ILlmProvider.BaseConfig"/> so the configured
    /// model/credentials are preserved while the tool schemas are added per call.
    /// </summary>
    /// <remarks>
    /// An async JS function built once per facade from <see cref="ActFactorySource"/>, not a
    /// CLR delegate. The loop itself runs no JS and stays CLR (<see cref="RunActAsync"/>), but
    /// its one contact point with the script — the <c>onDelta</c> callback — cannot be invoked
    /// from the streaming continuation: that is a thread-pool thread, while the body may be
    /// draining the engine on another (measured by thread id, SCR-25 scenario 10, and Jint
    /// guards nothing on <c>Engine.Invoke</c>). So the deltas cross over as data, through a
    /// channel the script pulls from exactly like <c>stream()</c>, and the callback is driven
    /// by a JS pump — promise reactions on whichever thread drains, the only one allowed inside
    /// the engine.
    /// </remarks>
    public JsValue act => _actJs ??= BuildActFunction();

    private JsValue? _actJs;

    // `beginAct` is synchronous and Guard-bridged: it reads the options while the script is
    // still executing (a JsValue is never read after an await) and hands back the session.
    // `onDelta` is read exactly once, as the CLR side used to: a getter or a Proxy on the
    // options object observes one read per call, not two.
    // `s.run()` is the loop as a Task whose result is a CLR object, settled on the engine's own
    // loop; `s.deltas` an async iterable over the delta channel. The pump is awaited in
    // `finally` so that every delta is delivered before `act` settles and a pump failure is
    // never an unhandled rejection. A callback that throws abandons the run (`s.abort()`,
    // observed at the next delta or iteration) instead of letting the loop pay for turns nobody
    // consumes; the run's cancellation is then superseded by the callback's own error, which
    // `await pump` rethrows from the finally.
    // The agent's toolBuilder() tools (withAutonomousTool) run the same way (GAP-12): the loop,
    // on a pool thread, posts each call to `s.toolRequests` and awaits its settlement; the
    // second pump runs the tool's `execute` here, on the thread draining the engine, and hands
    // the result — or the failure — back through `s.settleTool`, which never throws. A tool
    // instance cannot run from the loop itself: JsTool.CallAsync is a root pump and needs the
    // engine at rest, and the engine is busy running the body that awaits `act`.
    // The `onToolCall` option (RT-04) rides the same pump: the loop posts a request of kind
    // `onToolCall`, the pump runs the callback here and settles its verdict as JSON; a throwing
    // callback settles as a failure, which the loop reads as a deny with the exception message.
    // `onToolResult` rides it too: after the tool ran, a request of kind `onToolResult` carries the
    // call and its result; a returned `{ context }` replaces the line the model is shown.
    internal const string ActFactorySource = """
        (beginAct) => async function act(prompt, options) {
            const cb = options ? options.onDelta : undefined;
            const onDelta = typeof cb === "function" ? cb : null;
            const onToolCall = options && typeof options.onToolCall === "function" ? options.onToolCall : null;
            const onToolResult = options && typeof options.onToolResult === "function" ? options.onToolResult : null;
            const s = beginAct(prompt, options, onDelta !== null);
            const pump = onDelta
                ? (async () => {
                    try { for await (const d of s.deltas) onDelta(d); }
                    catch (e) { s.abort(); throw e; }
                })()
                : null;
            const tools = s.hasPumpWork
                ? (async () => {
                    for await (const id of s.toolRequests) {
                        try {
                            const kind = s.kindOf(id);
                            s.settleTool(id, true, kind === "onToolCall"
                                ? JSON.stringify(await onToolCall(JSON.parse(s.argumentsOf(id))))
                                : kind === "onToolResult"
                                    ? JSON.stringify(await onToolResult(...(a => [a.call, a.result])(JSON.parse(s.argumentsOf(id)))))
                                    : kind === "spawn"
                                        ? await s.spawn(s.argumentsOf(id))
                                        : await s.toolOf(id).execute(JSON.parse(s.argumentsOf(id))));
                        }
                        catch (e) { s.settleTool(id, false, e); }
                    }
                })()
                : null;
            try { return await s.run(); }
            catch (e) { throw s.declared(e); }
            finally {
                if (pump) await pump;
                if (tools) await tools;
            }
        }
        """;

    private JsValue BuildActFunction()
    {
        Func<string, JsValue?, bool, ActSession> beginAct = (prompt, options, streamDeltas) =>
            JsHostError.Guard(_engine, () => new ActSession(this, prompt, options, streamDeltas));
        var factory = JsTrampolineFactories.Act.For(_engine);
        return _engine.Invoke(factory, beginAct);
    }

    /// <summary>
    /// The CLR half of one <c>act</c> call. Built synchronously by <c>beginAct</c> while the
    /// script executes, so every option is parsed before the first await and the loop never
    /// touches a <see cref="JsValue"/> again. The members the trampoline calls are the public
    /// lowercase ones; the rest is internal, which Jint does not expose to the script.
    /// </summary>
    private sealed class ActSession
    {
        private readonly JsLlmFacade _facade;
        private readonly Channel<string>? _deltas;
        private JsValue? _deltasJs;
        private volatile bool _aborted;

        public ActSession(JsLlmFacade facade, string prompt, JsValue? options, bool streamDeltas)
        {
            // What the agent's .tools([...]) names is what the loop offers (GAP-27): a name the
            // host does not answer to fails the call here, before a single model turn, as the
            // declarative shape fails its load on it. Thrown under beginAct's Guard, it rejects
            // act with the declared UnknownToolError.
            if (facade._unknownTools is { } unknown)
                throw new UnknownToolException(facade._agentName, unknown.Names, unknown.Available);

            _facade = facade;
            Prompt = prompt;
            MaxIterations = ResolveMaxIterations(options);
            WaitsForChildren = WaitsForChildrenOf(options);
            PermissionMode = ResolvePermissionMode(options);
            // A mode the script poses on a host without a gate checks nothing: say so once for
            // this call rather than drop it in silence (#128).
            if (facade._permissionGate is null && facade._logger is not null && DeclaresPermissionMode(options))
                LogUngatedPermissionMode(facade._logger, PermissionMode, facade._agentName);
            Config = facade.ConfigFrom(options) ?? facade._provider?.BaseConfig ?? LlmConfig.OnProfile();
            // A script tool instance is offered through a proxy bound to this session: its calls
            // cross over to the JS tool pump instead of running on the loop's thread.
            var offered = TryResolveToolSubset(options, out var subset)
                ? SelectSubset(facade, subset)
                : facade._tools;
            Tools = offered
                .Select(t => t switch
                {
                    JsTool script => new ScriptToolProxy(script, this),
                    SpawnAgentScriptTool spawn => new SpawnToolProxy(spawn, this),
                    _ => t,
                })
                .ToList();
            ToolSchemas = Tools.Count > 0 ? Tools.Select(t => t.Schema).ToList() : null;
            HasToolCallback = HasCallback(options, "onToolCall");
            HasToolResultCallback = HasCallback(options, "onToolResult");
            if (HasToolCallback || HasToolResultCallback || Tools.Any(t => t is ScriptToolProxy or SpawnToolProxy))
                _toolRequests = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

            // A conversation-level system message wins over any LlmConfig.SystemMessage fallback
            // (OpenAICompatibleProviderBase.PrependConfiguredSystemMessage / Anthropic
            // SeparateSystemMessages both give the in-list message precedence).
            var system = ResolveSystem(options);
            if (system is not null)
                Messages.Add(LlmMessage.System(system));
            if (facade._instance is { } instance)
                Messages.AddRange(instance.Seed);
            Messages.Add(new LlmMessage { Role = "user", Content = prompt });

            // One reader (the pump), one writer (the streaming continuation), never bounded:
            // the writer must not block on a script that is slow to consume.
            if (streamDeltas)
                _deltas = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

            // Taken here, on the engine thread inside act(): a spawnBackground made while this act
            // runs finds it as the innermost active act.
            Owner = facade._background?.BeginAct();
            run = () => facade.RunActAsync(this);
        }

        /// <summary>Whether the options say <c>waitFor: "children"</c>.</summary>
        private static bool WaitsForChildrenOf(JsValue? options) =>
            options is not null && options.IsObject() && options.Get("waitFor") is { } waitFor
            && waitFor.IsString() && string.Equals(waitFor.AsString(), "children", StringComparison.Ordinal);

        /// <summary>Whether the options hold a function under <paramref name="name"/>.</summary>
        private static bool HasCallback(JsValue? options, string name) =>
            options is not null && options.IsObject() && options.Get(name) is Jint.Native.Function.Function;

        /// <summary>The agent's tools named by <paramref name="subset"/>; a name the agent does not hold fails the call.</summary>
        private static List<IBaseTool> SelectSubset(JsLlmFacade facade, List<string> subset)
        {
            var available = facade._tools.Select(t => t.Name).ToList();
            var missing = subset.Where(n => !available.Contains(n, StringComparer.OrdinalIgnoreCase)).ToList();
            if (missing.Count > 0)
                throw new UnknownToolException(facade._agentName, missing, available);
            return facade._tools.Where(t => subset.Contains(t.Name, StringComparer.OrdinalIgnoreCase)).ToList();
        }

        /// <summary>The background-children owner of this act; <c>null</c> without a background registry.</summary>
        internal object? Owner { get; }

        internal string Prompt { get; }
        internal int MaxIterations { get; }

        /// <summary>Whether the call ends its wait only once every background child has settled (<c>waitFor: "children"</c>).</summary>
        internal bool WaitsForChildren { get; }
        internal string PermissionMode { get; }
        internal LlmConfig Config { get; }
        internal IReadOnlyList<Orkeon.Domain.Tools.Protocol.ToolSchema>? ToolSchemas { get; }

        /// <summary>The tools this call offers: the built-ins as they are, the script tools behind a <see cref="ScriptToolProxy"/>.</summary>
        internal List<IBaseTool> Tools { get; }
        internal List<LlmMessage> Messages { get; } = new(capacity: 2);
        internal bool StreamsDeltas => _deltas is not null;

        /// <summary>Whether the call declared an <c>onToolCall</c> function; fixed at construction.</summary>
        internal bool HasToolCallback { get; }

        /// <summary>Whether the call declared an <c>onToolResult</c> function; fixed at construction.</summary>
        internal bool HasToolResultCallback { get; }

        /// <summary>The loop, as a Task of a CLR object — Jint settles it on the engine's own loop.</summary>
        public Func<Task<object>> run { get; }

        /// <summary>
        /// The content deltas as a JS async iterable, built on first access — by the script,
        /// on the draining thread. Without a channel it completes at once.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S1144:Unused private types or members should be removed",
            Justification = "Read by the act trampoline (ActFactorySource) through Jint: `s.deltas` is the JS pump's source. The type is private so the script sees nothing else.")]
        public JsValue deltas => _deltasJs ??= _facade.AsAsyncIterable(
            // No cancellation token on purpose: run's finally completes the channel in every
            // outcome, so the read always ends — and an interrupt must not reject the pump,
            // whose failure would supersede the graceful { interrupted: true } result.
            _deltas?.Reader.ReadAllAsync() ?? AsyncEnumerable.Empty<string>(), CancellationToken.None);

        /// <summary>Abandons the run: the pump's callback threw. Cannot throw, so needs no bridge.</summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S1144:Unused private types or members should be removed",
            Justification = "Called by the act trampoline (ActFactorySource) through Jint when the onDelta callback throws. The type is private so the script sees nothing else.")]
        public void abort() => _aborted = true;

        internal void ThrowIfAborted()
        {
            if (_aborted)
                throw new OperationCanceledException("ctx.llm.act: the onDelta callback failed; the run was abandoned.");
        }

        internal void OfferDelta(string delta) => _deltas?.Writer.TryWrite(delta);

        /// <summary>
        /// The loop's rejection as the class <c>errors.d.ts</c> declares — a spent budget is a
        /// <c>BudgetExhaustedError</c> — instead of the wrapped CLR exception of a faulted task.
        /// Runs in the trampoline's catch, on the engine thread; returns the value unchanged when
        /// it has no declared class.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S1144:Unused private types or members should be removed",
            Justification = "Called by the act trampoline (ActFactorySource) through Jint. The type is private so the script sees nothing else.")]
        public JsValue declared(JsValue rejected) => JsHostError.ToDeclaredError(_facade._engine, rejected);

        internal void CompleteDeltas()
        {
            _deltas?.Writer.TryComplete();
            _toolRequests?.Writer.TryComplete();
        }

        // ---- script tools: the loop posts, the JS tool pump runs and settles ----

        private readonly Channel<string>? _toolRequests;
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, PendingScriptToolCall> _pending = new(StringComparer.Ordinal);
        private JsValue? _toolRequestsJs;

        /// <summary>Whether the trampoline must start the pump: script tools or an <c>onToolCall</c> callback.</summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S1144:Unused private types or members should be removed",
            Justification = "Read by the act trampoline (ActFactorySource) through Jint. The type is private so the script sees nothing else.")]
        public bool hasPumpWork => _toolRequests is not null;

        /// <summary>The ids of the script-tool calls the loop posted, as a JS async iterable; ends with the run.</summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S1144:Unused private types or members should be removed",
            Justification = "Read by the act trampoline (ActFactorySource) through Jint. The type is private so the script sees nothing else.")]
        public JsValue toolRequests => _toolRequestsJs ??= _facade.AsAsyncIterable(
            _toolRequests?.Reader.ReadAllAsync() ?? AsyncEnumerable.Empty<string>(), CancellationToken.None);

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S1144:Unused private types or members should be removed",
            Justification = "Called by the act trampoline (ActFactorySource) through Jint. The type is private so the script sees nothing else.")]
        public JsTool? toolOf(string id) => _pending[id].Tool;

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S1144:Unused private types or members should be removed",
            Justification = "Called by the act trampoline (ActFactorySource) through Jint. The type is private so the script sees nothing else.")]
        public string kindOf(string id) => _pending[id].Kind;

        /// <summary>Starts the child of a <c>spawn_agent</c> call, on the engine thread; returns its promise.</summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S1144:Unused private types or members should be removed",
            Justification = "Called by the act trampoline (ActFactorySource) through Jint. The type is private so the script sees nothing else.")]
        public JsValue spawn(string argumentsJson) =>
            JsHostError.Guard(_facade._engine, () => Tools.OfType<SpawnToolProxy>().Single().Tool.Start(argumentsJson));

        /// <summary>Posts one <c>spawn_agent</c> call to the JS pump and awaits the child's output.</summary>
        internal Task<Orkeon.Domain.Tools.Protocol.ToolCallResponse> CallSpawnAsync(
            IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken)
            => PostAsync(new PendingScriptToolCall("spawn", null, JsonSerializer.Serialize(arguments)), cancellationToken);

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S1144:Unused private types or members should be removed",
            Justification = "Called by the act trampoline (ActFactorySource) through Jint. The type is private so the script sees nothing else.")]
        public string argumentsOf(string id) => _pending[id].ArgumentsJson;

        /// <summary>
        /// Settles one call, on the engine thread: the result is converted here, where a JsValue may
        /// be read, and handed to the loop as CLR data. Never throws — the pump has no one to throw to.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S1144:Unused private types or members should be removed",
            Justification = "Called by the act trampoline (ActFactorySource) through Jint. The type is private so the script sees nothing else.")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Tool-boundary fault barrier: a result that cannot be converted becomes a failed tool call fed back to the model, never an exception on the JS pump.")]
        public void settleTool(string id, bool ok, JsValue value)
        {
            if (!_pending.TryRemove(id, out var call)) return;
            try
            {
                call.Completion.TrySetResult(ok
                    ? new Orkeon.Domain.Tools.Protocol.ToolCallResponse(true, value.ToObject(), null)
                    : new Orkeon.Domain.Tools.Protocol.ToolCallResponse(false, null, BackgroundAgents.FailureMessage(value)));
            }
            catch (Exception ex)
            {
                call.Completion.TrySetResult(new Orkeon.Domain.Tools.Protocol.ToolCallResponse(false, null, ex.Message));
            }
        }

        /// <summary>Posts one call of <paramref name="tool"/> to the JS tool pump and awaits its settlement.</summary>
        internal async Task<Orkeon.Domain.Tools.Protocol.ToolCallResponse> CallScriptToolAsync(
            JsTool tool, IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken)
        {
            return await PostAsync(new PendingScriptToolCall("execute", tool, JsonSerializer.Serialize(arguments)), cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Posts one <c>onToolCall</c> request to the JS pump; the response carries the verdict as JSON.</summary>
        internal Task<Orkeon.Domain.Tools.Protocol.ToolCallResponse> CallToolCallbackAsync(
            string toolName, IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken)
            => PostAsync(new PendingScriptToolCall("onToolCall", null, JsonSerializer.Serialize(new { name = toolName, input = arguments })), cancellationToken);

        /// <summary>Posts one <c>onToolResult</c> request to the JS pump; the response carries the callback's return as JSON.</summary>
        internal Task<Orkeon.Domain.Tools.Protocol.ToolCallResponse> CallToolResultCallbackAsync(
            string toolName, IReadOnlyDictionary<string, object?> arguments, string result, CancellationToken cancellationToken)
        {
            object? shown = result;
            try { shown = JsonSerializer.Deserialize<JsonElement>(result); }
            catch (JsonException) { /* not JSON: the callback sees the text as is */ }
            var payload = JsonSerializer.Serialize(new { call = new { name = toolName, input = arguments }, result = shown });
            return PostAsync(new PendingScriptToolCall("onToolResult", null, payload), cancellationToken);
        }

        private async Task<Orkeon.Domain.Tools.Protocol.ToolCallResponse> PostAsync(PendingScriptToolCall call, CancellationToken cancellationToken)
        {
            var id = Guid.NewGuid().ToString("N");
            _pending[id] = call;
            using var registration = cancellationToken.Register(() =>
            {
                _pending.TryRemove(id, out _);
                call.Completion.TrySetCanceled(cancellationToken);
            });
            if (!_toolRequests!.Writer.TryWrite(id))
            {
                _pending.TryRemove(id, out _);
                return new Orkeon.Domain.Tools.Protocol.ToolCallResponse(false, null, "the act call has ended");
            }
            return await call.Completion.Task.ConfigureAwait(false);
        }
    }

    /// <summary><c>spawn_agent</c> as the act loop sees it: calls cross over to the session's JS tool pump.</summary>
    private sealed class SpawnToolProxy : IBaseTool
    {
        private readonly ActSession _session;

        public SpawnToolProxy(SpawnAgentScriptTool tool, ActSession session)
        {
            Tool = tool;
            _session = session;
        }

        internal SpawnAgentScriptTool Tool { get; }

        public string Name => Tool.Name;
        public string Description => Tool.Description;
        public Orkeon.Domain.Tools.Protocol.ToolSchema Schema => Tool.Schema;
        public ToolAccess Access => Tool.Access;

        public Task<Orkeon.Domain.Tools.Protocol.ToolCallResponse> CallAsync(ProtocolToolCallRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            return _session.CallSpawnAsync(request.Parameters, cancellationToken);
        }

        public async Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default)
        {
            var arguments = string.IsNullOrWhiteSpace(input)
                ? new Dictionary<string, object?>()
                : JsonSerializer.Deserialize<Dictionary<string, object?>>(input) ?? new Dictionary<string, object?>();
            var response = await CallAsync(new ProtocolToolCallRequest(Name, arguments), cancellationToken).ConfigureAwait(false);
            return response.Success
                ? ToolResult.CreateSuccess(JsonSerializer.Serialize(response.Result))
                : ToolResult.CreateError(response.Error ?? "Tool execution failed.");
        }

        public bool ValidateInput(string input) => Tool.ValidateInput(input);
    }

    private sealed record PendingScriptToolCall(string Kind, JsTool? Tool, string ArgumentsJson)
    {
        public TaskCompletionSource<Orkeon.Domain.Tools.Protocol.ToolCallResponse> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>
    /// A script tool as the act loop sees it: the tool's name, schema and access class, and a
    /// <see cref="CallAsync"/> that crosses over to the session's JS tool pump. Goes through the
    /// permission gate and the host's tool-invocation pipeline like any other tool.
    /// </summary>
    private sealed class ScriptToolProxy : IBaseTool
    {
        private readonly JsTool _tool;
        private readonly ActSession _session;

        public ScriptToolProxy(JsTool tool, ActSession session)
        {
            _tool = tool;
            _session = session;
        }

        public string Name => _tool.Name;
        public string Description => _tool.Description;
        public Orkeon.Domain.Tools.Protocol.ToolSchema Schema => _tool.Schema;
        public ToolAccess Access => _tool.Access;

        public Task<Orkeon.Domain.Tools.Protocol.ToolCallResponse> CallAsync(ProtocolToolCallRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            return _session.CallScriptToolAsync(_tool, request.Parameters, cancellationToken);
        }

        public async Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default)
        {
            var arguments = string.IsNullOrWhiteSpace(input)
                ? new Dictionary<string, object?>()
                : JsonSerializer.Deserialize<Dictionary<string, object?>>(input) ?? new Dictionary<string, object?>();
            var response = await CallAsync(new ProtocolToolCallRequest(Name, arguments), cancellationToken).ConfigureAwait(false);
            return response.Success
                ? ToolResult.CreateSuccess(JsonSerializer.Serialize(response.Result))
                : ToolResult.CreateError(response.Error ?? "Tool execution failed.");
        }

        public bool ValidateInput(string input) => _tool.ValidateInput(input);
    }

    /// <summary>
    /// The LLM ⇄ tool loop behind <see cref="act"/>: pre-flight budget gate, one chat turn, every
    /// tool call it returns run in order (each through the permission gate), the calls and their
    /// results fed back in the provider's own tool protocol — until the model answers without a
    /// tool call, the iteration cap, or an interrupt. Runs no JS: its results are CLR objects, and
    /// the deltas it observes go to the native sink and the session's channel, never to the
    /// script directly.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000", Justification = "The act-owned run scope is ended in the finally on every path; the analyzer does not follow the conditional ownership (null when the agent body owns the scope).")]
    private async Task<object> RunActAsync(ActSession session)
    {
        var completedIterations = 0;
        using var usage = BeginUsageScope("act");
        // The tools this loop runs see the agent's run scope (AgentRunScope.Current): the agent
        // body's when there is one, else a scope of this act() alone, ended in the finally.
        var ownedRunScope = _agentRunScope is null ? AgentRunScope.Create(agentRole: _agentName) : null;
        IDisposable? enteredRunScope = null;
        try
        {
            enteredRunScope = (_agentRunScope ?? ownedRunScope!).Enter();
            if (_provider is null)
                return new { output = $"<undefined-llm:act:{session.Prompt}>", iterations = 0 };

            var messages = session.Messages;
            for (var i = 0; i < session.MaxIterations; i++)
            {
                completedIterations = i;
                _ct.ThrowIfCancellationRequested();
                session.ThrowIfAborted();
                // Each completion of a background child is one user turn, delivered once.
                if (_background is not null)
                    messages.AddRange(_background.Drain().Select(m => new LlmMessage { Role = "user", Content = m }));
                // Pre-flight gate: refuse to pay for another LLM turn once any budget
                // dimension is spent. Throws BudgetExhaustedException (surfaced typed to the
                // host via JsCrew.TryUnwrapTypedHostException), except a spent token budget,
                // which ends the act with one last call and no tools (#218).
                try
                {
                    _budget?.ThrowIfExhausted();
                }
                catch (Orkeon.Domain.Autonomous.BudgetExhaustedException spent)
                    when (spent.Dimension == Orkeon.Domain.Autonomous.BudgetDimension.Tokens)
                {
                    return await WrapUpAsync(session, messages, spent, i).ConfigureAwait(false);
                }
                using var activity = StartChatActivity("act");
                activity?.SetTag("orkeon.llm.act.iteration", i);

                var cfg = session.ToolSchemas is null
                    ? session.Config
                    : session.Config with { Tools = session.ToolSchemas, ToolMode = ToolCallMode.Auto };
                var resp = await SendChatAsync(messages.ToArray(), cfg, session).ConfigureAwait(false);
                RecordCallTokens(resp.TokensUsed);

                var (calls, assistantTurn) = ReadToolCalls(resp);
                if (assistantTurn is null)
                {
                    var answer = RenderAnswer(resp);
                    if (!session.WaitsForChildren || _background is not { HasPendingWork: true })
                    {
                        messages.Add(new LlmMessage { Role = "assistant", Content = answer });
                        KeepTranscript(messages, interrupted: false);
                        return new { output = answer, iterations = i + 1 };
                    }

                    // The model answered while children are out: wait for their next completion
                    // (or for the last of them to settle) and take another turn.
                    messages.Add(new LlmMessage { Role = "assistant", Content = answer });
                    await _background.WaitForCompletionAsync(_ct).ConfigureAwait(false);
                    continue;
                }

                activity?.SetTag("orkeon.llm.act.tool", string.Join(",", calls.Select(c => c.ToolName)));

                // The assistant turn that asked, then one tool turn per call, in order: the
                // provider replays them in its own protocol (OpenAI tool_calls and tool messages,
                // Anthropic tool_use and tool_result blocks). Every call gets its answer, a refusal
                // or an error included, since a provider rejects a call left unanswered. The tool
                // schemas stay in `cfg` every iteration, so the model can chain further calls or answer.
                messages.Add(assistantTurn);
                await RunToolCallsAsync(session, calls, messages, activity).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (IsLocalInterrupt)
        {
            // ctx.llm.interrupt(): graceful settle instead of a crash — the script keeps
            // control and can inspect { interrupted: true }. Host cancellation is excluded
            // by the filter and keeps propagating as OperationCanceledException.
            KeepTranscript(session.Messages, interrupted: true);
            return new
            {
                output = "(interrupted)",
                iterations = completedIterations,
                interrupted = true,
            };
        }
        finally
        {
            await EndActAsync(session, enteredRunScope, ownedRunScope).ConfigureAwait(false);
        }

        KeepTranscript(session.Messages, interrupted: false);
        return new
        {
            output = "(max tool-call iterations reached without a final answer)",
            iterations = session.MaxIterations,
            exhausted = true,
        };
    }

    /// <summary>
    /// What the act tells the model when its token budget is spent: the last call it gets, with no
    /// tools, asks for the answer it can give from what it has.
    /// </summary>
    internal const string WrapUpNote =
        "Your token budget for this task is spent: this is your last turn, and no tool can be called. "
        + "Answer now with what you have, saying briefly what you could not finish.";

    /// <summary>
    /// Records the tokens of one call of the act loop and logs the running sum next to the limit. A
    /// spent token budget does not throw here: the answer in hand is kept, and the next iteration's
    /// gate ends the act with <see cref="WrapUpAsync"/>.
    /// </summary>
    private void RecordCallTokens(int tokens)
    {
        if (_budget is null) return;
        try
        {
            _budget.RecordTokens(tokens);
        }
        catch (Orkeon.Domain.Autonomous.BudgetExhaustedException spent)
            when (spent.Dimension == Orkeon.Domain.Autonomous.BudgetDimension.Tokens)
        {
            // Recorded already: the gate before the next call sees it.
        }
        finally
        {
            if (_logger is not null)
            {
                var calls = _budget.CurrentTokenCalls;
                LogActCallTokens(_logger, _agentName, tokens, _budget.CurrentTokensConsumed, _budget.MaxTokensConsumed,
                    calls, calls == 1 ? "call" : "calls");
            }
        }
    }

    /// <summary>
    /// The end of an act whose token budget is spent (#218): one last chat call with no tools, told
    /// the budget is spent, whose answer is the act's result marked <c>budgetExhausted</c>. Its
    /// tokens are recorded but never start another wrap-up. When it fails or answers nothing, the
    /// act rejects with the budget's own error, as before.
    /// </summary>
    private async Task<object> WrapUpAsync(
        ActSession session, List<LlmMessage> messages, Orkeon.Domain.Autonomous.BudgetExhaustedException spent, int iteration)
    {
        if (_logger is not null)
            LogTokenBudgetSpent(_logger, _agentName, spent.Message);

        messages.Add(new LlmMessage { Role = "user", Content = WrapUpNote });
        LlmResponse resp;
        using (var activity = StartChatActivity("act"))
        {
            activity?.SetTag("orkeon.llm.act.iteration", iteration);
            activity?.SetTag("orkeon.llm.act.budget_exhausted", true);
            var cfg = session.Config with { Tools = [], ToolMode = ToolCallMode.None };
            try
            {
                resp = await SendChatAsync(messages.ToArray(), cfg, session).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(spent);
                throw;
            }
        }

        RecordCallTokens(resp.TokensUsed);
        var answer = RenderAnswer(resp);
        if (string.IsNullOrWhiteSpace(answer) || ProviderErrorOf(resp) is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(spent);

        messages.Add(new LlmMessage { Role = "assistant", Content = answer });
        KeepTranscript(messages, interrupted: false);
        return new { output = answer, iterations = iteration + 1, budgetExhausted = true };
    }

    [Microsoft.Extensions.Logging.LoggerMessage(
        EventId = 3, Level = Microsoft.Extensions.Logging.LogLevel.Information,
        Message = "act: agent '{Agent}' call used {Tokens} tokens, running sum {Sum}/{Limit} tokens over {Calls} {CallWord}")]
    static partial void LogActCallTokens(
        Microsoft.Extensions.Logging.ILogger logger, string agent, int tokens, int sum, int limit, int calls, string callWord);

    [Microsoft.Extensions.Logging.LoggerMessage(
        EventId = 4, Level = Microsoft.Extensions.Logging.LogLevel.Warning,
        Message = "act: agent '{Agent}' ran out of budget - {Reason}; one last call with no tools asks for its answer")]
    static partial void LogTokenBudgetSpent(Microsoft.Extensions.Logging.ILogger logger, string agent, string reason);

    /// <summary>
    /// The end of an <c>act</c> call, whatever ended it — return, interrupt, throw or cancellation:
    /// the pump reading <c>deltas</c> must always see the end of the channel, or <c>act</c> would
    /// never settle; the background children the act owns are cancelled, and its run scope left.
    /// </summary>
    private async Task EndActAsync(ActSession session, IDisposable? enteredRunScope, AgentRunScope? ownedRunScope)
    {
        session.CompleteDeltas();
        if (session.Owner is { } owner)
            _background?.EndAct(owner);
        enteredRunScope?.Dispose();
        if (ownedRunScope is not null)
            await ownedRunScope.DisposeAsync().ConfigureAwait(false);
    }

    private void KeepTranscript(List<LlmMessage> messages, bool interrupted)
        => _instance?.Registry.Keep(_instance.Id, messages, interrupted);

    /// <summary>
    /// One chat turn for the tool-call loop. When the script asked for deltas (<c>onDelta</c>) —
    /// or the host registered a native delta sink (F5 L3, e.g. the REPL's incremental
    /// renderer) — and the provider streams, consume the SSE chat path: content deltas feed the
    /// sink (plain C# call) and the session's channel (read by the script's pump, on the
    /// engine's thread), and the Completed event yields a response iso-shape with ChatAsync.
    /// Otherwise falls back to the buffered <see cref="ILlmProvider.ChatAsync"/>.
    /// </summary>
    private async Task<LlmResponse> SendChatAsync(LlmMessage[] messages, LlmConfig cfg, ActSession session)
    {
        if ((session.StreamsDeltas || _deltaSink is not null) &&
            _provider is Orkeon.Application.Interfaces.Ports.IStreamingLlmProvider streamingProvider &&
            streamingProvider.SupportsStreaming)
            return await ChatViaStreamAsync(streamingProvider, messages, cfg, session).ConfigureAwait(false);
        return await _provider!.ChatAsync(messages, cfg, _ct).ConfigureAwait(false);
    }

    private static IBaseTool? FindTool(ActSession session, string toolName) =>
        session.Tools.FirstOrDefault(t => string.Equals(t.Name, toolName, StringComparison.OrdinalIgnoreCase));

    private readonly record struct PreparedToolCall(
        Orkeon.Application.Interfaces.LLM.ParsedToolCall Call, IBaseTool? Tool, Dictionary<string, object?> Arguments, string? Refusal);

    /// <summary>
    /// Runs the calls of one model turn group by group (<see cref="ToolCallScheduler"/>): per group the
    /// gate and <c>onToolCall</c> of each call first, one by one in call order; then the group's reads
    /// together (a write, or any call not allow-listed, is a group of one); then <c>onToolResult</c> and the
    /// tool messages in call order. A call whose gate or callback faults leaves its siblings to complete;
    /// the first fault is rethrown after the group.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Fault barrier: the siblings of a faulted call complete first; the first fault is rethrown after the group.")]
    private async Task RunToolCallsAsync(
        ActSession session, IReadOnlyList<Orkeon.Application.Interfaces.LLM.ParsedToolCall> calls,
        List<LlmMessage> messages, System.Diagnostics.Activity? activity)
    {
        var groups = ToolCallScheduler.Partition(
            calls, c => ToolCallScheduler.IsParallelRead(c.ToolName, FindTool(session, c.ToolName)?.Access ?? ToolAccess.Unspecified));
        foreach (var group in groups)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo? fault = null;
            var prepared = new List<PreparedToolCall>(group.Calls.Count);
            foreach (var call in group.Calls)
            {
                _ct.ThrowIfCancellationRequested();
                session.ThrowIfAborted();
                try
                {
                    prepared.Add(await PrepareToolCallAsync(session, call, activity).ConfigureAwait(false));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    fault ??= System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex);
                }
            }

            var toRun = prepared.Where(p => p.Refusal is null).ToList();
            var outputs = await ToolCallScheduler.RunAsync(
                toRun, _ => group.IsParallel,
                (p, _) => ExecuteToolAsync(p.Tool, p.Call.ToolName, p.Arguments),
                ToolLimits.MaxParallelToolCalls, _ct).ConfigureAwait(false);

            var executed = 0;
            foreach (var p in prepared)
            {
                var text = p.Refusal;
                if (text is null)
                {
                    text = outputs[executed++];
                    if (session.HasToolResultCallback)
                        text = await ApplyToolResultCallbackAsync(session, p.Call.ToolName, p.Arguments, text).ConfigureAwait(false);
                }
                messages.Add(new LlmMessage { Role = "tool", Content = text, ToolCallId = p.Call.Id });
            }

            fault?.Throw();
        }
    }

    /// <summary>
    /// Applies the permission gate and <c>onToolCall</c> to one tool call. A non-Allow verdict or a
    /// callback denial becomes the <c>DENIED: …</c> refusal fed back to the model; otherwise the call is
    /// ready to execute, with the arguments the callback may have rewritten.
    /// </summary>
    private async Task<PreparedToolCall> PrepareToolCallAsync(
        ActSession session, Orkeon.Application.Interfaces.LLM.ParsedToolCall call, System.Diagnostics.Activity? activity)
    {
        var toolName = call.ToolName;
        var toolArgs = call.Arguments;
        var permissionMode = session.PermissionMode;
        // Resolved before the gate so the tool's self-declared access class
        // (IBaseTool.Access) informs the verdict.
        var tool = FindTool(session, toolName);
        // A tool this call does not offer could never run: refused at once, before the gate
        // and onToolCall, so no operator is asked to approve it (#193).
        if (tool is null)
        {
            activity?.SetTag("orkeon.llm.act.permission", "unavailable");
            return new(call, null, toolArgs, NotAvailable(toolName));
        }
        var verdict = await GateAsync(toolName, toolArgs, permissionMode, tool.Access).ConfigureAwait(false);
        if (verdict is not null && verdict.Action != Orkeon.Application.Interfaces.Security.PermissionAction.Allow)
        {
            // Deny (an operator's refusal included, when an approval channel answered) and an
            // Ask the gate let through undecided become a motivated refusal fed back as the
            // tool result — no exception, the model can adapt. Denied calls consume no
            // tool-call budget.
            activity?.SetTag("orkeon.llm.act.permission", "denied");
            return new(call, tool, toolArgs, $"DENIED: {verdict.Message ?? $"tool '{toolName}' is not permitted in mode '{permissionMode}'."}");
        }

        if (session.HasToolCallback)
        {
            var callback = await RunToolCallbackAsync(session, tool, toolName, toolArgs).ConfigureAwait(false);
            if (callback.DenyReason is not null)
            {
                activity?.SetTag("orkeon.llm.act.permission", "denied");
                return new(call, tool, toolArgs, $"DENIED: {callback.DenyReason}");
            }
            toolArgs = callback.Arguments;
        }

        // Recorded here, NOT inside ExecuteToolAsync: its fault barrier would swallow
        // BudgetExhaustedException into an "ERROR:" string fed back to the model.
        _budget?.RecordToolCall();
        return new(call, tool, toolArgs, null);
    }

    /// <summary>
    /// Runs <c>onToolResult</c> through the session's pump: a returned <c>{ context }</c> replaces the
    /// line the model is shown; a void return, or a callback that fails, leaves it.
    /// </summary>
    private async Task<string> ApplyToolResultCallbackAsync(ActSession session, string toolName, Dictionary<string, object?> toolArgs, string text)
    {
        var response = await session.CallToolResultCallbackAsync(toolName, toolArgs, text, _ct).ConfigureAwait(false);
        if (!response.Success || response.Result is not string json)
            return text;
        using var returned = JsonDocument.Parse(json);
        return returned.RootElement.ValueKind == JsonValueKind.Object
            && returned.RootElement.TryGetProperty("context", out var context)
            && context.ValueKind == JsonValueKind.String
                ? context.GetString()!
                : text;
    }

    /// <summary>The permission gate's verdict on one call; <c>null</c> when the host registered no gate.</summary>
    private async Task<Orkeon.Application.Interfaces.Security.PermissionVerdict?> GateAsync(
        string toolName, Dictionary<string, object?> toolArgs, string permissionMode, ToolAccess access)
    {
        if (_permissionGate is null) return null;
        // The gate may hold the call while an operator answers an approval: that time is not
        // the script's, so the engine's wall-clock limit and the agent's wall-time budget
        // both stop while the gate decides (#127).
        using var paused = _engine.Constraints.Find<PausableTimeConstraint>()?.Pause();
        using var budgetPaused = _budget?.PauseWallClock();
        return await _permissionGate.CheckAsync(toolName, toolArgs, permissionMode, access, _ct).ConfigureAwait(false);
    }

    private readonly record struct ToolCallbackOutcome(string? DenyReason, Dictionary<string, object?> Arguments);

    /// <summary>
    /// Runs the <c>onToolCall</c> callback through the session's pump and applies its verdict:
    /// <c>ask</c> goes to the gate as a default-mode edit, a rewritten input is gated again.
    /// A callback failure, or a verdict this version does not know, denies.
    /// </summary>
    private async Task<ToolCallbackOutcome> RunToolCallbackAsync(
        ActSession session, IBaseTool? tool, string toolName, Dictionary<string, object?> toolArgs)
    {
        var response = await session.CallToolCallbackAsync(toolName, toolArgs, _ct).ConfigureAwait(false);
        if (!response.Success)
            return new(response.Error ?? "onToolCall failed", toolArgs);

        if (response.Result is not string json)
            return new(NoValidVerdict, toolArgs);
        JsonDocument verdict;
        try { verdict = JsonDocument.Parse(json); }
        catch (JsonException) { return new(NoValidVerdict, toolArgs); }
        using var disposeVerdict = verdict;
        var root = verdict.RootElement;
        return root.ValueKind switch
        {
            JsonValueKind.String => await FollowWordVerdictAsync(root.GetString(), toolName, toolArgs).ConfigureAwait(false),
            JsonValueKind.Object => await FollowObjectVerdictAsync(root, session, tool, toolName, toolArgs).ConfigureAwait(false),
            _ => new(NoValidVerdict, toolArgs),
        };
    }

    private const string NoValidVerdict = "onToolCall returned no valid verdict";

    /// <summary>An <c>onToolCall</c> verdict of one word: <c>allow</c>, or <c>ask</c> the gate as a default-mode edit.</summary>
    private async Task<ToolCallbackOutcome> FollowWordVerdictAsync(string? word, string toolName, Dictionary<string, object?> toolArgs)
    {
        if (word == "allow")
            return new(null, toolArgs);
        if (word != "ask")
            return new(NoValidVerdict, toolArgs);
        if (_permissionGate is null)
            return new("onToolCall asked for approval but no permission gate is registered", toolArgs);
        var asked = await GateAsync(toolName, toolArgs, "default", ToolAccess.Edit).ConfigureAwait(false);
        return new(DenialOf(asked, toolName, "default"), toolArgs);
    }

    /// <summary>An <c>onToolCall</c> verdict object: <c>{ deny }</c>, <c>{ input }</c> (gated again) or <c>{ context }</c>.</summary>
    private async Task<ToolCallbackOutcome> FollowObjectVerdictAsync(
        JsonElement root, ActSession session, IBaseTool? tool, string toolName, Dictionary<string, object?> toolArgs)
    {
        if (root.TryGetProperty("deny", out var reason))
            return new(reason.ToString(), toolArgs);
        if (root.TryGetProperty("input", out var input))
        {
            if (input.ValueKind != JsonValueKind.Object)
                return new(NoValidVerdict, toolArgs);
            var rewritten = JsonSerializer.Deserialize<Dictionary<string, object?>>(input.GetRawText())!;
            var regated = await GateAsync(toolName, rewritten, session.PermissionMode, tool?.Access ?? ToolAccess.Unspecified).ConfigureAwait(false);
            return new(DenialOf(regated, toolName, session.PermissionMode), rewritten);
        }
        if (root.TryGetProperty("context", out _))
            return new(null, toolArgs);
        return new(NoValidVerdict, toolArgs);
    }

    private static string? DenialOf(Orkeon.Application.Interfaces.Security.PermissionVerdict? verdict, string toolName, string mode) =>
        verdict is null || verdict.Action == Orkeon.Application.Interfaces.Security.PermissionAction.Allow
            ? null
            : verdict.Message ?? $"tool '{toolName}' is not permitted in mode '{mode}'.";

    private static string NotAvailable(string name) => $"ERROR: tool '{name}' is not available to this agent.";

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Tool-boundary fault barrier: any tool failure is converted to an 'ERROR: ...' string fed back to the model as the tool result, so one faulty tool cannot crash the scripted LLM tool-call loop.")]
    private async Task<string> ExecuteToolAsync(IBaseTool? tool, string name, Dictionary<string, object?> arguments)
    {
        if (tool is null)
            return NotAvailable(name);
        try
        {
            if (_toolInvocation is not null)
                return await InvokeThroughPipelineAsync(_toolInvocation, tool, arguments).ConfigureAwait(false);

            var resp = await tool.CallAsync(new ProtocolToolCallRequest(name, arguments), _ct).ConfigureAwait(false);
            if (!resp.Success)
                return $"ERROR: {resp.Error ?? "tool failed"}";
            // Without the host's pipeline, the bound it would have applied: one rule for both paths.
            return resp.Result is null
                ? "(no output)"
                : ToolResultLimitsOptions.Cut(JsonSerializer.Serialize(resp.Result), ToolLimits.ResolveFor(tool.Name));
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    /// <summary>
    /// The guarded path (GAP-09): the call goes through the host's invocation point, so the
    /// guardian screens its arguments and the result reaches the model tagged as data. A
    /// guardian block reads <c>DENIED: …</c>, like a permission-gate refusal; a tool failure
    /// keeps the <c>ERROR: …</c> shape.
    /// </summary>
    private async Task<string> InvokeThroughPipelineAsync(
        Orkeon.Application.Interfaces.Security.IToolInvocationPipeline pipeline, IBaseTool tool, Dictionary<string, object?> arguments)
    {
        var caller = new Orkeon.Application.Interfaces.Security.ToolInvocationCaller(_agentName, _agentName);
        var outcome = await pipeline.InvokeAsync(
            new Orkeon.Application.Interfaces.Security.ToolInvocation(tool, arguments, caller), _ct).ConfigureAwait(false);
        if (outcome.Blocked)
            return $"DENIED: {outcome.ConversationText["Error: ".Length..]}";
        if (!outcome.Success)
            return $"ERROR: {outcome.Response?.Error ?? "tool failed"}";
        return outcome.ConversationText;
    }

    private static readonly Orkeon.Infrastructure.LLMs.ToolCalling.AnthropicToolCallParser AnthropicToolCalls = new();

    private static readonly Orkeon.Infrastructure.LLMs.ToolCalling.OpenAIToolCallParser OpenAIToolCalls =
        new(Microsoft.Extensions.Logging.Abstractions.NullLogger<Orkeon.Infrastructure.LLMs.ToolCalling.OpenAIToolCallParser>.Instance);

    /// <summary>
    /// Reads every tool call of a response with the framework's parsers, chosen from the body's
    /// shape — the Anthropic Messages body (<c>content[]</c> with <c>tool_use</c> blocks) or the
    /// OpenAI chat body (<c>choices[0].message.tool_calls</c>), which every other provider returns
    /// or synthesizes (Ollama), buffered or rebuilt after a stream. The provider reaches the facade
    /// wrapped (metering, limits), so its shape is read rather than its type. Returns no call and
    /// no assistant turn when the model answered, or when the body cannot be read: an unreadable
    /// call is the final answer, never an exception.
    /// </summary>
    private static (IReadOnlyList<Orkeon.Application.Interfaces.LLM.ParsedToolCall> Calls, LlmMessage? AssistantTurn) ReadToolCalls(LlmResponse response)
    {
        if (string.IsNullOrEmpty(response.RawResponseBody))
            return ([], null);
        try
        {
            using var doc = JsonDocument.Parse(response.RawResponseBody);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return ([], null);

            var parser = ToolCallParserFor(root);
            if (parser is null)
                return ([], null);

            var calls = parser.ParseToolCalls(root).Where(c => !string.IsNullOrEmpty(c.ToolName)).ToList();
            return calls.Count == 0
                ? ([], null)
                : (calls, Orkeon.Application.Crew.Execution.NativeToolCallTranscript.AssistantTurn(response, root));
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return ([], null);
        }
    }

    /// <summary>The parser of a body's shape: Anthropic's <c>content[]</c>, OpenAI's non-empty <c>choices[]</c>, else none.</summary>
    private static Orkeon.Application.Interfaces.LLM.IToolCallParser? ToolCallParserFor(JsonElement root)
    {
        if (root.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
            return AnthropicToolCalls;
        if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
            return OpenAIToolCalls;
        return null;
    }

    /// <summary>
    /// Consumes the streamed chat completion: every content delta feeds the host's native
    /// delta sink (when registered) and the session's channel (when the script asked for
    /// deltas), and the terminal event's response replaces the buffered <c>ChatAsync</c>
    /// result. Nothing here enters the engine — this is a thread-pool continuation, and the
    /// script's callback is driven from JS by the pump that reads the channel. The sink must
    /// stay cheap (rendering, logging). Its turn terminator is only emitted when at least one
    /// delta was rendered, so tool-call-only turns leave the console untouched.
    /// </summary>
    private async Task<LlmResponse> ChatViaStreamAsync(
        Orkeon.Application.Interfaces.Ports.IStreamingLlmProvider provider,
        LlmMessage[] messages,
        LlmConfig cfg,
        ActSession session)
    {
        LlmResponse? final = null;
        var sankDeltas = false;
        // The turn's own sink (#211): two acts streaming at once each hold one, so the host keeps their
        // output apart. The act knows no label; the host's sink reads the command it runs under, when any.
        var turn = _deltaSink?.BeginTurn(null);
        await foreach (var ev in provider.ChatStreamingAsync(messages, cfg, _ct).ConfigureAwait(false))
        {
            if (ev.Kind == LlmStreamEventKind.ContentDelta && !string.IsNullOrEmpty(ev.Delta))
            {
                // An abandoned run stops mid-stream: leaving the enumeration disposes it, and
                // with it the provider's HTTP read.
                session.ThrowIfAborted();
                if (turn is not null)
                {
                    turn.OnDelta(ev.Delta);
                    sankDeltas = true;
                }
                session.OfferDelta(ev.Delta);
            }
            else if (ev.Kind == LlmStreamEventKind.Completed)
            {
                final = ev.FinalResponse;
            }
        }
        if (sankDeltas)
            turn!.OnTurnCompleted();
        return final ?? new LlmResponse { Content = "" };
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

    private static bool DeclaresPermissionMode(JsValue? options) =>
        options is not null && !options.IsUndefined() && !options.IsNull()
        && options.Get("permissionMode") is { } raw && raw.IsString() && !string.IsNullOrWhiteSpace(raw.AsString());

    [Microsoft.Extensions.Logging.LoggerMessage(
        EventId = 2, Level = Microsoft.Extensions.Logging.LogLevel.Warning,
        Message = "act: permissionMode '{Mode}' was declared (agent '{Agent}') but the host registered no permission gate - every tool call runs unchecked; set Orkeon:Security:PermissionGate:Enabled = true to check them")]
    static partial void LogUngatedPermissionMode(Microsoft.Extensions.Logging.ILogger logger, string mode, string agent);

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

    /// <summary>Reads the <c>tools</c> act option: the names of the agent's tools to offer; false to offer all of them.</summary>
    private static bool TryResolveToolSubset(JsValue? options, out List<string> subset)
    {
        subset = [];
        if (options is null || options.IsUndefined() || options.IsNull()) return false;
        var raw = options.Get("tools");
        if (!raw.IsArray()) return false;
        subset = raw.AsArray().Select(v => v.ToString()).ToList();
        return true;
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
    /// start from a blank one. A provider completes a call's configuration with its own key,
    /// base URL, timeout, temperature and <c>top_p</c> (GAP-29, GAP-36), but the settings that have
    /// no unset value — the penalties and the tool mode — are the call's: built on a blank
    /// configuration, a script asking for `{ responseFormat: 'json_object' }` would also reset
    /// penalties the host's provider was built with. Only a provider that declares no
    /// configuration gets one built from <see cref="LlmConfig.OnProfile"/>, which names no model
    /// (GAP-18) and no sampling.
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

        var effort = options.Get("effort");
        if (effort.IsString())
            config = (config ?? Inherited()) with { Thinking = JsLlmConfig.ThinkingAt(effort.AsString()) };

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

        // A provider that declares no configuration: the call names no model (GAP-18).
        LlmConfig Inherited() => _provider?.BaseConfig ?? LlmConfig.OnProfile();
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
