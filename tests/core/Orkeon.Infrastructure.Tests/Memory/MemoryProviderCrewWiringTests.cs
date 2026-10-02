using System.Net;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Memory;
using Orkeon.Domain.Common;
using Orkeon.Domain.Memory;
using Orkeon.Infrastructure.Memory;
using Orkeon.Infrastructure.Memory.ChromaDb;
using Orkeon.Infrastructure.Memory.Sqlite;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.Memory;

/// <summary>
/// End-to-end wiring of a crew-declared memory provider (P2-O-02, GAP-08): <see cref="MemoryService"/>
/// resolving the crew's provider TYPE through the real <see cref="MemoryProviderFactory"/>, which
/// connects it from the host's section for that provider and shares one instance per type.
/// </summary>
public sealed class MemoryProviderCrewWiringTests : IDisposable
{
    private readonly MockHttpClientFactory _httpClientFactory = new();
    private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), $"orkeon-crew-memory-{Guid.NewGuid():N}");

    public MemoryProviderCrewWiringTests() => Directory.CreateDirectory(_dataDirectory);

    public void Dispose()
    {
        _httpClientFactory.Dispose();
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dataDirectory))
            Directory.Delete(_dataDirectory, recursive: true);
    }

    private static MemoryItem Insight(string content = "insight") =>
        MemoryItem.Create(content: content, embedding: null, importance: 0.9f, source: "test");

    private static (MemoryService Service, CrewMemoryProviderRegistry Registry) NewService(MemoryProviderFactory factory)
    {
        var registry = new CrewMemoryProviderRegistry();
        return (new MemoryService(factory, NullLogger<MemoryService>.Instance, registry), registry);
    }

    private static CrewId CrewOf(CrewMemoryProviderRegistry registry, string providerType)
    {
        var crewId = CrewId.From(Guid.NewGuid());
        registry.Record(crewId, providerType, crewName: null);
        return crewId;
    }

    [Fact]
    public async Task UnknownProvider_ShouldFallBackToInMemory_WithWarning_NotThrow()
    {
        using var recorder = new CapturingLoggerFactory();
        using var factory = new MemoryProviderFactory(new FakeFileSystemService(), _httpClientFactory, loggerFactory: recorder);
        var (service, registry) = NewService(factory);
        using var _ = service;
        var crewId = CrewOf(registry, "cosmosdb");

        await service.SaveMemoryAsync(crewId, Insight(), TestContext.Current.CancellationToken);
        var results = await service.SearchMemoryAsync(crewId, "insight", 5, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains(
            recorder.Entries,
            e => e.Level == LogLevel.Warning
                 && e.Message.Contains("not recognized", StringComparison.OrdinalIgnoreCase)
                 && e.Message.Contains("cosmosdb", StringComparison.OrdinalIgnoreCase));
        Assert.Single(results);
    }

    [Fact]
    public async Task DeclaredProvider_ShouldResolveThroughRealFactory()
    {
        using var recorder = new CapturingLoggerFactory();
        using var factory = new MemoryProviderFactory(new FakeFileSystemService(), _httpClientFactory, loggerFactory: recorder);
        var (service, registry) = NewService(factory);
        using var _ = service;
        var crewId = CrewOf(registry, "inmemory");

        await service.SaveMemoryAsync(crewId, Insight(), TestContext.Current.CancellationToken);
        var results = await service.SearchMemoryAsync(crewId, "insight", 5, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Single(results);
        Assert.DoesNotContain(recorder.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task SqliteCrew_ShouldStoreInTheDatabaseOfTheSqliteSection_NotAnInMemoryDefault()
    {
        // The host's Orkeon:Sqlite section names a file on a writable mount; the crew only says "SQLite".
        var settings = new MemoryProviderSettings
        {
            Sqlite = new SqliteMemoryOptions { ConnectionString = "Data Source=/data/crew-memory.db" },
        };
        using (var factory = new MemoryProviderFactory(
                   new DiskBackedFileSystemService(_dataDirectory, "/data"), _httpClientFactory, settings))
        {
            var (service, registry) = NewService(factory);
            using var _ = service;
            await service.SaveMemoryAsync(CrewOf(registry, "SQLite"), Insight("durable insight"), TestContext.Current.CancellationToken);
        }

        // A later run — new factory, new crew — reads it back from the same file.
        using var nextRun = new MemoryProviderFactory(
            new DiskBackedFileSystemService(_dataDirectory, "/data"), _httpClientFactory, settings);
        var stored = await nextRun.GetProvider("sqlite").SearchAsync("durable", 5, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(File.Exists(Path.Combine(_dataDirectory, "crew-memory.db")));
        Assert.Contains(stored, item => item.Content == "durable insight");
    }

    [Fact]
    public async Task ChromaDbCrew_ShouldCallTheServerOfTheChromaDbSection_NotLocalhost()
    {
        using var handler = _httpClientFactory.AddClientWithHandler(nameof(ChromaDbMemoryProvider));
        handler.SetResponseFactory(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"id\":\"3fa85f64-5717-4562-b3fc-2c963f66afa6\",\"name\":\"orkeon_memories\"}",
                System.Text.Encoding.UTF8,
                "application/json"),
        });
        using var factory = new MemoryProviderFactory(
            new FakeFileSystemService(),
            _httpClientFactory,
            new MemoryProviderSettings { ChromaDb = new ChromaDbOptions { BaseUrl = new Uri("http://chroma.team:8800") } });
        var (service, registry) = NewService(factory);
        using var _ = service;

        await service.SaveMemoryAsync(CrewOf(registry, "ChromaDb"), Insight(), TestContext.Current.CancellationToken);

        Assert.NotEmpty(handler.AllRequests);
        Assert.All(handler.AllRequests, r => Assert.Equal("http://chroma.team:8800/", r.RequestUri!.GetLeftPart(UriPartial.Authority) + "/"));
    }

    [Fact]
    public async Task TwoCrewsOfTheSameType_ShouldShareOneProviderInstance()
    {
        using var inner = new MemoryProviderFactory(new FakeFileSystemService(), _httpClientFactory);
        var recording = new RecordingMemoryProviderFactory(inner);
        var registry = new CrewMemoryProviderRegistry();
        using var shared = new MemoryService(recording, NullLogger<MemoryService>.Instance, registry);

        var first = CrewOf(registry, "sqlite");
        var second = CrewOf(registry, "SQLite");
        await shared.SaveMemoryAsync(first, Insight("from the first crew"), TestContext.Current.CancellationToken);
        await shared.SaveMemoryAsync(second, Insight("from the second crew"), TestContext.Current.CancellationToken);

        Assert.Equal(2, recording.Handed.Count);
        Assert.Same(recording.Handed[0], recording.Handed[1]);
        Assert.Equal(2, await ((Orkeon.Infrastructure.Memory.Base.MemoryProviderBase)recording.Handed[0]).CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReleasingACrew_ShouldNotDisposeTheSharedProvider_AndClearingRemovesOnlyItsOwnEntries()
    {
        using var factory = new MemoryProviderFactory(new FakeFileSystemService(), _httpClientFactory);
        var (service, registry) = NewService(factory);
        using var _ = service;
        var first = CrewOf(registry, "sqlite");
        var second = CrewOf(registry, "sqlite");
        await service.SaveMemoryAsync(first, Insight("first"), TestContext.Current.CancellationToken);
        await service.SaveMemoryAsync(second, Insight("second"), TestContext.Current.CancellationToken);

        await service.ClearMemoryAsync(first, cancellationToken: TestContext.Current.CancellationToken);
        service.ReleaseMemorySystem(first);

        var remaining = await factory.GetProvider("sqlite").SearchAsync("second", 5, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Single(remaining);
        Assert.Equal(1, await ((Orkeon.Infrastructure.Memory.Base.MemoryProviderBase)factory.GetProvider("sqlite")).CountAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>Wraps the real factory and records every instance it hands out.</summary>
    private sealed class RecordingMemoryProviderFactory(MemoryProviderFactory inner) : Orkeon.Application.Interfaces.Ports.IMemoryProviderFactory
    {
        public MemoryProviderFactory Inner { get; } = inner;

        public List<IMemoryProvider> Handed { get; } = [];

        public IReadOnlyList<string> SupportedTypes => Inner.SupportedTypes;

        public IMemoryProvider GetProvider(string providerType)
        {
            var provider = Inner.GetProvider(providerType);
            Handed.Add(provider);
            return provider;
        }
    }

    private sealed class CapturingLoggerFactory : ILoggerFactory
    {
        private readonly List<LogRecord> _entries = [];
        public IReadOnlyList<LogRecord> Entries => _entries;

        public void AddProvider(ILoggerProvider provider) { }
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(_entries);
        public void Dispose() { }

        private sealed class CapturingLogger(List<LogRecord> entries) : ILogger
        {
            public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
                => entries.Add(new LogRecord(logLevel, formatter(state, exception)));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }

    private sealed record LogRecord(LogLevel Level, string Message);
}
