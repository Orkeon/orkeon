using System.Collections.Immutable;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Memory;
using Orkeon.Domain.Common;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Memory;
using Orkeon.Infrastructure.Memory;
using Orkeon.Infrastructure.Memory.Sqlite;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Stores;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.Memory;

/// <summary>
/// GAP-20 — a crew's long-term memory lives in the provider of its type, which every crew of that
/// type and the RAG store of that type share (one instance per type, GAP-08). The crew's NAME is its
/// scope: a crew reads what it stored, run after run, and never what a crew of another name stored,
/// nor a RAG chunk. Each case runs on In-Memory and on SQLite, the two providers that run offline.
/// </summary>
public sealed class CrewMemoryScopeTests : IDisposable
{
    private readonly MockHttpClientFactory _httpClientFactory = new();
    private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), $"orkeon-crew-scope-{Guid.NewGuid():N}");

    public CrewMemoryScopeTests() => Directory.CreateDirectory(_dataDirectory);

    public void Dispose()
    {
        _httpClientFactory.Dispose();
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dataDirectory))
            Directory.Delete(_dataDirectory, recursive: true);
    }

    public static TheoryData<string> Providers => new() { "inmemory", "sqlite" };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static MemoryItem Insight(string content) =>
        MemoryItem.Create(content: content, embedding: null, importance: 0.9f, source: "task_execution");

    private MemoryProviderFactory NewFactory(MemoryProviderSettings? settings = null, IFileSystemService? fileSystem = null) =>
        new(fileSystem ?? new FakeFileSystemService(), _httpClientFactory, settings);

    private static (MemoryService Service, CrewMemoryProviderRegistry Registry) NewService(MemoryProviderFactory factory)
    {
        var registry = new CrewMemoryProviderRegistry();
        return (new MemoryService(factory, NullLogger<MemoryService>.Instance, registry), registry);
    }

    /// <summary>What the orchestrator records at kickoff: a fresh crew id, the type, the name.</summary>
    private static CrewId Kickoff(CrewMemoryProviderRegistry registry, string providerType, string? crewName)
    {
        var crewId = CrewId.From(Guid.NewGuid());
        registry.Record(crewId, providerType, crewName, memoryEnabled: true);
        return crewId;
    }

    private static async Task<List<string>> RecallAsync(MemoryService service, CrewId crewId, string query) =>
        [.. (await service.SearchMemoryAsync(crewId, query, 10, cancellationToken: Ct)).Select(m => m.Content).Order(StringComparer.Ordinal)];

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task A_crew_never_reads_what_a_crew_of_another_name_stored(string providerType)
    {
        using var factory = NewFactory();
        var (service, registry) = NewService(factory);
        using var _ = service;
        var legal = Kickoff(registry, providerType, "legal-watch");
        var customers = Kickoff(registry, providerType, "customer-follow-up");

        await service.SaveMemoryAsync(legal, Insight("contract clause 4 changed"), Ct);
        await service.SaveMemoryAsync(customers, Insight("contract renewal reminder sent"), Ct);

        Assert.Equal(["contract clause 4 changed"], await RecallAsync(service, legal, "contract"));
        Assert.Equal(["contract renewal reminder sent"], await RecallAsync(service, customers, "contract"));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task A_name_that_contains_another_crew_name_is_still_another_crew(string providerType)
    {
        // Equality on the name, not a substring of the stored metadata.
        using var factory = NewFactory();
        var (service, registry) = NewService(factory);
        using var _ = service;
        var legal = Kickoff(registry, providerType, "legal");
        var legalOps = Kickoff(registry, providerType, "legal ops");

        await service.SaveMemoryAsync(legalOps, Insight("contract archive moved"), Ct);

        Assert.Empty(await RecallAsync(service, legal, "contract"));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task A_later_run_of_the_same_crew_reads_what_the_earlier_run_stored(string providerType)
    {
        // The daemon's shape: one factory and one memory service for the process, a fresh crew
        // (a new id) per run, released at its end (CrewRunner).
        using var factory = NewFactory();
        var (service, registry) = NewService(factory);
        using var _ = service;

        var firstRun = Kickoff(registry, providerType, "legal-watch");
        await service.SaveMemoryAsync(firstRun, Insight("contract clause 4 changed"), Ct);
        service.ReleaseMemorySystem(firstRun);
        registry.Remove(firstRun);

        var secondRun = Kickoff(registry, providerType, "legal-watch");

        Assert.Equal(["contract clause 4 changed"], await RecallAsync(service, secondRun, "contract"));
    }

    [Fact]
    public async Task A_later_process_reads_the_crew_memory_back_from_the_sqlite_file_and_only_that_crew_does()
    {
        var settings = new MemoryProviderSettings
        {
            Sqlite = new SqliteMemoryOptions { ConnectionString = "Data Source=/data/crew-memory.db" },
        };
        var disk = new DiskBackedFileSystemService(_dataDirectory, "/data");

        using (var firstProcess = NewFactory(settings, disk))
        {
            var (service, registry) = NewService(firstProcess);
            using var _ = service;
            await service.SaveMemoryAsync(Kickoff(registry, "sqlite", "legal-watch"), Insight("contract clause 4 changed"), Ct);
        }

        using var nextProcess = NewFactory(settings, new DiskBackedFileSystemService(_dataDirectory, "/data"));
        var (next, nextRegistry) = NewService(nextProcess);
        using var __ = next;

        Assert.Equal(["contract clause 4 changed"], await RecallAsync(next, Kickoff(nextRegistry, "sqlite", "legal-watch"), "contract"));
        Assert.Empty(await RecallAsync(next, Kickoff(nextRegistry, "sqlite", "customer-follow-up"), "contract"));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task An_unnamed_crew_keeps_its_memory_to_its_own_run(string providerType)
    {
        using var factory = NewFactory();
        var (service, registry) = NewService(factory);
        using var _ = service;
        var first = Kickoff(registry, providerType, crewName: null);
        var second = Kickoff(registry, providerType, crewName: null);

        await service.SaveMemoryAsync(first, Insight("contract clause 4 changed"), Ct);

        Assert.Equal(["contract clause 4 changed"], await RecallAsync(service, first, "contract"));
        Assert.Empty(await RecallAsync(service, second, "contract"));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task A_rag_chunk_in_the_same_store_never_reaches_the_crew_memory(string providerType)
    {
        // Orkeon:Rag:Provider unset: the RAG store is the ambient provider — the factory's
        // instance of the type the crew names. Its chunk, its manifest and its registry share the
        // crew memory's key space; the chunk's text and the manifest's (its keys name the
        // collection) both contain the query.
        using var factory = NewFactory();
        var store = new MemoryProviderDocumentStore(factory.GetProvider(providerType));
        await store.UpsertAsync("contracts", [Embedded("The contract may be terminated with 30 days notice.")], Ct);

        var (service, registry) = NewService(factory);
        using var _ = service;
        var legal = Kickoff(registry, providerType, "legal-watch");
        await service.SaveMemoryAsync(legal, Insight("contract clause 4 changed"), Ct);

        Assert.Equal(["contract clause 4 changed"], await RecallAsync(service, legal, "contract"));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task What_a_crew_stores_says_it_is_crew_memory_and_whose(string providerType)
    {
        using var factory = NewFactory();
        var (service, registry) = NewService(factory);
        using var _ = service;

        await service.SaveMemoryAsync(Kickoff(registry, providerType, "legal-watch"), Insight("contract clause 4 changed"), Ct);

        var stored = Assert.Single(await factory.GetProvider(providerType).SearchAsync("contract", 10, cancellationToken: Ct));
        var properties = stored.Metadata.CustomProperties;
        Assert.NotNull(properties);
        Assert.Equal("crew-memory", properties.GetValueOrDefault("kind"));
        Assert.Equal("legal-watch", properties.GetValueOrDefault("crew"));
    }

    private static EmbeddedChunk Embedded(string content) => new()
    {
        Chunk = new Chunk
        {
            Id = "contracts-0",
            DocumentId = "contracts.md",
            SourceId = "/kb/contracts.md",
            Content = content,
            Index = 0,
            StartOffset = 0,
            EndOffset = content.Length,
        },
        Embedding = ImmutableArray.Create(0.1f, 0.2f, 0.3f),
    };
}
