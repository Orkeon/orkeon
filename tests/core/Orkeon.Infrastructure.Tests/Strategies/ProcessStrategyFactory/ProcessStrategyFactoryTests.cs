using Microsoft.Extensions.DependencyInjection;
using Orkeon.Domain.Crew;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Task;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Infrastructure.Agent;
using Orkeon.Infrastructure.Consensus;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Infrastructure.Crew.Strategies;

namespace Orkeon.Infrastructure.Tests.Strategies;

public class ProcessStrategyFactoryTests
{
    #region Test Doubles

    private class TestLogger : ILogger<ProcessStrategyFactory>
    {
        public List<string> LoggedMessages { get; } = [];
        public List<Exception> LoggedExceptions { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            LoggedMessages.Add($"[{logLevel}] {message}");
            if (exception != null)
            {
                LoggedExceptions.Add(exception);
            }
        }

        public bool HasLoggedDebug(string partialMessage)
        {
            return LoggedMessages.Any(m => m.StartsWith("[Debug]") && m.Contains(partialMessage));
        }
    }

    private class TestServiceProvider : IServiceProvider
    {
        private readonly Dictionary<Type, object> _services = [];
        public bool ThrowOnGetService { get; set; }
        public Exception ExceptionToThrow { get; set; } = new InvalidOperationException("Service not found");

        public void RegisterService<T>(T instance) where T : class
        {
            _services[typeof(T)] = instance;
        }

        public object? GetService(Type serviceType)
        {
            if (ThrowOnGetService)
            {
                throw ExceptionToThrow;
            }

            _services.TryGetValue(serviceType, out var service);
            return service;
        }
    }

    #endregion

    #region Test Helpers

    private static TestServiceProvider CreateServiceProvider(
        bool includeSequential = true,
        bool includeHierarchical = true,
        bool includeParallel = true)
    {
        var serviceProvider = new TestServiceProvider();

        // Use manual mocks instead of Moq to avoid dependency issues
        var mockTaskRepo = new MockTaskRepository();
        var mockAgentRepo = new MockAgentRepository();
        var mockExecutionService = new MockAgentExecutionService();
        using var mockMemoryScope = new MockMemoryScope();

        if (includeSequential)
        {
            var mockLogger = new StrategyLogger<SequentialProcessStrategy>();
            var delegationProvider = new AgentDelegationToolsProvider(
                new MockAgentCommunicationService(), mockExecutionService, new StrategyLogger<AgentDelegationToolsProvider>());
            var sequentialStrategy = new SequentialProcessStrategy(
                mockTaskRepo, mockAgentRepo, mockExecutionService, mockMemoryScope, delegationProvider, mockLogger);
            serviceProvider.RegisterService<SequentialProcessStrategy>(sequentialStrategy);
        }

        if (includeHierarchical)
        {
            var hierarchicalLogger = new StrategyLogger<HierarchicalProcessStrategy>();
            var mockManagerAgent = new MockManagerAgent();
            var hierarchicalStrategy = new HierarchicalProcessStrategy(
                mockTaskRepo, mockAgentRepo, hierarchicalLogger,
                mockManagerAgent, mockExecutionService, mockMemoryScope);
            serviceProvider.RegisterService<HierarchicalProcessStrategy>(hierarchicalStrategy);
        }

        if (includeParallel)
        {
            var parallelLogger = new StrategyLogger<ParallelProcessStrategy>();
            var parallelStrategy = new ParallelProcessStrategy(
                mockTaskRepo, mockAgentRepo, mockExecutionService, mockMemoryScope, parallelLogger);
            serviceProvider.RegisterService<ParallelProcessStrategy>(parallelStrategy);
        }

        return serviceProvider;
    }

    private static ServiceCollection CreateServiceCollectionWithMocks()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // Add manual mock dependencies
        var mockTaskRepo = new MockTaskRepository();
        var mockAgentRepo = new MockAgentRepository();
        var mockManagerAgent = new MockManagerAgent();
        var mockExecutionService = new MockAgentExecutionService();
        using var mockMemoryScope = new MockMemoryScope();

        services.AddSingleton<ITaskRepository>(mockTaskRepo);
        services.AddSingleton<IAgentRepository>(mockAgentRepo);
        services.AddSingleton<IManagerAgent>(mockManagerAgent);
        services.AddSingleton<IAgentExecutionService>(mockExecutionService);
        services.AddSingleton<IMemoryScope>(mockMemoryScope);
        services.AddSingleton<IAgentCommunicationService>(new MockAgentCommunicationService());
        services.AddTransient<AgentDelegationToolsProvider>();

        return services;
    }

    /// <summary>
    /// Simple ILogger implementation for strategy types.
    /// </summary>
    private class StrategyLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        { }
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void ShouldInitialize_WhenConstructorWithValidDependencies()
    {
        // Arrange
        var serviceProvider = CreateServiceProvider();
        var logger = new TestLogger();

        // Act
        var factory = new ProcessStrategyFactory(serviceProvider, logger);

        // Assert
        Assert.NotNull(factory);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructorWithNullServiceProvider()
    {
        // Arrange
        var logger = new TestLogger();

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => new ProcessStrategyFactory(null!, logger));
        Assert.Equal("serviceProvider", exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructorWithNullLogger()
    {
        // Arrange
        var serviceProvider = CreateServiceProvider();

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => new ProcessStrategyFactory(serviceProvider, null!));
        Assert.Equal("logger", exception.ParamName);
    }

    #endregion

    #region CreateStrategy Tests - Sequential

    [Fact]
    public void ShouldReturnSequentialStrategy_WhenCreateStrategyWithSequentialProcessType()
    {
        // Arrange
        var services = CreateServiceCollectionWithMocks();
        services.AddTransient<SequentialProcessStrategy>();
        var serviceProvider = services.BuildServiceProvider();
        var logger = new TestLogger();
        var factory = new ProcessStrategyFactory(serviceProvider, logger);

        // Act
        var strategy = factory.CreateStrategy(ProcessType.Sequential);

        // Assert
        Assert.NotNull(strategy);
        Assert.IsType<SequentialProcessStrategy>(strategy);
        Assert.True(logger.HasLoggedDebug("Creating process strategy for type: Sequential"));
    }

    [Fact]
    public void ShouldThrowInvalidOperationException_WhenCreateStrategyWithSequentialProcessTypeWhenServiceNotRegistered()
    {
        // Arrange
        var serviceProvider = new ServiceCollection().BuildServiceProvider(); // Empty service provider
        var logger = new TestLogger();
        var factory = new ProcessStrategyFactory(serviceProvider, logger);

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(
            () => factory.CreateStrategy(ProcessType.Sequential));
        Assert.Contains("No service for type", exception.Message);
        Assert.Contains("SequentialProcessStrategy", exception.Message);
    }

    #endregion

    #region CreateStrategy Tests - Hierarchical

    [Fact]
    public void ShouldReturnHierarchicalStrategy_WhenCreateStrategyWithHierarchicalProcessType()
    {
        // Arrange
        var services = CreateServiceCollectionWithMocks();
        services.AddTransient<HierarchicalProcessStrategy>();
        var serviceProvider = services.BuildServiceProvider();
        var logger = new TestLogger();
        var factory = new ProcessStrategyFactory(serviceProvider, logger);

        // Act
        var strategy = factory.CreateStrategy(ProcessType.Hierarchical);

        // Assert
        Assert.NotNull(strategy);
        Assert.IsType<HierarchicalProcessStrategy>(strategy);
        Assert.True(logger.HasLoggedDebug("Creating process strategy for type: Hierarchical"));
    }

    [Fact]
    public void ShouldThrowInvalidOperationException_WhenCreateStrategyWithHierarchicalProcessTypeWhenServiceNotRegistered()
    {
        // Arrange
        var serviceProvider = new ServiceCollection().BuildServiceProvider(); // Empty service provider
        var logger = new TestLogger();
        var factory = new ProcessStrategyFactory(serviceProvider, logger);

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(
            () => factory.CreateStrategy(ProcessType.Hierarchical));
        Assert.Contains("No service for type", exception.Message);
        Assert.Contains("HierarchicalProcessStrategy", exception.Message);
    }

    #endregion

    #region CreateStrategy Tests - Parallel

    [Fact]
    public void ShouldReturnParallelStrategy_WhenCreateStrategyWithParallelProcessType()
    {
        // Arrange
        var services = CreateServiceCollectionWithMocks();
        services.AddTransient<ParallelProcessStrategy>();
        var serviceProvider = services.BuildServiceProvider();
        var logger = new TestLogger();
        var factory = new ProcessStrategyFactory(serviceProvider, logger);

        // Act
        var strategy = factory.CreateStrategy(ProcessType.Parallel);

        // Assert
        Assert.NotNull(strategy);
        Assert.IsType<ParallelProcessStrategy>(strategy);
        Assert.True(logger.HasLoggedDebug("Creating process strategy for type: Parallel"));
    }

    [Fact]
    public void ShouldThrowInvalidOperationException_WhenCreateStrategyWithParallelProcessTypeWhenServiceNotRegistered()
    {
        // Arrange
        var serviceProvider = new ServiceCollection().BuildServiceProvider(); // Empty service provider
        var logger = new TestLogger();
        var factory = new ProcessStrategyFactory(serviceProvider, logger);

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(
            () => factory.CreateStrategy(ProcessType.Parallel));
        Assert.Contains("No service for type", exception.Message);
        Assert.Contains("ParallelProcessStrategy", exception.Message);
    }

    #endregion

    #region CreateStrategy Tests - Consensual (R3.3)

    [Fact]
    public void ShouldReturnConsensualStrategy_WhenCreateStrategyWithConsensualProcessType()
    {
        // Arrange — DoD R3.3: Consensual routes through the factory like every other mode
        // (no more NotSupportedException).
        var services = CreateServiceCollectionWithMocks();
        services.AddSingleton<IVotingStrategy>(new MajorityVotingStrategy());
        services.AddTransient<ConsensualProcessStrategy>();
        var serviceProvider = services.BuildServiceProvider();
        var logger = new TestLogger();
        var factory = new ProcessStrategyFactory(serviceProvider, logger);

        // Act
        var strategy = factory.CreateStrategy(ProcessType.Consensual);

        // Assert
        Assert.NotNull(strategy);
        Assert.IsType<ConsensualProcessStrategy>(strategy);
        Assert.True(logger.HasLoggedDebug("Creating process strategy for type: Consensual"));
    }

    [Fact]
    public void ShouldThrowInvalidOperationException_WhenCreateStrategyWithConsensualProcessTypeWhenServiceNotRegistered()
    {
        // Arrange — consistent with the other modes: a missing registration surfaces as
        // an InvalidOperationException from the service provider, not NotSupportedException.
        var serviceProvider = new ServiceCollection().BuildServiceProvider(); // Empty service provider
        var logger = new TestLogger();
        var factory = new ProcessStrategyFactory(serviceProvider, logger);

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(
            () => factory.CreateStrategy(ProcessType.Consensual));
        Assert.Contains("No service for type", exception.Message);
        Assert.Contains("ConsensualProcessStrategy", exception.Message);
    }

    #endregion

    #region Multiple Strategy Creation Tests

    [Fact]
    public void ShouldReturnCorrectStrategies_WhenCreateStrategyMultipleCallsWithDifferentTypes()
    {
        // Arrange
        var services = CreateServiceCollectionWithMocks();
        services.AddTransient<SequentialProcessStrategy>();
        services.AddTransient<HierarchicalProcessStrategy>();
        services.AddTransient<ParallelProcessStrategy>();
        var serviceProvider = services.BuildServiceProvider();
        var logger = new TestLogger();
        var factory = new ProcessStrategyFactory(serviceProvider, logger);

        // Act
        var sequentialStrategy = factory.CreateStrategy(ProcessType.Sequential);
        var hierarchicalStrategy = factory.CreateStrategy(ProcessType.Hierarchical);
        var parallelStrategy = factory.CreateStrategy(ProcessType.Parallel);

        // Assert
        Assert.NotNull(sequentialStrategy);
        Assert.NotNull(hierarchicalStrategy);
        Assert.NotNull(parallelStrategy);

        Assert.IsType<SequentialProcessStrategy>(sequentialStrategy);
        Assert.IsType<HierarchicalProcessStrategy>(hierarchicalStrategy);
        Assert.IsType<ParallelProcessStrategy>(parallelStrategy);

        // Verify logging for all creations
        Assert.Equal(3, logger.LoggedMessages.Count);
        Assert.True(logger.HasLoggedDebug("Creating process strategy for type: Sequential"));
        Assert.True(logger.HasLoggedDebug("Creating process strategy for type: Hierarchical"));
        Assert.True(logger.HasLoggedDebug("Creating process strategy for type: Parallel"));
    }

    [Fact]
    public void ShouldCreateNewInstancesEachTime_WhenCreateStrategyMultipleCalls()
    {
        // Arrange
        var services = CreateServiceCollectionWithMocks();
        services.AddTransient<SequentialProcessStrategy>();
        var serviceProvider = services.BuildServiceProvider();
        var logger = new TestLogger();
        var factory = new ProcessStrategyFactory(serviceProvider, logger);

        // Act
        var strategy1 = factory.CreateStrategy(ProcessType.Sequential);
        var strategy2 = factory.CreateStrategy(ProcessType.Sequential);

        // Assert
        Assert.NotNull(strategy1);
        Assert.NotNull(strategy2);
        Assert.NotSame(strategy1, strategy2); // Different instances due to Transient registration

        // Verify both calls were logged
        Assert.Equal(2, logger.LoggedMessages.Count);
        Assert.All(logger.LoggedMessages, message =>
            Assert.Contains("Creating process strategy for type: Sequential", message));
    }

    #endregion

    #region Dependency Injection Integration Tests

    [Fact]
    public void ShouldWorkCorrectly_WhenCreateStrategyWithRealServiceProvider()
    {
        // Arrange
        var services = CreateServiceCollectionWithMocks();

        // Register actual strategies
        services.AddTransient<SequentialProcessStrategy>();
        services.AddTransient<HierarchicalProcessStrategy>();
        services.AddTransient<ParallelProcessStrategy>();

        services.AddTransient<IProcessStrategyFactory, ProcessStrategyFactory>();

        var serviceProvider = services.BuildServiceProvider();
        var factory = serviceProvider.GetRequiredService<IProcessStrategyFactory>();

        // Act
        var sequentialStrategy = factory.CreateStrategy(ProcessType.Sequential);
        var hierarchicalStrategy = factory.CreateStrategy(ProcessType.Hierarchical);
        var parallelStrategy = factory.CreateStrategy(ProcessType.Parallel);

        // Assert
        Assert.NotNull(sequentialStrategy);
        Assert.NotNull(hierarchicalStrategy);
        Assert.NotNull(parallelStrategy);

        Assert.IsType<SequentialProcessStrategy>(sequentialStrategy);
        Assert.IsType<HierarchicalProcessStrategy>(hierarchicalStrategy);
        Assert.IsType<ParallelProcessStrategy>(parallelStrategy);
    }

    [Fact]
    public void ShouldRespectServiceLifetime_WhenCreateStrategyWithScopedServices()
    {
        // Arrange
        var services = CreateServiceCollectionWithMocks();

        // Register as scoped to test lifetime behavior
        services.AddScoped<SequentialProcessStrategy>();
        services.AddTransient<IProcessStrategyFactory, ProcessStrategyFactory>();

        var serviceProvider = services.BuildServiceProvider();

        // Act & Assert
        using (var scope1 = serviceProvider.CreateScope())
        {
            var factory1 = scope1.ServiceProvider.GetRequiredService<IProcessStrategyFactory>();
            var strategy1a = factory1.CreateStrategy(ProcessType.Sequential);
            var strategy1b = factory1.CreateStrategy(ProcessType.Sequential);

            // Same scope should return same instance for scoped service
            Assert.Same(strategy1a, strategy1b);
        }

        using (var scope2 = serviceProvider.CreateScope())
        {
            var factory2 = scope2.ServiceProvider.GetRequiredService<IProcessStrategyFactory>();
            var strategy2 = factory2.CreateStrategy(ProcessType.Sequential);

            // Different scope should return different instance
            using (var scope1 = serviceProvider.CreateScope())
            {
                var factory1 = scope1.ServiceProvider.GetRequiredService<IProcessStrategyFactory>();
                var strategy1 = factory1.CreateStrategy(ProcessType.Sequential);
                Assert.NotSame(strategy1, strategy2);
            }
        }
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public void ShouldPropagateException_WhenCreateStrategyWhenServiceProviderThrows()
    {
        // Arrange
        var serviceProvider = new TestServiceProvider
        {
            ThrowOnGetService = true,
            ExceptionToThrow = new InvalidOperationException("Dependency injection failed")
        };
        var logger = new TestLogger();
        var factory = new ProcessStrategyFactory(serviceProvider, logger);

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(
            () => factory.CreateStrategy(ProcessType.Sequential));
        Assert.Equal("Dependency injection failed", exception.Message);
    }

    [Fact]
    public void ShouldThrowInvalidOperationException_WhenCreateStrategyWithNullFromServiceProvider()
    {
        // Arrange
        var serviceProvider = new TestServiceProvider(); // Returns null for unregistered services
        var logger = new TestLogger();
        var factory = new ProcessStrategyFactory(serviceProvider, logger);

        // Act & Assert
        // GetRequiredService should throw when service is not found (null)
        // Note: This test depends on the actual implementation of GetRequiredService
        // In a real ServiceProvider, this would throw InvalidOperationException
        var exception = Assert.Throws<InvalidOperationException>(
            () => factory.CreateStrategy(ProcessType.Sequential));
    }

    #endregion

    #region Performance Tests

    [Fact]
    public void ShouldPerformConsistently_WhenCreateStrategyRepeatedCalls()
    {
        // Arrange
        var services = CreateServiceCollectionWithMocks();
        services.AddTransient<SequentialProcessStrategy>();
        var serviceProvider = services.BuildServiceProvider();
        var logger = new TestLogger();
        var factory = new ProcessStrategyFactory(serviceProvider, logger);

        // Act
        var strategies = new List<IProcessStrategy>();
        for (int i = 0; i < 100; i++)
        {
            strategies.Add(factory.CreateStrategy(ProcessType.Sequential));
        }

        // Assert
        Assert.Equal(100, strategies.Count);
        Assert.All(strategies, strategy => Assert.NotNull(strategy));
        Assert.All(strategies, strategy => Assert.IsType<SequentialProcessStrategy>(strategy));

        // Verify all calls were logged
        Assert.Equal(100, logger.LoggedMessages.Count);
        Assert.All(logger.LoggedMessages, message =>
            Assert.Contains("Creating process strategy for type: Sequential", message));
    }

    #endregion
}
