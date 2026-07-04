using Orkeon.Domain.Constants.Agent;
namespace Orkeon.Domain.Agent;

/// <summary>
/// Built-in guardrail presets for common agent patterns.
/// Presets can be used standalone or composed with custom rules via <see cref="GuardrailsBuilder.UsePreset"/>.
/// </summary>
/// <example>
/// YAML usage:
/// <code>
/// agents:
///   my_agent:
///     guardrails:
///       preset: "analysis"
///       rules:
///         - "Additional custom rule"
/// </code>
/// Fluent usage:
/// <code>
/// builder.WithGuardrails(g => g.UsePreset(GuardrailPresets.Analysis))
/// </code>
/// </example>
public static class GuardrailPresets
{
    /// <summary>
    /// Analysis preset — prevents hallucination and file creation.
    /// Designed for agents whose job is to READ and ANALYZE existing code or data,
    /// not to create new content. Includes tool-specific rules for file_write and directory_read.
    /// </summary>
    public static GuardrailsConfig Analysis { get; } = new()
    {
        Header = GuardrailDefaults.AnalysisHeader,
        Rules =
        [
            "NEVER fabricate, invent, or hallucinate data. Only use information obtained from tool results or provided in the task context.",
            "If a tool call fails or returns no data, report the failure clearly. Do NOT generate fictional content to compensate.",
            "If the source files you need to analyze are missing or inaccessible, report this explicitly and stop. Do NOT invent file contents.",
            "Clearly distinguish between facts derived from tool outputs and your own analysis or interpretation."
        ],
        ToolRules = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["file_write"] =
            [
                "The file_write tool should ONLY be used to write output/report files as specified in the task. NEVER use it to create input source files.",
                "NEVER use file_write to create source code files that are supposed to already exist. Your job is to ANALYZE existing code, not to create it."
            ],
            ["directory_read"] =
            [
                "Avoid scanning very large directories recursively. Use targeted paths and glob patterns to limit results.",
                "When listing a repository root, always use a glob pattern (e.g., '*.ts', '*.cs') rather than listing all files recursively."
            ]
        }
    };

    /// <summary>
    /// Strict preset — maximum guardrails. Prevents hallucination, file creation,
    /// and adds data provenance requirements. Suitable for compliance-sensitive workflows.
    /// </summary>
    public static GuardrailsConfig Strict { get; } = Analysis.MergeWith(new GuardrailsConfig
    {
        Rules =
        [
            "Every factual claim in your output MUST be traceable to a specific tool result. If you cannot cite the source, do not include the claim.",
            "If you are uncertain about any information, explicitly state your uncertainty rather than presenting it as fact.",
            "Before writing any output file, verify that all data referenced in the file was obtained from actual tool results."
        ]
    });

    /// <summary>
    /// Creative preset — lightweight guardrails for agents that are expected to generate content.
    /// Still prevents pure hallucination but allows content creation via file_write.
    /// </summary>
    public static GuardrailsConfig Creative { get; } = new()
    {
        Header = GuardrailDefaults.CreativeHeader,
        Rules =
        [
            "Base your work on information obtained from tool results and task context whenever available.",
            "If a tool call fails, report the failure and explain how it affects your output.",
            "Clearly distinguish between content derived from source data and content you generated."
        ]
    };

    /// <summary>
    /// Resolves a preset by name (case-insensitive). Returns null if the name is not recognized.
    /// </summary>
    public static GuardrailsConfig? FromName(string? presetName)
    {
        if (string.IsNullOrWhiteSpace(presetName))
            return null;

#pragma warning disable CA1308 // lowercase is the normalized form matched by the switch arms (preset keys), not a comparison normalization
        return presetName.Trim().ToLowerInvariant() switch
        {
#pragma warning restore CA1308
            GuardrailDefaults.PresetAnalysis => Analysis,
            GuardrailDefaults.PresetStrict => Strict,
            GuardrailDefaults.PresetCreative => Creative,
            _ => null
        };
    }
}
