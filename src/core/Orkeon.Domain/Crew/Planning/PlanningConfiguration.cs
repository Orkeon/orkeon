using Orkeon.Domain.Constants.Llm;

namespace Orkeon.Domain.Crew.Planning;

/// <summary>
/// Configuration for AI-powered planning capabilities.
/// </summary>
public class PlanningConfiguration
{
    /// <summary>
    /// Whether planning is enabled.
    /// </summary>
    public bool EnablePlanning { get; init; } = true;

    /// <summary>
    /// The LLM model to use for planning.
    /// </summary>
    public string PlanningLlmModel { get; init; } = LlmDefaults.DefaultPlanningModel;

    /// <summary>
    /// Temperature setting for planning LLM.
    /// </summary>
    public double Temperature { get; init; } = 0.1;
}
