using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Logging;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.Logging;
using Orkeon.Infrastructure.Tests.Doubles;
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
    public async Task AnExcludedClient_LeavesTheExchangeLog_WhileTheOthersStayInIt()
    {
        var services = NewServices();
        var exchanges = new RecordingExchangeLogger();
        services.AddSingleton<ILlmExchangeLogger>(exchanges);
        services.AddLlmExchangeLogging("/logs");
        services.AddSingleton(new LlmExchangeLogExclusion("orkeon.drive"));
        foreach (var name in new[] { "orkeon.drive", "llm" })
            services.AddHttpClient(name).ConfigurePrimaryHttpMessageHandler(() => new MockHttpMessageHandler());

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        using (var drive = factory.CreateClient("orkeon.drive"))
            await drive.GetAsync(new Uri("https://www.googleapis.com/drive/v3/files?upload_id=secret"), TestContext.Current.CancellationToken);
        using (var llm = factory.CreateClient("llm"))
            await llm.GetAsync(new Uri("https://api.example.com/v1/models"), TestContext.Current.CancellationToken);

        var exchange = Assert.Single(exchanges.Records);
        Assert.Equal("api.example.com", exchange.RequestUrl?.Host);
    }

    [Fact]
    public void TheExclusion_RefusesAnEmptyClientName() =>
        Assert.Throws<ArgumentException>(() => new LlmExchangeLogExclusion(" "));

    [Fact]
    public void TheExtensions_GuardTheirArguments()
    {
        var services = NewServices();

        Assert.Throws<ArgumentNullException>(() =>
            LlmLoggingExtensions.AddLlmExchangeLogging(null!, "/logs"));
        Assert.Throws<ArgumentException>(() => services.AddLlmExchangeLogging(" "));
        Assert.Throws<ArgumentNullException>(() =>
            LlmLoggingExtensions.AddLlmExchangeLogging((IHttpClientBuilder)null!));
    }

    private sealed class RecordingExchangeLogger : ILlmExchangeLogger
    {
        private readonly List<LlmExchangeRecord> _records = [];

        public IReadOnlyList<LlmExchangeRecord> Records
        {
            get { lock (_records) { return [.. _records]; } }
        }

        public Task LogExchangeAsync(LlmExchangeRecord exchange, CancellationToken cancellationToken = default)
        {
            lock (_records)
                _records.Add(exchange);
            return Task.CompletedTask;
        }
    }
}
