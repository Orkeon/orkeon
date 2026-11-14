using System.Text.RegularExpressions;
using Orkeon.Constants.Llm;
using Microsoft.Extensions.Logging;
using Polly;
using Orkeon.Application.Interfaces.LLM;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.Constants.Llm;
using Orkeon.Infrastructure.LLMs.Base;
using Orkeon.Infrastructure.LLMs.Capabilities;

namespace Orkeon.Infrastructure.LLMs;

/// <summary>
/// MiniMax LLM provider implementation.
/// Uses MiniMax's OpenAI-compatible endpoint (<c>api.minimax.io/v1</c> international,
/// <c>api.minimaxi.com</c> mainland China — Bearer auth).
/// </summary>
/// <remarks>
/// Campaign-backed since 2026-08-30 (7/2/3 on <c>MiniMax-M2</c>): the reasoning arrives
/// INLINE as a <c>&lt;think&gt;</c> block this dialect splits out, <c>response_format</c>
/// is accepted but non-binding (a schema is ignored, json_object arrives fenced in
/// markdown — the None declaration is now a measurement, not caution), and the implicit
/// cache reports no token breakdown at an 8k prefix.
/// </remarks>
public partial class MiniMaxLlmProvider : OpenAICompatibleProviderBase
{
    [GeneratedRegex(@"^\s*<think>(?<trace>.*?)</think>\s*", RegexOptions.Singleline)]
    private static partial Regex LeadingThinkBlock();

    [GeneratedRegex(@"^\s*<think>")]
    private static partial Regex UnterminatedLeadingThink();

    /// <inheritdoc />
    public override string Name => "minimax";

    /// <inheritdoc />
    protected override Uri DefaultBaseUrl => new(LlmEndpoints.MiniMax);

    /// <inheritdoc />
    protected override string DefaultModel => LlmProviderDefaultModels.MiniMax;

    /// <inheritdoc />
    protected override string ProviderDisplayName => "MiniMax";

    /// <summary>
    /// Measured on the first campaign (2026-08-30). Vision is declared for the documented
    /// VL family — per provider here, per model in reality (D-03): <c>MiniMax-M2</c>
    /// answers an image with "I'm unable to view the image". <c>response_format</c> stays
    /// None BY MEASUREMENT: the field is accepted but non-binding (schema ignored,
    /// json_object fenced in markdown), which is precisely the silent drop the warning
    /// exists to surface. The trace is on by default, inline — <see cref="SplitReasoningFromContent"/>
    /// extracts it and <see cref="EnrichAssistantMessage"/> puts it back in history as the vendor
    /// documents. <c>reasoning_effort</c> (every level) and <c>thinking.type: disabled</c> are
    /// accepted, and the switch stops the trace on <c>MiniMax-M3</c> (campaign M18, 2026-11-13);
    /// <c>MiniMax-M3.1-Flash</c> always reasons (a table entry).
    /// </summary>
    protected override LlmProviderCapabilities DeclaredCapabilities { get; } = new()
    {
        ResponseFormat = ResponseFormatSupport.None,
        Thinking = ThinkingSupport.Toggle,
        EffortLevels = LlmThinkingConfig.Levels,
        Vision = true,
        ReplaysReasoningContent = true,
    };

    /// <summary>
    /// Only the off switch is written: thinking is on by default, and <c>MiniMax-M3</c> refuses an
    /// explicit <c>thinking.type: enabled</c> with a 400 (campaign M18, 2026-11-13), so asking for it
    /// on sends nothing and is honoured all the same.
    /// </summary>
    private protected override void WriteReasoningFields(Dictionary<string, object> payload, ReasoningVerdict verdict)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(verdict);
        if (verdict.Toggle is false)
            payload["thinking"] = new Dictionary<string, object?> { ["type"] = "disabled" };
        if (verdict.Effort is { } effort)
            payload["reasoning_effort"] = effort;
    }

    /// <summary>
    /// MiniMax ships its reasoning INLINE: every reply opens with a
    /// <c>&lt;think&gt;...&lt;/think&gt;</c> block inside <c>content</c>, no separate field
    /// (measured 2026-08-30, first campaign). The trace moves to <c>reasoning_content</c>
    /// and the visible answer is what remains.
    /// </summary>
    protected override (string Content, string? Reasoning) SplitReasoningFromContent(string content)
    {
        if (string.IsNullOrEmpty(content))
            return (content, null);

        var match = LeadingThinkBlock().Match(content);
        if (match.Success)
            return (content[match.Length..], match.Groups["trace"].Value.Trim());

        // finish_reason=length can cut the reply inside the think block, so the closing
        // tag never arrives. The first regex being lazy, a leading <think> it did not
        // match means no </think> exists at all: everything after the opening tag is
        // reasoning, and the visible answer is simply missing.
        var open = UnterminatedLeadingThink().Match(content);
        return open.Success
            ? (string.Empty, content[open.Length..].Trim())
            : (content, null);
    }

    /// <summary>
    /// The same block, split out of a stream as it arrives (GAP-32): its text goes out as
    /// reasoning deltas, so the content a stream carries is the answer its final response keeps.
    /// </summary>
    protected override string? LeadingReasoningTag => "think";

    /// <summary>
    /// The vendor documents that multi-turn history must KEEP the think blocks. Orkeon
    /// removed the block from the visible content, so replay puts it back — re-inlined
    /// ahead of the answer in the vendor's own shape, not as the separate
    /// <c>reasoning_content</c> field the DeepSeek dialect uses.
    /// </summary>
    protected override void EnrichAssistantMessage(
        Dictionary<string, object?> messageDict, LlmMessage source)
    {
        ArgumentNullException.ThrowIfNull(messageDict);
        ArgumentNullException.ThrowIfNull(source);

        if (string.IsNullOrEmpty(source.ReasoningContent))
            return;

        var visible = messageDict.TryGetValue("content", out var existing) && existing is string text
            ? text
            : source.Content;
        messageDict["content"] = $"<think>\n{source.ReasoningContent}\n</think>\n\n{visible}";
    }

    /// <summary>Initializes a new instance of <see cref="MiniMaxLlmProvider"/>.</summary>
    public MiniMaxLlmProvider(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        ILogger<MiniMaxLlmProvider>? logger = null)
        : base(config, httpClientFactory, logger)
    {
    }

    /// <summary>Constructor overload that accepts an optional resilience policy for testing.</summary>
    public MiniMaxLlmProvider(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        IAsyncPolicy<HttpResponseMessage>? resiliencePolicy,
        ILogger<MiniMaxLlmProvider>? logger = null)
        : base(config, httpClientFactory, resiliencePolicy, logger)
    {
    }

    /// <summary>Constructor overload that accepts a tool calling strategy.</summary>
    public MiniMaxLlmProvider(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        IToolCallingStrategy? toolCallingStrategy,
        ILogger<MiniMaxLlmProvider>? logger = null)
        : base(config, httpClientFactory, toolCallingStrategy, logger)
    {
    }

    /// <summary>
    /// Constructor overload that accepts both a resilience policy and a tool calling strategy;
    /// <see langword="null"/> for the strategy sends no <c>tools</c>, leaving the text protocol in charge.
    /// </summary>
    public MiniMaxLlmProvider(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        IAsyncPolicy<HttpResponseMessage>? resiliencePolicy,
        IToolCallingStrategy? toolCallingStrategy,
        ILogger<MiniMaxLlmProvider>? logger = null)
        : base(config, httpClientFactory, resiliencePolicy, toolCallingStrategy, logger)
    {
    }
}
