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
#pragma warning disable CS1591
public sealed class JsLlmFacade
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

    internal JsLlmFacade(
        Engine engine,
        ILlmProvider? provider,
        CancellationToken ct,
        IReadOnlyList<IBaseTool>? tools = null,
        Orkeon.Domain.Autonomous.AgentExecutionBudget? budget = null,
        Orkeon.Application.Interfaces.Security.IPermissionGate? permissionGate = null,
        Orkeon.Application.Interfaces.Ports.ILlmDeltaSink? deltaSink = null)
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
        _deltaSink = deltaSink;
        embed = EmbedAsync;
        act = ActAsync;
    }

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
        return resp.Content;
    };

    public Func<JsValue, JsValue?, Task<JsValue>> chat => async (messages, options) =>
    {
        var msgs = ToMessages(messages);
        if (_provider is null)
            return JsValue.FromObject(_engine, new { content = $"<undefined-llm:chat:{msgs.Length} msgs>", tokensUsed = 0 });
        var resp = await _provider.ChatAsync(msgs, ConfigFrom(options), _ct).ConfigureAwait(false);
        return JsValue.FromObject(_engine, new
        {
            content = resp.Content,
            tokensUsed = resp.TokensUsed,
            model = resp.Model ?? string.Empty,
        });
    };

    /// <summary>
    /// Streams the completion as 1+ chunks. Streams per-token when the provider exposes a
    /// real SSE path (<see cref="Orkeon.Application.Interfaces.Ports.IStreamingLlmProvider"/>,
    /// exp07 F5); otherwise falls back to a single full-text chunk.
    /// </summary>
    public Func<string, JsValue?, IAsyncEnumerable<string>> stream => (prompt, options)
        => StreamCore(prompt, options);

    private async IAsyncEnumerable<string> StreamCore(string prompt, JsValue? options)
    {
        if (_provider is null)
        {
            yield return $"<undefined-llm:{prompt}>";
            yield break;
        }
        if (_provider is Orkeon.Application.Interfaces.Ports.IStreamingLlmProvider sp && sp.SupportsStreaming)
        {
            await foreach (var chunk in sp.GenerateStreamingAsync(prompt, ConfigFrom(options), _ct).ConfigureAwait(false))
                yield return chunk;
            yield break;
        }
        var resp = await _provider.GenerateAsync(prompt, ConfigFrom(options), _ct).ConfigureAwait(false);
        yield return resp.Content;
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
        // explicitly asked for text), so OpenAI-compatible providers (DeepSeek/OpenAI/Groq/…) emit raw
        // JSON; (3) the reply is stripped of ```json fences before parsing, tolerating models that wrap
        // the object anyway. Without these, any prose/markdown reply threw and aborted the whole crew.
        var fullPrompt = AugmentExtractPrompt(prompt, schema);
        var resp = await _provider.GenerateAsync(fullPrompt, ConfigForExtract(options), _ct).ConfigureAwait(false);
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
        var resp = _provider is null
            ? new LlmResponse { Content = allowed[0] }
            : await _provider.GenerateAsync(
                $"{prompt}\n\nReply with exactly one of: {string.Join(", ", allowed)}",
                ConfigFrom(options), _ct).ConfigureAwait(false);
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

        var messages = new List<LlmMessage> { new() { Role = "user", Content = prompt } };

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
    /// the model produced no tool call. Targets OpenAI/DeepSeek/Groq/Azure-shaped responses.
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

    private static LlmConfig? ConfigFrom(JsValue? options)
    {
        if (options is null || options.IsUndefined() || options.IsNull()) return null;

        LlmConfig? config = null;

        // Top-level overrides (e.g. `{ responseFormat: "json_object" }`) — the most common
        // call-time shape, matching the LLM Response Format quickstart.
        var topRf = options.Get("responseFormat");
        if (topRf.IsString())
        {
            var rfType = topRf.AsString();
            config = LlmConfig.Default() with
            {
                ResponseFormat = string.Equals(rfType, "text", StringComparison.OrdinalIgnoreCase)
                    ? null
                    : new LlmResponseFormat { Type = rfType },
            };
        }

        // Nested `{ llm: { model: "..." } }` — V1 minimum kept for compatibility.
        var llm = options.Get("llm");
        if (llm.IsObject())
        {
            var model = llm.Get("model");
            if (model.IsString())
            {
                config = (config ?? LlmConfig.Default()) with
                {
                    Model = model.AsString(),
                };
            }
        }

        return config;
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
