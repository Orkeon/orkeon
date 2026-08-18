using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Logging;
using Orkeon.Domain.FileSystem;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.Logging;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.DependencyInjection;

/// <summary>
/// SONAR-14: pins the LLM exchange logging DI wiring — the composite registration,
/// the file-only variant, the per-client builder hook and the argument guards.
/// </summary>
public class LlmLoggingExtensionsTests
{
    private static ServiceCollection NewServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService().AddMount("/logs"));
        return services;
    }

    [Fact]
    public void AddLlmExchangeLogging_WiresTheCompositeLogger_AndTheHandler()
    {
        var services = NewServices();

        var returned = services.AddLlmExchangeLogging("/logs");

        Assert.Same(services, returned);
        using var provider = services.BuildServiceProvider();

        Assert.IsType<CompositeLlmExchangeLogger>(provider.GetRequiredService<ILlmExchangeLogger>());
        Assert.NotNull(provider.GetRequiredService<LlmExchangeJsonLogger>());
        Assert.NotNull(provider.GetRequiredService<LlmExchangeStructuredLogger>());
        Assert.NotNull(provider.GetRequiredService<LlmLoggingDelegatingHandler>());
        Assert.NotNull(provider.GetRequiredService<LlmLoggingOptions>());

        // Transient handler: two resolutions are two instances.
        Assert.NotSame(
            provider.GetRequiredService<LlmLoggingDelegatingHandler>(),
            provider.GetRequiredService<LlmLoggingDelegatingHandler>());
    }

    [Fact]
    public void AddLlmExchangeLogging_KeepsExplicitOptions()
    {
        var services = NewServices();
        var options = new LlmLoggingOptions { MaxBodyLengthChars = 42 };

        services.AddLlmExchangeLogging("/logs", options);

        using var provider = services.BuildServiceProvider();
        Assert.Same(options, provider.GetRequiredService<LlmLoggingOptions>());
    }

    [Fact]
    public void AddLlmExchangeFileLogging_RegistersTheJsonLoggerAlone()
    {
        var services = NewServices();

        services.AddLlmExchangeFileLogging("/logs");

        using var provider = services.BuildServiceProvider();
        Assert.IsType<LlmExchangeJsonLogger>(provider.GetRequiredService<ILlmExchangeLogger>());
        Assert.NotNull(provider.GetRequiredService<LlmLoggingDelegatingHandler>());
    }

    [Fact]
    public void TheHttpClientBuilderOverload_ChainsOnTheBuilder()
    {
        var services = NewServices();
        services.AddLlmExchangeLogging("/logs");

        var builder = services.AddHttpClient("probe");
        Assert.Same(builder, builder.AddLlmExchangeLogging());

        // The pipeline builds: creating the client applies the handler chain.
        using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("probe");
        Assert.NotNull(client);
    }

    [Fact]
    public void TheExtensions_GuardTheirArguments()
    {
        var services = NewServices();

        Assert.Throws<ArgumentNullException>(() =>
            LlmLoggingExtensions.AddLlmExchangeLogging(null!, "/logs"));
        Assert.Throws<ArgumentException>(() => services.AddLlmExchangeLogging(" "));
        Assert.Throws<ArgumentNullException>(() =>
            LlmLoggingExtensions.AddLlmExchangeFileLogging(null!, "/logs"));
        Assert.Throws<ArgumentException>(() => services.AddLlmExchangeFileLogging(""));
        Assert.Throws<ArgumentNullException>(() =>
            LlmLoggingExtensions.AddLlmExchangeLogging((IHttpClientBuilder)null!));
    }
}
