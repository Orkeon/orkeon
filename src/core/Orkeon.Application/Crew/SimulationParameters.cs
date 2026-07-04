using System.Globalization;

namespace Orkeon.Application.Crew;

/// <summary>
/// Parameters for simulation of task execution.
/// </summary>
public record SimulationParameters(
    int Iterations = 10,
    double SuccessThreshold = 0.8,
    TimeSpan? MaxDuration = null,
    bool EnableLogging = true,
    bool CollectMetrics = true,
    int RandomSeed = 42)
{
    /// <summary>
    /// Default simulation parameters.
    /// </summary>
    public static SimulationParameters Default { get; } = new();

    /// <summary>
    /// Quick simulation with fewer iterations.
    /// </summary>
    public static SimulationParameters Quick { get; } = new(Iterations: 3, EnableLogging: false);

    /// <summary>
    /// Thorough simulation with more iterations.
    /// </summary>
    public static SimulationParameters Thorough { get; } = new(Iterations: 50, CollectMetrics: true);

    /// <summary>
    /// To String.
    /// </summary>
    public override string ToString()
    {
        return $"SimulationParameters {{ Iterations = {Iterations}, SuccessThreshold = {SuccessThreshold.ToString(CultureInfo.InvariantCulture)}, MaxDuration = {MaxDuration}, EnableLogging = {EnableLogging}, CollectMetrics = {CollectMetrics}, RandomSeed = {RandomSeed} }}";
    }
}
