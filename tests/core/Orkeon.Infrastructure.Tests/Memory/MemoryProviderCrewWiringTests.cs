using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Memory;
using Orkeon.Domain.Common;
using Orkeon.Domain.Memory;
using Orkeon.Infrastructure.Memory;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.Memory;

/// <summary>
/// End-to-end wiring of a crew-declared memory provider (P2-O-02): <see cref="MemoryService"/>
/// resolving the crew's provider string through the real <see cref="MemoryProviderFactory"/>. Proves
/// the unknown/unavailable-provider fallback-with-warning behavior is preserved on this path.
/// </summary>
public sealed class MemoryProviderCrewWiringTests
{
    [Fact]
    public async System.Threading.Tasks.Task UnknownProvider_ShouldFallBackToInMemory_WithWarning_NotThrow()
    {
        // Arrange — real factory; the crew declared an unrecognized provider.
        using var recorder = new CapturingLoggerFactory();
        var factory = new MemoryProviderFactory(new FakeFileSystemService());
        var registry = new CrewMemoryProviderRegistry();
        var crewId = CrewId.From(Guid.NewGuid());
        registry.SetProvider(crewId, "cosmosdb");
        using var service = new MemoryService(factory, NullLogger<MemoryService>.Instance, registry, recorder);

        // Act — must not throw; the factory degrades to in-memory with a warning.
        var item = MemoryItem.Create(content: "insight", embedding: null, importance: 0.9f, source: "test");
        await service.SaveMemoryAsync(crewId, item, TestContext.Current.CancellationToken);
        var results = await service.SearchMemoryAsync(crewId, "insight", 5, cancellationToken: TestContext.Current.CancellationToken);

        // Assert — the factory's fallback warning surfaced (behavior preserved), and memory still works.
        Assert.Contains(
            recorder.Entries,
            e => e.Level == LogLevel.Warning
                 && e.Message.Contains("not recognized", StringComparison.OrdinalIgnoreCase)
                 && e.Message.Contains("cosmosdb", StringComparison.OrdinalIgnoreCase));
        Assert.Single(results);
    }

    [Fact]
    public async System.Threading.Tasks.Task DeclaredProvider_ShouldResolveThroughRealFactory()
    {
        // Arrange — a recognized provider ("inmemory") resolves without warning.
        using var recorder = new CapturingLoggerFactory();
        var factory = new MemoryProviderFactory(new FakeFileSystemService());
        var registry = new CrewMemoryProviderRegistry();
        var crewId = CrewId.From(Guid.NewGuid());
        registry.SetProvider(crewId, "inmemory");
        using var service = new MemoryService(factory, NullLogger<MemoryService>.Instance, registry, recorder);

        var item = MemoryItem.Create(content: "insight", embedding: null, importance: 0.9f, source: "test");
        await service.SaveMemoryAsync(crewId, item, TestContext.Current.CancellationToken);
        var results = await service.SearchMemoryAsync(crewId, "insight", 5, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Single(results);
        Assert.DoesNotContain(recorder.Entries, e => e.Level == LogLevel.Warning);
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
