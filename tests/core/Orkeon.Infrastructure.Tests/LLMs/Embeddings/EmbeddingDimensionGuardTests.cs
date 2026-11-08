using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orkeon.Domain.FileSystem;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.LLMs.Embeddings;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.LLMs.Embeddings;

public class EmbeddingDimensionGuardTests
{
    private static MockEmbeddingProvider Provider(string name, string model, int dimensions) =>
        new() { Name = name, Model = model, Dimensions = dimensions };

    private static ServiceProvider BuildHost(IEmbeddingFingerprintStore store, params (string Key, string Value)[] settings) =>
        BuildHost(store, null, settings);

    private static ServiceProvider BuildHost(
        IEmbeddingFingerprintStore store, FakeLoggerFactory? loggers, params (string Key, string Value)[] settings)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        if (loggers is not null)
            services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(loggers);
        services.AddSingleton(store);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => (string?)s.Value))
            .Build());
        services.AddOrkeonInfrastructure();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task ShouldRecordTheFingerprint_WhenNothingIsStoredYet()
    {
        var store = new MockEmbeddingFingerprintStore();

        await EmbeddingDimensionGuard.CheckAsync(Provider("Mistral", "mistral-embed", 1024), store, TestContext.Current.CancellationToken);

        Assert.Equal(new EmbeddingFingerprint("Mistral", "mistral-embed", 1024), store.Stored);
    }

    [Fact]
    public async Task ShouldPass_WhenTheStoredFingerprintMatches()
    {
        var store = new MockEmbeddingFingerprintStore { Stored = new EmbeddingFingerprint("Mistral", "mistral-embed", 1024) };

        await EmbeddingDimensionGuard.CheckAsync(Provider("Mistral", "mistral-embed", 1024), store, TestContext.Current.CancellationToken);

        Assert.Equal(0, store.SaveCallCount);
    }

    [Fact]
    public async Task ShouldRefuseAndKeepTheStoredFingerprint_WhenTheDimensionsChange()
    {
        var old = new EmbeddingFingerprint("OpenAI", "text-embedding-3-small", 1536);
        var store = new MockEmbeddingFingerprintStore { Stored = old };

        var ex = await Assert.ThrowsAsync<EmbeddingModelChangedException>(() =>
            EmbeddingDimensionGuard.CheckAsync(Provider("Mistral", "mistral-embed", 1024), store, TestContext.Current.CancellationToken));

        Assert.Contains("1536", ex.Message, StringComparison.Ordinal);
        Assert.Contains("1024", ex.Message, StringComparison.Ordinal);
        Assert.Contains("mistral-embed", ex.Message, StringComparison.Ordinal);
        Assert.Equal(old, store.Stored);
        Assert.Equal(0, store.SaveCallCount);
    }

    [Fact]
    public async Task ShouldRefuse_WhenOnlyTheModelChangesAtTheSameDimensions()
    {
        var store = new MockEmbeddingFingerprintStore { Stored = new EmbeddingFingerprint("Mistral", "model-a", 1024) };

        await Assert.ThrowsAsync<EmbeddingModelChangedException>(() =>
            EmbeddingDimensionGuard.CheckAsync(Provider("Mistral", "model-b", 1024), store, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldRefuse_WhenOnlyTheProviderChangesAtTheSameModelAndDimensions()
    {
        var store = new MockEmbeddingFingerprintStore { Stored = new EmbeddingFingerprint("Mistral", "shared-model", 1024) };

        var ex = await Assert.ThrowsAsync<EmbeddingModelChangedException>(() =>
            EmbeddingDimensionGuard.CheckAsync(Provider("Gemini", "shared-model", 1024), store, TestContext.Current.CancellationToken));

        Assert.Contains("Mistral", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Gemini", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldSkipTheCheck_WhenNoProviderIsConfigured()
    {
        var store = new MockEmbeddingFingerprintStore { Stored = new EmbeddingFingerprint("OpenAI", "m", 1536) };

        await EmbeddingDimensionGuard.CheckAsync(new UnconfiguredEmbeddingProvider(), store, TestContext.Current.CancellationToken);

        Assert.Equal(new EmbeddingFingerprint("OpenAI", "m", 1536), store.Stored);
    }

    [Fact]
    public async Task ShouldRefuseTheHostStart_WhenTheConfiguredModelChanged()
    {
        var store = new MockEmbeddingFingerprintStore { Stored = new EmbeddingFingerprint("Ollama", "nomic-embed-text", 768) };
        using var sp = BuildHost(
            store,
            ("Orkeon:Embeddings:Provider", "ollama"),
            ("Orkeon:Embeddings:Model", "mxbai-embed-large"),
            ("Orkeon:Embeddings:Dimension", "1024"),
            ("Orkeon:Embeddings:EnableCache", "false"));

        var guards = sp.GetServices<IHostedService>().ToList();

        var ex = await Assert.ThrowsAsync<EmbeddingModelChangedException>(async () =>
        {
            foreach (var guard in guards)
                await guard.StartAsync(TestContext.Current.CancellationToken);
        });
        Assert.Contains("mxbai-embed-large", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldStartUnguarded_WhenTheFingerprintCannotBeRead()
    {
        var store = new MockEmbeddingFingerprintStore { LoadFailure = new FileAccessDeniedException("no mount", "/workspace/.orkeon/embedding-fingerprint.json") };
        using var loggers = new FakeLoggerFactory();
        using var sp = BuildHost(store, loggers, ("Orkeon:Embeddings:Provider", "ollama"));

        foreach (var service in sp.GetServices<IHostedService>())
            await service.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, store.SaveCallCount);
        var warning = Assert.Single(
            loggers.Entries,
            e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning && e.Message.Contains("check is skipped", StringComparison.Ordinal));
        Assert.Contains("no mount", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldWriteNothing_WhenTheHostNeverConfiguredEmbeddings()
    {
        var fs = new MockFileSystemService(Path.Combine(Path.GetTempPath(), "fwk25-none-" + Guid.NewGuid().ToString("N")));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IFileSystemService>(fs);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddOrkeonInfrastructure();
        using var sp = services.BuildServiceProvider();

        foreach (var service in sp.GetServices<IHostedService>())
            await service.StartAsync(TestContext.Current.CancellationToken);

        Assert.Null(await fs.TryReadAllTextAsync("/workspace/.orkeon/embedding-fingerprint.json", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldPersistAndReloadTheFingerprint_WhenBackedByTheFileSystem()
    {
        var root = Path.Combine(Path.GetTempPath(), "fwk25-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            IFileSystemService fs = new MockFileSystemService(root);
            var first = new FileEmbeddingFingerprintStore(fs, "/workspace/.orkeon/embedding-fingerprint.json");
            Assert.Null(await first.LoadAsync(TestContext.Current.CancellationToken));

            var fp = new EmbeddingFingerprint("Gemini", "gemini-embedding-001", 3072);
            await first.SaveAsync(fp, TestContext.Current.CancellationToken);

            var second = new FileEmbeddingFingerprintStore(fs, "/workspace/.orkeon/embedding-fingerprint.json");
            Assert.Equal(fp, await second.LoadAsync(TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
