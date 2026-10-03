using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orkeon.Application.Common.CQRS;
using Orkeon.Application.Configuration;
using Orkeon.Application.DependencyInjection;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Common;

namespace Orkeon.Application.Tests.DependencyInjection;

public class ServiceCollectionExtensionsTests
{
    // ── AddOrkeonApplication (parameterless) ──

    [Fact]
    public void ShouldRegisterCoreServices_WhenCallingAddOrkeonApplication()
    {
        // Arrange
        var services = CreateServiceCollectionWithRequiredDeps();

        // Act
        services.AddOrkeonApplication();

        // Assert — check that key service descriptors are present
        Assert.Contains(services, sd =>
            sd.ServiceType == typeof(IAgentExecutionService));
        Assert.Contains(services, sd =>
            sd.ServiceType == typeof(IMemoryService));
        Assert.Contains(services, sd =>
            sd.ServiceType == typeof(IExecutionOrchestrator));
    }

    [Fact]
    public void ShouldRegisterCommandHandlers_WhenCallingAddOrkeonApplication()
    {
        // Arrange
        var services = CreateServiceCollectionWithRequiredDeps();

        // Act
        services.AddOrkeonApplication();

        // Assert — at least one ICommandHandler registration should exist
        Assert.Contains(services, sd =>
            sd.ServiceType.IsGenericType &&
            sd.ServiceType.GetGenericTypeDefinition() == typeof(ICommandHandler<,>));
    }

    [Fact]
    public void ShouldRegisterQueryHandlers_WhenCallingAddOrkeonApplication()
    {
        // Arrange
        var services = CreateServiceCollectionWithRequiredDeps();

        // Act
        services.AddOrkeonApplication();

        // Assert
        Assert.Contains(services, sd =>
            sd.ServiceType.IsGenericType &&
            sd.ServiceType.GetGenericTypeDefinition() == typeof(IQueryHandler<,>));
    }

    // ── AddOrkeonApplication (with options) ──

    [Fact]
    public void ShouldApplyOptions_WhenCallingAddOrkeonApplicationWithConfigure()
    {
        // Arrange
        var services = CreateServiceCollectionWithRequiredDeps();

        // Act
        services.AddOrkeonApplication(opt => { opt.AgentSelectionStrategy = AgentSelectionStrategyKind.Skill; });

        // Assert — the value the container resolves, not a registered descriptor
        Assert.Equal(AgentSelectionStrategyKind.Skill, ResolvedStrategy(services));
    }

    [Fact]
    public void ShouldRegisterMemoryService_WhenCallingAddOrkeonApplicationWithOptions()
    {
        // Arrange
        var services = CreateServiceCollectionWithRequiredDeps();

        // Act
        services.AddOrkeonApplication(opt => { opt.AgentSelectionStrategy = AgentSelectionStrategyKind.Skill; });

        // Assert
        Assert.Contains(services, sd =>
            sd.ServiceType == typeof(IMemoryService));
    }

    [Fact]
    public void ShouldReturnServiceCollection_WhenCallingAddOrkeonApplication()
    {
        // Arrange
        var services = CreateServiceCollectionWithRequiredDeps();

        // Act
        var returned = services.AddOrkeonApplication();

        // Assert — fluent API returns same collection
        Assert.Same(services, returned);
    }

    [Fact]
    public void ShouldReturnServiceCollection_WhenCallingAddOrkeonApplicationWithOptions()
    {
        // Arrange
        var services = CreateServiceCollectionWithRequiredDeps();

        // Act
        var returned = services.AddOrkeonApplication(opt => { });

        // Assert
        Assert.Same(services, returned);
    }

    [Fact]
    public void ShouldRegisterCommandHandlers_WhenCallingAddOrkeonApplicationWithOptions()
    {
        // Arrange — regression test (R4.3 / SML-002): the options overload previously
        // skipped AddCqrsHandlers, so no ICommandHandler was registered through this path.
        var services = CreateServiceCollectionWithRequiredDeps();

        // Act
        services.AddOrkeonApplication(opt => { opt.AgentSelectionStrategy = AgentSelectionStrategyKind.Skill; });

        // Assert — at least one ICommandHandler registration must exist through the options path.
        Assert.Contains(services, sd =>
            sd.ServiceType.IsGenericType &&
            sd.ServiceType.GetGenericTypeDefinition() == typeof(ICommandHandler<,>));
    }

    [Fact]
    public void ShouldRegisterCommandValidators_WhenCallingAddOrkeonApplicationWithOptions()
    {
        // Arrange — regression test (R4.3 / SML-002): the options overload previously
        // skipped AddCommandValidators.
        var services = CreateServiceCollectionWithRequiredDeps();

        // Act
        services.AddOrkeonApplication(opt => { opt.AgentSelectionStrategy = AgentSelectionStrategyKind.Skill; });

        // Assert — at least one ICommandValidator registration must exist through the options path.
        Assert.Contains(services, sd =>
            sd.ServiceType.IsGenericType &&
            sd.ServiceType.GetGenericTypeDefinition() == typeof(Orkeon.Application.Validation.ICommandValidator<>));
    }

    [Fact]
    public void ShouldNotDuplicateDeliverableResolvers_WhenCallingAddOrkeonApplicationTwice()
    {
        // Arrange — idempotency test (R4.3 / ANT-012): a double invocation must not
        // register the same IDeliverableResolver implementations twice.
        var services = CreateServiceCollectionWithRequiredDeps();

        // Act
        services.AddOrkeonApplication();
        services.AddOrkeonApplication();

        // Assert — exactly two resolver implementations (FinalMessage + StructuredOutput),
        // not four.
        var resolverDescriptors = services
            .Where(sd => sd.ServiceType == typeof(Orkeon.Application.Crew.DeliverableResolvers.IDeliverableResolver))
            .ToList();
        Assert.Equal(2, resolverDescriptors.Count);
    }

    // ── Both overloads register identical services (AUDIT-P2-02) ──

    [Fact]
    public void BothOverloads_ShouldRegisterIdenticalServiceTypes()
    {
        // Arrange
        var services1 = CreateServiceCollectionWithRequiredDeps();
        var services2 = CreateServiceCollectionWithRequiredDeps();

        // Act
        services1.AddOrkeonApplication();
        services2.AddOrkeonApplication(opts => { });

        // Assert — both overloads should register the same set of service types
        // (the options overload may add IConfigureOptions, so we compare the common core set)
        var coreServiceTypes = new[]
        {
            typeof(IAgentExecutionService),
            typeof(IExecutionOrchestrator),
            typeof(ICallbackOrchestrator),
            typeof(IMemoryCoordinator),
            typeof(IMemoryService),
            typeof(IMemorySearchService),
        };

        foreach (var serviceType in coreServiceTypes)
        {
            Assert.Contains(services1, sd => sd.ServiceType == serviceType);
            Assert.Contains(services2, sd => sd.ServiceType == serviceType);
        }
    }

    [Fact]
    public void BothOverloads_ShouldRegisterExecutionOrchestrator()
    {
        // Arrange
        var services1 = CreateServiceCollectionWithRequiredDeps();
        var services2 = CreateServiceCollectionWithRequiredDeps();

        // Act
        services1.AddOrkeonApplication();
        services2.AddOrkeonApplication(opts => { });

        // Assert — both should have exactly one IExecutionOrchestrator descriptor
        var count1 = services1.Count(sd => sd.ServiceType == typeof(IExecutionOrchestrator));
        var count2 = services2.Count(sd => sd.ServiceType == typeof(IExecutionOrchestrator));

        Assert.Equal(1, count1);
        Assert.Equal(1, count2);
    }

    [Fact]
    public void BothOverloads_ShouldResolveExecutionOrchestrator()
    {
        // Arrange & Act
        var services1 = CreateServiceCollectionWithRequiredDeps();
        services1.AddOrkeonApplication();
        using var provider1 = services1.BuildServiceProvider();

        var services2 = CreateServiceCollectionWithRequiredDeps();
        services2.AddOrkeonApplication(opts => { });
        using var provider2 = services2.BuildServiceProvider();

        // Assert — both providers should resolve IExecutionOrchestrator to the same concrete type
        using var scope1 = provider1.CreateScope();
        using var scope2 = provider2.CreateScope();
        var orch1 = scope1.ServiceProvider.GetRequiredService<IExecutionOrchestrator>();
        var orch2 = scope2.ServiceProvider.GetRequiredService<IExecutionOrchestrator>();

        Assert.NotNull(orch1);
        Assert.NotNull(orch2);
        Assert.Equal(orch1.GetType(), orch2.GetType());
    }

    // ── GAP-37: the lambda is registered as is, so it composes with Configure<…> in both orders ──

    [Fact]
    public void AddOrkeonApplication_WithALambda_KeepsTheStrategyConfiguredBefore()
    {
        var services = CreateServiceCollectionWithRequiredDeps();
        services.Configure<OrkeonApplicationOptions>(o => o.AgentSelectionStrategy = AgentSelectionStrategyKind.Skill);

        services.AddOrkeonApplication(o => { });

        Assert.Equal(AgentSelectionStrategyKind.Skill, ResolvedStrategy(services));
    }

    [Fact]
    public void AddOrkeonApplication_AppliesTheLambda()
    {
        var services = CreateServiceCollectionWithRequiredDeps();

        services.AddOrkeonApplication(o => o.AgentSelectionStrategy = AgentSelectionStrategyKind.Embedding);

        Assert.Equal(AgentSelectionStrategyKind.Embedding, ResolvedStrategy(services));
    }

    [Fact]
    public void AddOrkeonApplication_WithALambda_YieldsToAConfigureRegisteredAfter()
    {
        var services = CreateServiceCollectionWithRequiredDeps();
        services.AddOrkeonApplication(o => o.AgentSelectionStrategy = AgentSelectionStrategyKind.Embedding);

        services.Configure<OrkeonApplicationOptions>(o => o.AgentSelectionStrategy = AgentSelectionStrategyKind.Skill);

        Assert.Equal(AgentSelectionStrategyKind.Skill, ResolvedStrategy(services));
    }

    private static AgentSelectionStrategyKind ResolvedStrategy(ServiceCollection services)
    {
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<OrkeonApplicationOptions>>().Value.AgentSelectionStrategy;
    }

    // Helper to provide minimal required dependencies
    private static ServiceCollection CreateServiceCollectionWithRequiredDeps()
    {
        var services = new ServiceCollection();
        // The registration code needs logging and a few infrastructure interfaces
        services.AddLogging();
        // Provide stubs for external dependencies that the factory lambdas resolve
        services.AddScoped<IBasicLlmProvider, NullLlmProvider>();
        services.AddScoped<IUnitOfWork, NullUnitOfWork>();
        // VFS-70: ExecutionOrchestrator (chatClient overloads) now requires a non-nullable
        // IFileSystemService — a host prerequisite, provided here by an in-memory fake.
        services.AddSingleton<Orkeon.Domain.FileSystem.IFileSystemService>(
            new Orkeon.Tests.Shared.FileSystem.FakeFileSystemService());
        return services;
    }

    // ── Minimal test doubles ──

    private sealed class NullLlmProvider : IBasicLlmProvider
    {
        public string Name => "null";
        public System.Threading.Tasks.Task<string> ChatAsync(string message, Orkeon.Domain.SharedKernel.ValueObjects.LlmConfig? config = null, CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.FromResult(string.Empty);
        public System.Threading.Tasks.Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.FromResult(true);
    }

    private sealed class NullUnitOfWork : IUnitOfWork
    {
        public void Track(IHasDomainEvents aggregate) { }
        public System.Threading.Tasks.Task<int> SaveChangesAsync(CancellationToken ct = default) => System.Threading.Tasks.Task.FromResult(0);
    }
}
