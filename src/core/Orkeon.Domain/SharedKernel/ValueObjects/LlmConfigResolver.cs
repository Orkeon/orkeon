namespace Orkeon.Domain.SharedKernel.ValueObjects;

/// <summary>
/// The merge of a task's override onto its agent's config: <c>baseConfig</c> ⊕ <c>taskOverride</c>.
/// Each field is resolved independently with a <c>??</c> chain so a null override never erases an
/// inherited value. Four callers: <c>ChatOptionsComposer</c> (the IChatClient path, base = the
/// agent's config), <c>LegacyTextAgentLoop</c> and <c>OutputValidationCoordinator</c> (same base, no
/// chat client), <c>NativeToolCallingAgentLoop</c> (base built by <c>BuildNativeLlmConfig</c> from
/// the agent's config, the system prompt and the tools).
/// </summary>
public static class LlmConfigResolver
{
    /// <summary>Merges the task override over <paramref name="baseConfig"/> and returns an effective immutable config.</summary>
    public static LlmConfig Resolve(
        LlmConfig baseConfig,
        LlmConfigOverride? taskOverride)
    {
        ArgumentNullException.ThrowIfNull(baseConfig);

        return baseConfig with
        {
            Profile = taskOverride?.Profile ?? baseConfig.Profile,
            ResponseFormat = taskOverride?.ResponseFormat ?? baseConfig.ResponseFormat,
            Temperature = taskOverride?.Temperature ?? baseConfig.Temperature,
            MaxTokens = taskOverride?.MaxTokens ?? baseConfig.MaxTokens,
            TopP = taskOverride?.TopP ?? baseConfig.TopP,
            Thinking = taskOverride?.Thinking ?? baseConfig.Thinking,
            Cache = taskOverride?.Cache ?? baseConfig.Cache,
        };
    }
}
