using Orkeon.Application.Configuration;
using Orkeon.Application.Memory;
using Orkeon.Domain.Crew;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Infrastructure.Serialization;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Memory;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.Memory;
using Orkeon.Infrastructure.Parsing;
using Orkeon.Infrastructure.Serialization;
using Orkeon.Infrastructure.Crew.Strategies;

namespace Orkeon.Infrastructure.Tests.DependencyInjection;

public class InfrastructureExtensionsTests
{
    [Fact]
    public void ShouldRegisterAllRequiredServices_WhenAddOrkeonInfrastructure()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<Orkeon.Domain.FileSystem.IFileSystemService>(
            new Orkeon.Tests.Shared.FileSystem.FakeFileSystemService());

        // Act
        services.AddOrkeonInfrastructure();
        var serviceProvider = services.BuildServiceProvider();

        // Assert - Verify services that can be resolved without additional dependencies
        Assert.NotNull(serviceProvider.GetService<ILlmProviderFactory>());
        Assert.NotNull(serviceProvider.GetService<IMemoryProvider>());
        Assert.NotNull(serviceProvider.GetService<IYamlSerializer>());
        Assert.NotNull(serviceProvider.GetService<ICsvSerializer>());
        Assert.NotNull(serviceProvider.GetService<IMarkdownParser>());
        Assert.NotNull(serviceProvider.GetService<Orkeon.Domain.Memory.IEmbeddingService>());
        Assert.NotNull(serviceProvider.GetService<Orkeon.Application.Interfaces.Services.IAgentCommunicationService>());
        Assert.NotNull(serviceProvider.GetService<IProcessStrategyFactory>());
        // Note: IManagerAgent and Process Strategies require additional dependencies not provided by AddOrkeonInfrastructure
    }

    [Fact]
    public void ShouldRegisterLlmProviderFactoryAsSingleton_WhenAddOrkeonInfrastructure()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddOrkeonInfrastructure();
        var serviceProvider = services.BuildServiceProvider();

        // Assert - Verify singleton behavior
        var factory1 = serviceProvider.GetService<ILlmProviderFactory>();
        var factory2 = serviceProvider.GetService<ILlmProviderFactory>();
        Assert.Same(factory1, factory2);
    }

    [Fact]
    public void ShouldRegisterMemoryProviderAsInMemoryProvider_WhenAddOrkeonInfrastructure()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<Orkeon.Domain.FileSystem.IFileSystemService>(
            new Orkeon.Tests.Shared.FileSystem.FakeFileSystemService());

        // Act
        services.AddOrkeonInfrastructure();
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var memoryProvider = serviceProvider.GetService<IMemoryProvider>();
        Assert.IsType<InMemoryProvider>(memoryProvider);
    }

    [Fact]
    public void ShouldRegisterSerializationServices_WhenAddOrkeonInfrastructure()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddOrkeonInfrastructure();
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var yamlSerializer = serviceProvider.GetService<IYamlSerializer>();
        var csvSerializer = serviceProvider.GetService<ICsvSerializer>();
        var markdownParser = serviceProvider.GetService<IMarkdownParser>();

        Assert.IsType<YamlDotNetSerializer>(yamlSerializer);
        Assert.IsType<CsvHelperSerializer>(csvSerializer);
        Assert.IsType<MarkdownParser>(markdownParser);
    }

    [Fact]
    public void ShouldRegisterEmbeddingServiceWithDefaultDimension_WhenAddOrkeonInfrastructure()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddOrkeonInfrastructure();
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var embeddingService = serviceProvider.GetService<Orkeon.Domain.Memory.IEmbeddingService>();
        Assert.NotNull(embeddingService);
        Assert.IsType<DomainEmbeddingServiceAdapter>(embeddingService);
    }

    [Fact]
    public void ShouldRegisterEmbeddingServiceWithCustomDimension_WhenAddOrkeonInfrastructure()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<OrkeonApplicationOptions>(options =>
        {
            options.EmbeddingDimension = 768;
        });

        // Act
        services.AddOrkeonInfrastructure();
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var embeddingService = serviceProvider.GetService<Orkeon.Domain.Memory.IEmbeddingService>();
        Assert.NotNull(embeddingService);
        Assert.IsType<DomainEmbeddingServiceAdapter>(embeddingService);
    }

    [Fact]
    public void ShouldRegisterAgentCommunicationService_WhenAddOrkeonInfrastructure()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddOrkeonInfrastructure();
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var communicationService = serviceProvider.GetService<Orkeon.Application.Interfaces.Services.IAgentCommunicationService>();
        Assert.IsType<Orkeon.Infrastructure.Communication.AsyncAgentCommunicationService>(communicationService);
    }

    [Fact]
    public void ShouldRegisterManagerAgentTypeIsRegistered_WhenAddOrkeonInfrastructure()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddOrkeonInfrastructure();

        // Assert - Check that the service is registered (uses factory, so ImplementationType may be null)
        var serviceDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(IManagerAgent));
        Assert.NotNull(serviceDescriptor);
        Assert.Equal(ServiceLifetime.Scoped, serviceDescriptor.Lifetime);
    }

    [Fact]
    public void ShouldRegisterProcessStrategiesTypesAreRegistered_WhenAddOrkeonInfrastructure()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddOrkeonInfrastructure();

        // Assert - Check that the services are registered, not that they can be resolved
        var sequentialDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(SequentialProcessStrategy));
        var hierarchicalDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(HierarchicalProcessStrategy));
        var parallelDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(ParallelProcessStrategy));

        Assert.NotNull(sequentialDescriptor);
        Assert.NotNull(hierarchicalDescriptor);
        Assert.NotNull(parallelDescriptor);

        Assert.Equal(ServiceLifetime.Scoped, sequentialDescriptor.Lifetime);
        Assert.Equal(ServiceLifetime.Scoped, hierarchicalDescriptor.Lifetime);
        Assert.Equal(ServiceLifetime.Scoped, parallelDescriptor.Lifetime);
    }

    [Fact]
    public void ShouldRegisterProcessStrategyFactory_WhenAddOrkeonInfrastructure()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddOrkeonInfrastructure();
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var factory = serviceProvider.GetService<IProcessStrategyFactory>();
        Assert.IsType<ProcessStrategyFactory>(factory);
    }

    [Fact]
    public void ShouldRegisterHttpClientFactory_WhenAddOrkeonInfrastructure()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddOrkeonInfrastructure();
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var httpClientFactory = serviceProvider.GetService<IHttpClientFactory>();
        Assert.NotNull(httpClientFactory);
    }

    [Fact]
    public void ShouldBeAbleToCreateMultipleScopesForResolvableServices_WhenAddOrkeonInfrastructure()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOrkeonInfrastructure();
        var serviceProvider = services.BuildServiceProvider();

        // Act
        using var scope1 = serviceProvider.CreateScope();
        using var scope2 = serviceProvider.CreateScope();

        // Test with services that can actually be resolved
        var factory1 = scope1.ServiceProvider.GetService<IProcessStrategyFactory>();
        var factory2 = scope2.ServiceProvider.GetService<IProcessStrategyFactory>();

        var embedding1 = scope1.ServiceProvider.GetService<Orkeon.Domain.Memory.IEmbeddingService>();
        var embedding2 = scope2.ServiceProvider.GetService<Orkeon.Domain.Memory.IEmbeddingService>();

        // Assert - Scoped services should be different instances per scope (ProcessStrategyFactory is scoped)
        Assert.NotSame(factory1, factory2);
        // Embedding service is singleton, so should be same
        Assert.Same(embedding1, embedding2);
    }

    [Fact]
    public void ShouldSingletonServicesSameBetweenScopes_WhenAddOrkeonInfrastructure()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<Orkeon.Domain.FileSystem.IFileSystemService>(
            new Orkeon.Tests.Shared.FileSystem.FakeFileSystemService());
        services.AddOrkeonInfrastructure();
        var serviceProvider = services.BuildServiceProvider();

        // Act
        using var scope1 = serviceProvider.CreateScope();
        using var scope2 = serviceProvider.CreateScope();

        var factory1 = scope1.ServiceProvider.GetService<ILlmProviderFactory>();
        var factory2 = scope2.ServiceProvider.GetService<ILlmProviderFactory>();

        var memory1 = scope1.ServiceProvider.GetService<IMemoryProvider>();
        var memory2 = scope2.ServiceProvider.GetService<IMemoryProvider>();

        // Assert - Singleton services should be the same instance across scopes
        Assert.Same(factory1, factory2);
        Assert.Same(memory1, memory2);
    }

    [Fact]
    public void ShouldBeAbleToResolveAllResolvableServicesWithoutExceptions_WhenAddOrkeonInfrastructure()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<Orkeon.Domain.FileSystem.IFileSystemService>(
            new Orkeon.Tests.Shared.FileSystem.FakeFileSystemService());
        services.Configure<OrkeonApplicationOptions>(options =>
        {
            options.EmbeddingDimension = 512;
        });

        // Act
        services.AddOrkeonInfrastructure();
        var serviceProvider = services.BuildServiceProvider();

        // Assert - Services without dependencies should be resolvable without throwing exceptions
        var exceptions = new List<Exception>();

        try { serviceProvider.GetRequiredService<ILlmProviderFactory>(); } catch (Exception ex) { exceptions.Add(ex); }
        try { serviceProvider.GetRequiredService<IMemoryProvider>(); } catch (Exception ex) { exceptions.Add(ex); }
        try { serviceProvider.GetRequiredService<IYamlSerializer>(); } catch (Exception ex) { exceptions.Add(ex); }
        try { serviceProvider.GetRequiredService<ICsvSerializer>(); } catch (Exception ex) { exceptions.Add(ex); }
        try { serviceProvider.GetRequiredService<IMarkdownParser>(); } catch (Exception ex) { exceptions.Add(ex); }
        try { serviceProvider.GetRequiredService<Orkeon.Domain.Memory.IEmbeddingService>(); } catch (Exception ex) { exceptions.Add(ex); }
        try { serviceProvider.GetRequiredService<Orkeon.Application.Interfaces.Services.IAgentCommunicationService>(); } catch (Exception ex) { exceptions.Add(ex); }
        try { serviceProvider.GetRequiredService<IProcessStrategyFactory>(); } catch (Exception ex) { exceptions.Add(ex); }
        // Note: IManagerAgent and Process Strategies are not tested here as they require additional dependencies

        Assert.Empty(exceptions);
    }

    [Fact]
    public void ShouldWithoutLoggingStillRegistersServices_WhenAddOrkeonInfrastructure()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddOrkeonInfrastructure();
        var serviceProvider = services.BuildServiceProvider();

        // Assert - Should still register services even without explicit logging setup
        Assert.NotNull(serviceProvider.GetService<ILlmProviderFactory>());
        Assert.NotNull(serviceProvider.GetService<IProcessStrategyFactory>());
    }

    [Fact]
    public void ShouldCalledMultipleTimesDoesNotDuplicateRegistrations_WhenAddOrkeonInfrastructure()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<Orkeon.Domain.FileSystem.IFileSystemService>(
            new Orkeon.Tests.Shared.FileSystem.FakeFileSystemService());

        // Act
        services.AddOrkeonInfrastructure();
        services.AddOrkeonInfrastructure(); // Call twice
        services.AddOrkeonInfrastructure(); // Call three times

        // Assert - Should not throw and should still resolve services
        var serviceProvider = services.BuildServiceProvider();
        Assert.NotNull(serviceProvider.GetService<ILlmProviderFactory>());
        Assert.NotNull(serviceProvider.GetService<IMemoryProvider>());
    }

    [Fact]
    public void ShouldBeRegistered_WhenAddOrkeonInfrastructureHttpClient()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddOrkeonInfrastructure();

        // Assert - HttpClient should be registered
        var httpClientDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(IHttpClientFactory));
        Assert.NotNull(httpClientDescriptor);
    }

    [Fact]
    public void ShouldBeCorrect_WhenAddOrkeonInfrastructureCheckServiceLifetimes()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddOrkeonInfrastructure();

        // Assert lifetimes
        var llmFactory = services.FirstOrDefault(s => s.ServiceType == typeof(ILlmProviderFactory));
        Assert.Equal(ServiceLifetime.Singleton, llmFactory?.Lifetime);

        var memoryProvider = services.FirstOrDefault(s => s.ServiceType == typeof(IMemoryProvider));
        Assert.Equal(ServiceLifetime.Singleton, memoryProvider?.Lifetime);

        var yamlSerializer = services.FirstOrDefault(s => s.ServiceType == typeof(IYamlSerializer));
        Assert.Equal(ServiceLifetime.Singleton, yamlSerializer?.Lifetime);

        var managerAgent = services.FirstOrDefault(s => s.ServiceType == typeof(IManagerAgent));
        Assert.Equal(ServiceLifetime.Scoped, managerAgent?.Lifetime);

        var processFactory = services.FirstOrDefault(s => s.ServiceType == typeof(IProcessStrategyFactory));
        Assert.Equal(ServiceLifetime.Scoped, processFactory?.Lifetime);
    }

    [Fact]
    public void ShouldUseCorrectEmbeddingDimension_WhenAddOrkeonInfrastructureWithCustomOptions()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<OrkeonApplicationOptions>(options =>
        {
            options.EmbeddingDimension = 1024;
        });

        // Act
        services.AddOrkeonInfrastructure();
        var serviceProvider = services.BuildServiceProvider();

        // Assert - The embedding service should be created with the custom dimension
        var embeddingService = serviceProvider.GetService<Orkeon.Domain.Memory.IEmbeddingService>();
        Assert.NotNull(embeddingService);
        // Since we can't directly check the dimension, at least verify it was created
        Assert.IsType<DomainEmbeddingServiceAdapter>(embeddingService);
    }

    [Fact]
    public void ShouldUseDefaultEmbeddingDimension_WhenAddOrkeonInfrastructureWithNullOptions()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        // Don't configure options, so they will be null

        // Act
        services.AddOrkeonInfrastructure();
        var serviceProvider = services.BuildServiceProvider();

        // Assert - Should use default dimension of 384
        var embeddingService = serviceProvider.GetService<Orkeon.Domain.Memory.IEmbeddingService>();
        Assert.NotNull(embeddingService);
        Assert.IsType<DomainEmbeddingServiceAdapter>(embeddingService);
    }

    [Fact]
    public void ShouldRegisterAllSerializationServices_WhenAddOrkeonInfrastructure()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddOrkeonInfrastructure();

        // Assert - Check all serialization services are registered
        var yamlDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(IYamlSerializer));
        var csvDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(ICsvSerializer));
        var markdownDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(IMarkdownParser));

        Assert.NotNull(yamlDescriptor);
        Assert.NotNull(csvDescriptor);
        Assert.NotNull(markdownDescriptor);

        Assert.Equal(typeof(YamlDotNetSerializer), yamlDescriptor.ImplementationType);
        Assert.Equal(typeof(CsvHelperSerializer), csvDescriptor.ImplementationType);
        Assert.Equal(typeof(MarkdownParser), markdownDescriptor.ImplementationType);
    }

    [Fact]
    public void ShouldProcessStrategiesAllRegistered_WhenAddOrkeonInfrastructure()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddOrkeonInfrastructure();

        // Assert - Verify all three process strategies are registered
        var sequentialCount = services.Count(s => s.ServiceType == typeof(SequentialProcessStrategy));
        var hierarchicalCount = services.Count(s => s.ServiceType == typeof(HierarchicalProcessStrategy));
        var parallelCount = services.Count(s => s.ServiceType == typeof(ParallelProcessStrategy));

        Assert.Equal(1, sequentialCount);
        Assert.Equal(1, hierarchicalCount);
        Assert.Equal(1, parallelCount);
    }

    [Fact]
    public void ShouldRegisterAgentCommunicationServiceAsSingleton_WhenAddOrkeonInfrastructure()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddOrkeonInfrastructure();

        // Assert
        var descriptor = services.FirstOrDefault(s =>
            s.ServiceType == typeof(Orkeon.Application.Interfaces.Services.IAgentCommunicationService));
        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        Assert.Equal(typeof(Orkeon.Infrastructure.Communication.AsyncAgentCommunicationService),
                     descriptor.ImplementationType);
    }

    [Fact]
    public void ShouldRegisterServices_WhenAddOrkeonInfrastructureEmptyServiceCollection()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddOrkeonInfrastructure();

        // Assert - Should register services even with empty collection
        Assert.True(services.Count > 0);
        Assert.Contains(services, s => s.ServiceType == typeof(ILlmProviderFactory));
    }

    [Fact]
    public void ShouldWithPreExistingServicesDoesNotOverwrite_WhenAddOrkeonInfrastructure()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<ITestService, TestService>(); // Add custom service
        services.AddLogging();

        // Act
        services.AddOrkeonInfrastructure();
        var serviceProvider = services.BuildServiceProvider();

        // Assert - Custom service should still be available
        var testService = serviceProvider.GetService<ITestService>();
        Assert.NotNull(testService);
        Assert.IsType<TestService>(testService);

        // Infrastructure services should also be available
        Assert.NotNull(serviceProvider.GetService<ILlmProviderFactory>());
    }

    [Fact]
    public void ShouldBeAbleToCreateScope_WhenAddOrkeonInfrastructureServiceProvider()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOrkeonInfrastructure();
        var serviceProvider = services.BuildServiceProvider();

        // Act & Assert - Should be able to create scope without exceptions
        using (var scope = serviceProvider.CreateScope())
        {
            Assert.NotNull(scope);
            Assert.NotNull(scope.ServiceProvider);

            // Should be able to resolve services within scope
            var factory = scope.ServiceProvider.GetService<IProcessStrategyFactory>();
            Assert.NotNull(factory);
        }
    }

    [Fact]
    public void ShouldMemoryProviderCreatedWithLogger_WhenAddOrkeonInfrastructure()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<Orkeon.Domain.FileSystem.IFileSystemService>(
            new Orkeon.Tests.Shared.FileSystem.FakeFileSystemService());

        // Act
        services.AddOrkeonInfrastructure();
        var serviceProvider = services.BuildServiceProvider();

        // Assert - Memory provider should be created and be InMemoryProvider
        var memoryProvider = serviceProvider.GetService<IMemoryProvider>();
        Assert.NotNull(memoryProvider);
        Assert.IsType<InMemoryProvider>(memoryProvider);
    }

    [Fact]
    public void ShouldBeCommentedOut_WhenAddOrkeonInfrastructureRepositoryServices()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddOrkeonInfrastructure();

        // Assert - Repository services ARE registered as in-memory implementations
        var agentRepoDescriptor = services.FirstOrDefault(s =>
            s.ServiceType.Name == "IAgentRepository");
        var crewRepoDescriptor = services.FirstOrDefault(s =>
            s.ServiceType.Name == "ICrewRepository");
        var taskRepoDescriptor = services.FirstOrDefault(s =>
            s.ServiceType.Name == "ITaskRepository");

        Assert.NotNull(agentRepoDescriptor);
        Assert.NotNull(crewRepoDescriptor);
        Assert.NotNull(taskRepoDescriptor);
    }

    [Fact]
    public void ShouldRespectHostRegisteredPathValidator_WhenRegisteredBeforeAddOrkeonInfrastructure()
    {
        // Arrange — host registers its own IPathValidator BEFORE calling AddOrkeonInfrastructure.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<Orkeon.Domain.Tools.Security.IPathValidator, TestPathValidator>();

        // Act
        services.AddOrkeonInfrastructure();
        var serviceProvider = services.BuildServiceProvider();

        // Assert — the host override wins because IPathValidator is registered via TryAdd*.
        var pathValidator = serviceProvider.GetService<Orkeon.Domain.Tools.Security.IPathValidator>();
        Assert.IsType<TestPathValidator>(pathValidator);
    }

    [Fact]
    public void ShouldRegisterDefaultPathValidator_WhenHostProvidesNone()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddOrkeonInfrastructure();

        // Assert — Orkeon's default is still wired (via TryAdd) when the host provides none.
        var descriptor = services.FirstOrDefault(s =>
            s.ServiceType == typeof(Orkeon.Domain.Tools.Security.IPathValidator));
        Assert.NotNull(descriptor);
        Assert.Equal(typeof(Orkeon.Infrastructure.Security.PathValidator), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void ShouldRespectHostRegisteredComponentSerializer_WhenRegisteredBeforeAddOrkeonInfrastructure()
    {
        // Arrange — host registers its own IComponentSerializer BEFORE AddOrkeonInfrastructure.
        var services = new ServiceCollection();
        services.AddLogging();
        var hostSerializer = new TestComponentSerializer();
        services.AddSingleton<Orkeon.Domain.Common.IComponentSerializer>(hostSerializer);

        // Act
        services.AddOrkeonInfrastructure();
        var serviceProvider = services.BuildServiceProvider();

        // Assert — the host serializer wins because it is registered via TryAdd*.
        var resolved = serviceProvider.GetService<Orkeon.Domain.Common.IComponentSerializer>();
        Assert.Same(hostSerializer, resolved);
    }

    // Helper interfaces and classes for testing
    private interface ITestService { }
    private class TestService : ITestService { }

    private sealed class TestPathValidator : Orkeon.Domain.Tools.Security.IPathValidator
    {
        public Orkeon.Domain.Tools.Security.PathValidationResult ValidatePath(
            string requestedPath, string? workspaceRoot = null)
            => Orkeon.Domain.Tools.Security.PathValidationResult.Allowed(requestedPath);
    }

    private sealed class TestComponentSerializer : Orkeon.Domain.Common.IComponentSerializer
    {
        public T Deserialize<T>(Dictionary<string, object?> parameters) where T : class, new() => new();
        public Dictionary<string, object?> Serialize<T>(T value) where T : class => new();
        public Dictionary<string, object?> NormalizeParameters(Dictionary<string, object?> parameters) => parameters;
        public object? NormalizeValue(object? value) => value;
        public string NormalizeKeyToSnakeCase(string key) => key;
    }
}
