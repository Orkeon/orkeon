namespace Orkeon.Application.Common.DTOs;

/// <summary>
/// Stable keys used under <c>Microsoft.Extensions.AI.ChatOptions.AdditionalProperties</c>
/// to carry Orkeon-specific overrides that the M.E.AI surface area does not model directly
/// (e.g. per-call thinking-mode toggle, GBNF grammar, tool schema list).
///
/// Centralised here so both the Application-side orchestrator (writer) and the
/// Infrastructure-side adapter (reader) reference the same constants without taking a
/// layering dependency on each other.
/// </summary>
public static class LlmChatOptionsKeys
{
    /// <summary>Carries an <c>Orkeon.Domain.SharedKernel.ValueObjects.LlmThinkingConfig</c>.</summary>
    public const string Thinking = "orkeon:thinking";

    /// <summary>Carries an <c>Orkeon.Domain.SharedKernel.ValueObjects.LlmResponseFormat</c>.</summary>
    public const string ResponseFormat = "orkeon:response_format";

    /// <summary>Carries an <c>Orkeon.Domain.SharedKernel.ValueObjects.LlmCacheConfig</c>.</summary>
    public const string Cache = "orkeon:cache";
}
