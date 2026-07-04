using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.AgentCommunication;
using Orkeon.Application.Interfaces.Compliance;
using Orkeon.Application.Interfaces.Monitoring;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Application.Evaluation;
using Orkeon.Application.MultiModal;
using Orkeon.Application.Validation;
using Orkeon.Domain.FileSystem;
using Orkeon.Infrastructure.AgentCommunication;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.Evaluation;
using Orkeon.Infrastructure.MultiModal;
using Orkeon.Infrastructure.Security.Dlp;
using Orkeon.Infrastructure.Security.Encryption;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.DependencyInjection;

/// <summary>
/// R4.9 (MORT-003) — Opt-in subsystems: each dormant subsystem must NOT be registered
/// by the default <c>AddOrkeonInfrastructure()</c> overloads, and its explicit
/// <c>AddOrkeonXxx()</c> activation must produce a resolvable service graph.
/// See <c>docs/reference/opt-in-subsystems.md</c>.
/// </summary>
public class OptInSubsystemsRegistrationTests
{
    /// <summary>
    /// The 13 dormant ports of MORT-003 registered by Infrastructure extensions,
    /// plus <see cref="IMultiModalContentLoader"/> added by the R3.9 vision wiring
    /// (also opt-in, registered only by <c>AddOrkeonMultiModal()</c>).
    /// </summary>
    public static TheoryData<Type> DormantPortTypes() => new()
    {
        typeof(IA2AServer),
        typeof(IA2AClient),
        typeof(IA2AAgentDiscovery),
        typeof(IMetricsAggregation),
        typeof(ITraceExplorer),
        typeof(INistComplianceReporter),
        typeof(IBenchmarkRunner),
        typeof(IContentValidationService),
        typeof(IMultiModalContentLoader),
        typeof(IDlpPolicyProvider),
        typeof(ITokenBudgetTracker),
        typeof(IToolRateLimiter),
        typeof(IKeyRotationService),
    };

    private static ServiceCollection NewServices(bool withConfiguration = false)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService());
        if (withConfiguration)
        {
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        }
        return services;
    }

    // -------------------------------------------------------------------------
    // 1. Default registration excludes every dormant port
    // -------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(DormantPortTypes))]
    public void ShouldNotRegisterDormantPort_WhenAddOrkeonInfrastructure(Type portType)
    {
        // Arrange
        var services = NewServices();

        // Act
        services.AddOrkeonInfrastructure();

        // Assert — the dormant port must not appear in the default registrations.
        Assert.DoesNotContain(services, d => d.ServiceType == portType);
    }

    [Theory]
    [MemberData(nameof(DormantPortTypes))]
    public void ShouldNotRegisterDormantPort_WhenAddOrkeonInfrastructureWithConfiguration(Type portType)
    {
        // Arrange
        var configuration = new ConfigurationBuilder().Build();
        var services = NewServices();

        // Act
        services.AddOrkeonInfrastructure(configuration);

        // Assert
        Assert.DoesNotContain(services, d => d.ServiceType == portType);
    }

    [Fact]
    public void ShouldNotRegisterDlpInterceptors_WhenAddOrkeonInfrastructure()
    {
        // Arrange
        var services = NewServices();

        // Act
        services.AddOrkeonInfrastructure();

        // Assert — the 5-channel DLP interceptors are part of the opt-in DLP subsystem.
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IDlpInterceptor));
    }

    // -------------------------------------------------------------------------
    // 2. Each opt-in extension yields a resolvable graph on its own
    // -------------------------------------------------------------------------

    [Fact]
    public void ShouldResolveMonitoringServices_WhenAddOrkeonMonitoring()
    {
        // Arrange
        var services = NewServices();

        // Act
        services.AddOrkeonMonitoring();
        using var provider = services.BuildServiceProvider();

        // Assert
        Assert.NotNull(provider.GetRequiredService<IMetricsAggregation>());
        Assert.NotNull(provider.GetRequiredService<ITraceExplorer>());
    }

    [Fact]
    public void ShouldResolveNistReporter_WhenAddOrkeonNistCompliance()
    {
        // Arrange
        var services = NewServices();

        // Act
        services.AddOrkeonNistCompliance();
        using var provider = services.BuildServiceProvider();

        // Assert — the extension TryAdds its own fallback audit chain.
        Assert.NotNull(provider.GetRequiredService<INistComplianceReporter>());
    }

    [Fact]
    public void ShouldResolveNistReporter_WhenAddOrkeonNistComplianceOnTopOfDefaults()
    {
        // Arrange — opt-in used the documented way: after AddOrkeonInfrastructure().
        var services = NewServices(withConfiguration: true);
        services.AddOrkeonInfrastructure();

        // Act
        services.AddOrkeonNistCompliance();
        using var provider = services.BuildServiceProvider();

        // Assert
        Assert.NotNull(provider.GetRequiredService<INistComplianceReporter>());
    }

    [Fact]
    public void ShouldResolveDlpServices_WhenAddOrkeonDlp()
    {
        // Arrange — options bind from configuration, so a host configuration is required.
        var services = NewServices(withConfiguration: true);

        // Act
        services.AddOrkeonDlp();
        using var provider = services.BuildServiceProvider();

        // Assert
        Assert.NotNull(provider.GetRequiredService<IPiiDetector>());
        Assert.NotNull(provider.GetRequiredService<IDlpPolicyProvider>());
        Assert.Equal(5, provider.GetServices<IDlpInterceptor>().Count());
    }

    [Fact]
    public void ShouldNotDuplicateDlpInterceptors_WhenAddOrkeonDlpCalledTwice()
    {
        // Arrange
        var services = NewServices(withConfiguration: true);

        // Act
        services.AddOrkeonDlp();
        services.AddOrkeonDlp();
        using var provider = services.BuildServiceProvider();

        // Assert — TryAddEnumerable keeps the interceptor set idempotent.
        Assert.Equal(5, provider.GetServices<IDlpInterceptor>().Count());
    }

    [Fact]
    public void ShouldResolveToolRateLimitingServices_WhenAddOrkeonToolRateLimiting()
    {
        // Arrange — options bind from configuration, so a host configuration is required.
        var services = NewServices(withConfiguration: true);

        // Act
        services.AddOrkeonToolRateLimiting();
        using var provider = services.BuildServiceProvider();

        // Assert
        Assert.NotNull(provider.GetRequiredService<ITokenBudgetTracker>());
        Assert.NotNull(provider.GetRequiredService<IToolRateLimiter>());
    }

    [Fact]
    public void ShouldResolveKeyRotationService_WhenAddOrkeonKeyRotation()
    {
        // Arrange
        var services = NewServices();

        // Act
        services.AddOrkeonKeyRotation();
        using var provider = services.BuildServiceProvider();

        // Assert
        Assert.NotNull(provider.GetRequiredService<IKeyRotationService>());
    }

    [Fact]
    public void ShouldResolveBenchmarkRunner_WhenAddOrkeonBenchmarking()
    {
        // Arrange
        var services = NewServices();

        // Act
        services.AddOrkeonBenchmarking();
        using var provider = services.BuildServiceProvider();

        // Assert
        Assert.NotNull(provider.GetRequiredService<IBenchmarkRunner>());
    }

    [Fact]
    public void ShouldResolveContentValidationService_WhenAddOrkeonMultiModalWithoutConfiguration()
    {
        // Arrange
        var services = NewServices();

        // Act
        services.AddOrkeonMultiModal();
        using var provider = services.BuildServiceProvider();

        // Assert — default options are used when no configuration is supplied.
        Assert.NotNull(provider.GetRequiredService<IContentValidationService>());
    }

    [Fact]
    public void ShouldResolveContentValidationService_WhenAddOrkeonMultiModalWithConfiguration()
    {
        // Arrange
        var configuration = new ConfigurationBuilder().Build();
        var services = NewServices();

        // Act
        services.AddOrkeonMultiModal(configuration);
        using var provider = services.BuildServiceProvider();

        // Assert
        Assert.NotNull(provider.GetRequiredService<IContentValidationService>());
    }

    [Fact]
    public void ShouldResolveMultiModalContentLoader_WhenAddOrkeonMultiModalWithFileSystem()
    {
        // Arrange — the loader requires IFileSystemService (AddOrkeonFileSystem in real hosts).
        var services = NewServices();
        services.AddSingleton<IFileSystemService>(
            new FakeFileSystemService().AddMount("/workspace", FileAccessRights.ReadOnly));

        // Act
        services.AddOrkeonMultiModal();
        using var provider = services.BuildServiceProvider();

        // Assert — R3.9: the vision loading port is registered by the opt-in extension.
        Assert.NotNull(provider.GetRequiredService<IMultiModalContentLoader>());
    }

    [Fact]
    public void ShouldResolveA2AClientStack_WhenAddOrkeonA2AWithoutConfiguration()
    {
        // Arrange
        var services = NewServices();

        // Act — action-based overload; the server stays off by default.
        services.AddOrkeonA2A();
        using var provider = services.BuildServiceProvider();

        // Assert — the extension TryAdds its own HTTP + repository dependencies.
        Assert.NotNull(provider.GetRequiredService<IA2AAgentDiscovery>());
        Assert.NotNull(provider.GetRequiredService<IA2AClient>());
        Assert.NotNull(provider.GetRequiredService<IA2ATaskRouter>());
        Assert.Null(provider.GetService<IA2AServer>());
    }

    [Fact]
    public void ShouldResolveA2AServer_WhenAddOrkeonA2AWithServerEnabled()
    {
        // Arrange
        var services = NewServices();

        // Act
        services.AddOrkeonA2A(options => options.EnableServer = true);
        using var provider = services.BuildServiceProvider();

        // Assert
        Assert.NotNull(provider.GetRequiredService<IA2AServer>());
    }

    [Fact]
    public void ShouldResolveA2AServer_WhenAddOrkeonA2AWithConfigurationEnablingServer()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["A2A:EnableServer"] = "true",
            })
            .Build();
        var services = NewServices();

        // Act
        services.AddOrkeonA2A(configuration);
        using var provider = services.BuildServiceProvider();

        // Assert
        Assert.NotNull(provider.GetRequiredService<IA2AServer>());
    }

    // -------------------------------------------------------------------------
    // 3. Opt-ins compose with the defaults (TryAdd — no double registration)
    // -------------------------------------------------------------------------

    [Fact]
    public void ShouldResolveAllOptInPorts_WhenEveryOptInIsActivatedOnTopOfDefaults()
    {
        // Arrange
        var services = NewServices(withConfiguration: true);
        services.AddOrkeonInfrastructure();

        // Act — a host opting into everything.
        services.AddOrkeonA2A(options => options.EnableServer = true);
        services.AddOrkeonMonitoring();
        services.AddOrkeonNistCompliance();
        services.AddOrkeonDlp();
        services.AddOrkeonToolRateLimiting();
        services.AddOrkeonKeyRotation();
        services.AddOrkeonBenchmarking();
        services.AddOrkeonMultiModal();
        using var provider = services.BuildServiceProvider();

        // Assert — every dormant port is now resolvable.
        Assert.NotNull(provider.GetRequiredService<IA2AServer>());
        Assert.NotNull(provider.GetRequiredService<IA2AClient>());
        Assert.NotNull(provider.GetRequiredService<IA2AAgentDiscovery>());
        Assert.NotNull(provider.GetRequiredService<IMetricsAggregation>());
        Assert.NotNull(provider.GetRequiredService<ITraceExplorer>());
        Assert.NotNull(provider.GetRequiredService<INistComplianceReporter>());
        Assert.NotNull(provider.GetRequiredService<IBenchmarkRunner>());
        Assert.NotNull(provider.GetRequiredService<IContentValidationService>());
        Assert.NotNull(provider.GetRequiredService<IDlpPolicyProvider>());
        Assert.NotNull(provider.GetRequiredService<ITokenBudgetTracker>());
        Assert.NotNull(provider.GetRequiredService<IToolRateLimiter>());
        Assert.NotNull(provider.GetRequiredService<IKeyRotationService>());
    }

    [Fact]
    public void ShouldStillRegisterLlmRateLimiter_WhenAddOrkeonInfrastructure()
    {
        // Arrange — guard: only TOOL-level rate limiting moved to opt-in; the LLM-level
        // rate limiter is consumed by the execution pipeline and must stay default.
        var services = NewServices();

        // Act
        services.AddOrkeonInfrastructure();

        // Assert
        Assert.Contains(services, d => d.ServiceType == typeof(ILlmRateLimiter));
    }

    [Fact]
    public void ShouldStillRegisterEncryptionProvider_WhenAddOrkeonInfrastructure()
    {
        // Arrange — guard: only key rotation moved to opt-in; the encryption provider
        // backs the encrypted memory decorators and must stay default.
        var services = NewServices();

        // Act
        services.AddOrkeonInfrastructure();

        // Assert
        Assert.Contains(services, d => d.ServiceType == typeof(IEncryptionProvider));
    }
}
