namespace Orkeon.Application.Constants.Execution;

/// <summary>
/// Default values for planning configuration parameters.
/// Centralizes magic numbers used in execution orchestration and planning.
/// </summary>
public static class PlanningDefaults
{
    /// <summary>
    /// Rough estimate of minutes per execution step when planning task duration.
    /// </summary>
    public const int MinutesPerStepEstimate = 2;

    /// <summary>
    /// Fallback plan duration in minutes when planning fails and a simple plan is used.
    /// </summary>
    public const int FallbackPlanMinutes = 5;
}
