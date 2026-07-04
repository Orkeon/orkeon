using Microsoft.Extensions.DependencyInjection;
using Orkeon.Domain.Crew;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.Consensus;

namespace Orkeon.Infrastructure.Crew.Strategies;

/// <summary>
/// Factory for creating process strategy instances based on process type.
/// </summary>
public sealed partial class ProcessStrategyFactory : IProcessStrategyFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ProcessStrategyFactory> _logger;

    /// <summary>Initializes a new instance of <see cref="ProcessStrategyFactory"/>.</summary>
    /// <param name="serviceProvider">The service provider used to resolve strategy instances.</param>
    /// <param name="logger">The logger.</param>
    public ProcessStrategyFactory(
        IServiceProvider serviceProvider,
        ILogger<ProcessStrategyFactory> logger)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        _serviceProvider = serviceProvider;
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public IProcessStrategy CreateStrategy(ProcessType processType)
    {
        ArgumentNullException.ThrowIfNull(processType);
        LogCreatingProcessStrategyForType(processType);

        return processType.Value switch
        {
            "Sequential" => _serviceProvider.GetRequiredService<SequentialProcessStrategy>(),
            "Hierarchical" => _serviceProvider.GetRequiredService<HierarchicalProcessStrategy>(),
            "Parallel" => _serviceProvider.GetRequiredService<ParallelProcessStrategy>(),
            "Graph" => _serviceProvider.GetRequiredService<GraphProcessStrategy>(),
            "Autonomous" => _serviceProvider.GetRequiredService<AutonomousProcessStrategy>(),
            "Consensual" => _serviceProvider.GetRequiredService<ConsensualProcessStrategy>(),
            _ => throw new NotSupportedException($"Process type {processType} is not supported")
        };
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Creating process strategy for type: {ProcessType}")]
    private partial void LogCreatingProcessStrategyForType(ProcessType processType);

}
