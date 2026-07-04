namespace Orkeon.Domain.SharedKernel.ValueObjects;

/// <summary>
/// Single fusion point for the LLM-config cascade:
/// <c>baseConfig</c> (= agent or crew default) ⊕ <c>taskOverride</c> ⊕ <c>callOverride</c>.
/// Priority order, highest first: callOverride &gt; taskOverride &gt; baseConfig. Each field
/// is resolved independently with a <c>??</c> chain so a null override never erases an
/// inherited value.
/// </summary>
public static class LlmConfigResolver
{
    /// <summary>Fuses overrides over <paramref name="baseConfig"/> and returns an effective immutable config.</summary>
    public static LlmConfig Resolve(
        LlmConfig baseConfig,
        LlmConfigOverride? taskOverride,
        LlmConfigOverride? callOverride)
    {
        ArgumentNullException.ThrowIfNull(baseConfig);

        return baseConfig with
        {
            ResponseFormat = callOverride?.ResponseFormat
                          ?? taskOverride?.ResponseFormat
                          ?? baseConfig.ResponseFormat,
            Temperature = callOverride?.Temperature
                          ?? taskOverride?.Temperature
                          ?? baseConfig.Temperature,
            MaxTokens = callOverride?.MaxTokens
                          ?? taskOverride?.MaxTokens
                          ?? baseConfig.MaxTokens,
            TopP = callOverride?.TopP
                          ?? taskOverride?.TopP
                          ?? baseConfig.TopP,
            Thinking = callOverride?.Thinking
                          ?? taskOverride?.Thinking
                          ?? baseConfig.Thinking,
        };
    }
}
