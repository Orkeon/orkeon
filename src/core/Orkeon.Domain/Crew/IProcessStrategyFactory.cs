using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Domain.Crew;

/// <summary>
/// Factory for creating process strategy instances.
/// </summary>
public interface IProcessStrategyFactory
{
    /// <summary>
    /// Creates a process strategy for the specified process type.
    /// </summary>
    /// <param name="processType">The type of process to create a strategy for.</param>
    /// <returns>The appropriate process strategy implementation.</returns>
    IProcessStrategy CreateStrategy(ProcessType processType);
}
