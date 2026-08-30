using System.Text.RegularExpressions;
using Orkeon.Constants.Llm;
using Microsoft.Extensions.Logging;
using Polly;
using Orkeon.Application.Interfaces.LLM;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.Constants.Llm;
using Orkeon.Infrastructure.LLMs.Base;

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
    /// exists to surface. Thinking stays None: the trace is always on, inline, with no
    /// knob — <see cref="SplitReasoningFromContent"/> extracts it and
    /// <see cref="EnrichAssistantMessage"/> puts it back in history as the vendor
    /// documents.
    /// </summary>
    public override LlmProviderCapabilities Capabilities { get; } = new()
    {
        ResponseFormat = ResponseFormatSupport.None,
        Thinking = ThinkingSupport.None,
        Vision = true,
        ReplaysReasoningContent = true,
    };

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
        return match.Success
            ? (content[match.Length..], match.Groups["trace"].Value.Trim())
            : (content, null);
    }

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
}
