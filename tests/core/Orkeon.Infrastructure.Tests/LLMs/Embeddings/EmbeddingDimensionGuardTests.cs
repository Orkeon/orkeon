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

    private static async Task StartWithMountsAsync(FakeLoggerFactory loggers, params (string Dir, string Virtual)[] mounts)
    {
        using var registry = new FileSystemRegistry(mounts.Select(m => new FileSystemMount(
            m.Dir, m.Virtual, FileAccessRights.ReadWrite | FileAccessRights.Create | FileAccessRights.Delete)));
        var fs = new Orkeon.Infrastructure.FileSystem.FileSystemService(
            registry, new Orkeon.Tests.Shared.Doubles.StubPathValidator().AllowAll(), Microsoft.Extensions.Logging.LoggerFactoryExtensions.CreateLogger<Orkeon.Infrastructure.FileSystem.FileSystemService>(loggers));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(loggers);
        services.AddSingleton<IFileSystemService>(fs);
        services.AddSingleton<Orkeon.Application.Interfaces.Ports.IEmbeddingProvider>(Provider("Mistral", "mistral-embed", 1024));
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddOrkeonInfrastructure();
        await using var sp = services.BuildServiceProvider();
        foreach (var service in sp.GetServices<IHostedService>())
            await service.StartAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    [Trait("BR", "CH-22")]
    public async Task ShouldStillGuard_WhenAMountCoversTheFingerprintPath()
    {
        var root = Path.Combine(Path.GetTempPath(), "ch22-mounted-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var loggers = new FakeLoggerFactory();

            await StartWithMountsAsync(loggers, (root, "/workspace"));

            Assert.True(File.Exists(Path.Combine(root, ".orkeon", "embedding-fingerprint.json")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    [Trait("BR", "CH-22")]
    public async Task ShouldSkipAtDebug_WhenNoMountCoversTheFingerprintPath()
    {
        var docs = Path.Combine(Path.GetTempPath(), "ch22-docs-" + Guid.NewGuid().ToString("N"));
        var output = Path.Combine(Path.GetTempPath(), "ch22-out-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(docs);
        Directory.CreateDirectory(output);
        try
        {
            using var loggers = new FakeLoggerFactory();

            await StartWithMountsAsync(loggers, (docs, "/documents"), (output, "/output"));

            Assert.DoesNotContain(loggers.Entries, e => e.Level >= Microsoft.Extensions.Logging.LogLevel.Warning);
            var debug = Assert.Single(
                loggers.Entries,
                e => e.Level == Microsoft.Extensions.Logging.LogLevel.Debug
                    && e.Message.Contains("/workspace/.orkeon/embedding-fingerprint.json", StringComparison.Ordinal));
            Assert.Contains("EmbeddingDimensionGuardRunner", debug.Category, StringComparison.Ordinal);
            Assert.Empty(Directory.GetFileSystemEntries(docs));
            Assert.Empty(Directory.GetFileSystemEntries(output));
        }
        finally
        {
            Directory.Delete(docs, true);
            Directory.Delete(output, true);
        }
    }

    [Fact]
    [Trait("BR", "CH-38")]
    public async Task ShouldStillRefuse_WhenAReadOnlyWorkspaceHoldsAFingerprintOfAnotherModel()
    {
        var root = Path.Combine(Path.GetTempPath(), "ch38-ro-" + Guid.NewGuid().ToString("N"));
        var dir = Path.Combine(root, ".orkeon");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "embedding-fingerprint.json");
        var json = System.Text.Json.JsonSerializer.Serialize(new EmbeddingFingerprint("OpenAI", "text-embedding-3-small", 1536));
        await File.WriteAllTextAsync(file, json, TestContext.Current.CancellationToken);
        try
        {
            using var loggers = new FakeLoggerFactory();
            using var registry = new FileSystemRegistry([new FileSystemMount(root, "/workspace", FileAccessRights.Read)]);
            var fs = (IFileSystemService)(new Orkeon.Infrastructure.FileSystem.FileSystemService(
                registry, new Orkeon.Tests.Shared.Doubles.StubPathValidator().AllowAll(),
                Microsoft.Extensions.Logging.LoggerFactoryExtensions.CreateLogger<Orkeon.Infrastructure.FileSystem.FileSystemService>(loggers)));
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(loggers);
            services.AddSingleton<IFileSystemService>(fs);
            services.AddSingleton<Orkeon.Application.Interfaces.Ports.IEmbeddingProvider>(Provider("Mistral", "mistral-embed", 1024));
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
            services.AddOrkeonInfrastructure();
            await using var sp = services.BuildServiceProvider();

            var ex = await Assert.ThrowsAsync<EmbeddingModelChangedException>(async () =>
            {
                foreach (var service in sp.GetServices<IHostedService>())
                    await service.StartAsync(TestContext.Current.CancellationToken);
            });

            Assert.Contains("text-embedding-3-small", ex.Message, StringComparison.Ordinal);
            Assert.Equal(json, await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken));
            Assert.Single(Directory.GetFileSystemEntries(dir));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    [Trait("BR", "CH-38")]
    public async Task ShouldLogTheProviderFailure_WhenTheDimensionsCannotBeRead()
    {
        using var loggers = new FakeLoggerFactory();
        var store = new MockEmbeddingFingerprintStore();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(loggers);
        services.AddSingleton<IEmbeddingFingerprintStore>(store);
        services.AddSingleton<Orkeon.Application.Interfaces.Ports.IEmbeddingProvider>(new StubThrowingDimensionsEmbeddingProvider());
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddOrkeonInfrastructure();
        await using var sp = services.BuildServiceProvider();

        foreach (var service in sp.GetServices<IHostedService>())
            await service.StartAsync(TestContext.Current.CancellationToken);

        var entry = Assert.Single(loggers.Entries, e => e.Message.Contains(StubThrowingDimensionsEmbeddingProvider.FailureMessage, StringComparison.Ordinal));
        Assert.DoesNotContain("writable path", entry.Message, StringComparison.Ordinal);
        Assert.Equal(0, store.SaveCallCount);
    }

    [Fact]
    [Trait("BR", "CH-38")]
    public async Task ShouldWriteNothingAndLogNoWarning_WhenTheCoveringMountLacksCreate()
    {
        await AssertNothingWrittenAsync(FileAccessRights.Read | FileAccessRights.Write, null);
    }

    [Fact]
    [Trait("BR", "CH-38")]
    public async Task ShouldWriteNothingAndLogNoWarning_WhenAnOverrideMakesTheFolderReadOnly()
    {
        await AssertNothingWrittenAsync(
            FileAccessRights.ReadWrite | FileAccessRights.Create | FileAccessRights.Delete,
            [new SubPathOverride(".orkeon", FileAccessRights.Read)]);
    }

    private static async Task AssertNothingWrittenAsync(FileAccessRights rights, IReadOnlyList<SubPathOverride>? overrides)
    {
        var root = Path.Combine(Path.GetTempPath(), "ch38-nw-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var loggers = new FakeLoggerFactory();
            using var registry = new FileSystemRegistry([new FileSystemMount(root, "/workspace", rights, overrides)]);
            var fs = new Orkeon.Infrastructure.FileSystem.FileSystemService(
                registry, new Orkeon.Tests.Shared.Doubles.StubPathValidator().AllowAll(),
                Microsoft.Extensions.Logging.LoggerFactoryExtensions.CreateLogger<Orkeon.Infrastructure.FileSystem.FileSystemService>(loggers));
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(loggers);
            services.AddSingleton<IFileSystemService>(fs);
            services.AddSingleton<Orkeon.Application.Interfaces.Ports.IEmbeddingProvider>(Provider("Mistral", "mistral-embed", 1024));
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
            services.AddOrkeonInfrastructure();
            await using var sp = services.BuildServiceProvider();

            foreach (var service in sp.GetServices<IHostedService>())
                await service.StartAsync(TestContext.Current.CancellationToken);

            Assert.DoesNotContain(loggers.Entries, e => e.Level >= Microsoft.Extensions.Logging.LogLevel.Warning);
            Assert.Empty(Directory.GetFileSystemEntries(root));
        }
        finally
        {
            Directory.Delete(root, true);
        }
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
    [Trait("BR", "CH-14")]
    public async Task ShouldNameTheFingerprintSetting_WhenTheGuardIsSkipped()
    {
        var store = new MockEmbeddingFingerprintStore { LoadFailure = new FileAccessDeniedException("no mount", "/workspace/.orkeon/embedding-fingerprint.json") };
        using var loggers = new FakeLoggerFactory();
        using var sp = BuildHost(store, loggers, ("Orkeon:Embeddings:Provider", "ollama"));

        foreach (var service in sp.GetServices<IHostedService>())
            await service.StartAsync(TestContext.Current.CancellationToken);

        var warning = Assert.Single(
            loggers.Entries,
            e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning && e.Message.Contains("check is skipped", StringComparison.Ordinal));
        Assert.Contains("Orkeon:Embeddings:FingerprintPath", warning.Message, StringComparison.Ordinal);
        Assert.Contains("no mount", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-04")]
    public async Task ShouldLogDebugNotWarning_WhenTheFirstFingerprintWriteIsDeniedByAReadOnlyMount()
    {
        var store = new MockEmbeddingFingerprintStore { SaveFailure = new FileAccessDeniedException("read-only mount", "/workspace/.orkeon/embedding-fingerprint.json") };
        using var loggers = new FakeLoggerFactory();
        using var sp = BuildHost(store, loggers, ("Orkeon:Embeddings:Provider", "ollama"));

        foreach (var service in sp.GetServices<IHostedService>())
            await service.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, store.SaveCallCount);
        Assert.DoesNotContain(loggers.Entries, e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning);
        Assert.Contains(loggers.Entries, e => e.Level == Microsoft.Extensions.Logging.LogLevel.Debug && e.Message.Contains("read-only mount", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("BR", "CH-04")]
    public async Task ShouldStillWarn_WhenTheFirstFingerprintWriteFailsForAnotherReason()
    {
        var store = new MockEmbeddingFingerprintStore { SaveFailure = new IOException("disk full") };
        using var loggers = new FakeLoggerFactory();
        using var sp = BuildHost(store, loggers, ("Orkeon:Embeddings:Provider", "ollama"));

        foreach (var service in sp.GetServices<IHostedService>())
            await service.StartAsync(TestContext.Current.CancellationToken);

        var warning = Assert.Single(loggers.Entries, e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning);
        Assert.Contains("disk full", warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(loggers.Entries, e => e.Level == Microsoft.Extensions.Logging.LogLevel.Debug && e.Message.Contains("disk full", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ShouldWriteNothing_WhenTheHostHasNoEmbeddingProvider()
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
    public async Task ShouldRefuseTheModelASectionSwitchesTo_WhenTheHostFirstEmbeddedWithoutASection()
    {
        // FWK review LLM-5: a host with no Orkeon:Embeddings section recorded nothing, so the section
        // written later (the switch to Mistral or Gemini) recorded the new model as the first one.
        var root = Path.Combine(Path.GetTempPath(), "fwk25-switch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var fs = new MockFileSystemService(root);
            await StartAsync(fs, Provider("Default", "nomic-embed-text", 768));
            Assert.NotNull(await fs.TryReadAllTextAsync("/workspace/.orkeon/embedding-fingerprint.json", TestContext.Current.CancellationToken));

            var ex = await Assert.ThrowsAsync<EmbeddingModelChangedException>(
                () => StartAsync(fs, Provider("Mistral", "mistral-embed", 1024), ("Orkeon:Embeddings:Provider", "mistral")));

            Assert.Contains("nomic-embed-text", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, true);
        }

        static async Task StartAsync(IFileSystemService fs, MockEmbeddingProvider provider, params (string Key, string Value)[] settings)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IFileSystemService>(fs);
            services.AddSingleton<Orkeon.Application.Interfaces.Ports.IEmbeddingProvider>(provider);
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
                .AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => (string?)s.Value))
                .Build());
            services.AddOrkeonInfrastructure();
            await using var sp = services.BuildServiceProvider();
            foreach (var service in sp.GetServices<IHostedService>())
                await service.StartAsync(TestContext.Current.CancellationToken);
        }
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
