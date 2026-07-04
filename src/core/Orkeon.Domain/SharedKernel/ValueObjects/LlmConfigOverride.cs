namespace Orkeon.Domain.SharedKernel.ValueObjects;

/// <summary>
/// Partial LLM-config patch applied on top of an inherited <see cref="LlmConfig"/>. Each
/// field is nullable; <c>null</c> means "leave the inherited value untouched". The cascade
/// is materialised by <see cref="LlmConfigResolver.Resolve"/>; no component fuses overrides
/// elsewhere.
/// </summary>
public sealed record LlmConfigOverride
{
    /// <summary>Output-format constraint (e.g. JSON object). <c>null</c> = keep inherited.</summary>
    public LlmResponseFormat? ResponseFormat { get; init; }

    /// <summary>Sampling temperature override. <c>null</c> = keep inherited.</summary>
    public double? Temperature { get; init; }

    /// <summary>Maximum output tokens override. <c>null</c> = keep inherited.</summary>
    public int? MaxTokens { get; init; }

    /// <summary>Nucleus sampling probability override. <c>null</c> = keep inherited.</summary>
    public double? TopP { get; init; }

    /// <summary>Thinking-mode toggle and reasoning-effort hint override. <c>null</c> = keep inherited.</summary>
    public LlmThinkingConfig? Thinking { get; init; }

    /// <summary>Sugar for the most common case: force a specific output format.</summary>
    public static LlmConfigOverride ForResponseFormat(LlmResponseFormat fmt)
        => new() { ResponseFormat = fmt };
}
