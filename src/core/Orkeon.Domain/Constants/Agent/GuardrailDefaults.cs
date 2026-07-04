namespace Orkeon.Domain.Constants.Agent;

/// <summary>
/// Default values for guardrail configuration.
/// Centralises magic strings used in guardrail presets and execution orchestration.
/// </summary>
public static class GuardrailDefaults
{
    /// <summary>Prompt header for the analysis preset (critical operational rules).</summary>
    public const string AnalysisHeader = "CRITICAL OPERATIONAL RULES — You MUST follow these at all times:";

    /// <summary>Prompt header for the creative preset (operational guidelines).</summary>
    public const string CreativeHeader = "OPERATIONAL GUIDELINES:";

    /// <summary>Default prompt header used when no preset header is specified.</summary>
    public const string DefaultHeader = "OPERATIONAL RULES — You MUST follow these at all times:";

    /// <summary>Name of the analysis guardrail preset.</summary>
    public const string PresetAnalysis = "analysis";

    /// <summary>Name of the strict guardrail preset.</summary>
    public const string PresetStrict = "strict";

    /// <summary>Name of the creative guardrail preset.</summary>
    public const string PresetCreative = "creative";
}
