using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.FileSystem;
using Orkeon.Hosting.Tests.Doubles;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// GAP-08: the memory provider a YAML crew names by TYPE (<c>memoryProvider:</c>), and the RAG
/// store named by <c>Orkeon:Rag:Provider</c>, are connected from the host's section for that
/// provider. Before, the crew's SQLite was an in-process <c>:memory:</c> database lost at the end
/// of the run, and the RAG store had its own connection keys. SQLite stands in for every provider
/// here because it runs offline; the factory path is the same for Redis, ChromaDB and Pinecone.
/// GAP-30: a crew with <c>memory: true</c> stores each result with its vector and recalls it in its
/// next run's prompt; a crew without stores nothing. The embedder is bag-of-words, its MinScore set
/// in the settings. Joins <see cref="ConsoleSerialCollection"/> because it redirects the
/// process-global console.
/// </summary>
[Collection(ConsoleSerialCollection.Name)]
public sealed class RunnerHostMemoryProviderTests : IDisposable
{
    private sealed class TestOptions : RunnerOptionsBase { }

    private const string SqliteCrew = """
        name: "memory-crew"
        goal: "Remember what was found"
        process: "sequential"
        memory: true
        memoryProvider: "SQLite"
        agents:
          analyst:
            role: "Analyst"
            goal: "Find one fact"
            backstory: "A minimal test agent."
            maxIter: 1
        tasks:
          find:
            description: "State one fact about the sea."
            expectedOutput: "One fact."
            agent: "analyst"
        """;

    /// <summary>The same crew, without memory: nor memoryProvider:.</summary>
    private const string ForgetfulCrew = """
        name: "memory-crew"
        goal: "Remember what was found"
        process: "sequential"
        agents:
          analyst:
            role: "Analyst"
            goal: "Find one fact"
            backstory: "A minimal test agent."
            maxIter: 1
        tasks:
          find:
            description: "State one fact about the sea."
            expectedOutput: "One fact."
            agent: "analyst"
        """;

    private const string MemorySettings = """
        { "RaggableTree": { "Enabled": false },
          "Memory": { "Provider": "sqlite" },
          "Orkeon": {
            "Sqlite": { "ConnectionString": "Data Source=/data/crew-memory.db" },
            "CrewMemory": { "MinScore": 0.3 } } }
        """;

    private const string KnowledgeCrew = """
        name: "support-crew"
        goal: "Answer from the FAQ"
        process: "sequential"
        rag:
          collections:
            produits:
              sources: ["/kb/faq.md"]
        agents:
          support:
            role: "Support agent"
            goal: "Answer questions from the knowledge base"
            backstory: "A careful support agent."
            maxIter: 1
            knowledge: [produits]
        tasks:
          answer:
            description: "How many days do customers have to request a refund?"
            expectedOutput: "A grounded answer."
            agent: "support"
        """;

    private readonly string _root;
    private readonly string _data;
    private readonly string _kb;

    public RunnerHostMemoryProviderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "orkeon-hosting-memory-" + Guid.NewGuid().ToString("N"));
        _data = Path.Combine(_root, "data");
        _kb = Path.Combine(_root, "kb");
        Directory.CreateDirectory(_data);
        Directory.CreateDirectory(_kb);
        File.WriteAllText(Path.Combine(_kb, "faq.md"), "Refund policy: customers may request a full refund within 30 days of purchase.");
        File.WriteAllText(Path.Combine(_root, "memory.yaml"), SqliteCrew);
        File.WriteAllText(Path.Combine(_root, "forgetful.yaml"), ForgetfulCrew);
        File.WriteAllText(Path.Combine(_root, "knowledge.yaml"), KnowledgeCrew);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* best-effort temp cleanup */ }
        catch (UnauthorizedAccessException) { /* best-effort temp cleanup */ }
    }

    [Fact]
    public async Task A_crew_naming_SQLite_stores_its_results_in_the_database_of_the_Sqlite_section()
    {
        WriteSettings("""
            { "RaggableTree": { "Enabled": false },
              "Orkeon": { "Sqlite": { "ConnectionString": "Data Source=/data/crew-memory.db" } } }
            """);
        using var chat = new CapturingChatClient();

        var (exit, stderr) = await RunAsync("memory.yaml", chat);

        Assert.True(exit == 0, stderr);
        Assert.True(CountRows(Path.Combine(_data, "crew-memory.db")) >= 1, stderr);
    }

    [Fact]
    public async Task A_crew_stores_its_results_as_crew_memory_under_the_name_of_its_yaml()
    {
        // GAP-20: the YAML name: reaches the store — every row says it is crew memory, and whose —
        // so the next run of this crew finds it, and another crew of the same type does not.
        WriteSettings("""
            { "RaggableTree": { "Enabled": false },
              "Orkeon": { "Sqlite": { "ConnectionString": "Data Source=/data/crew-memory.db" } } }
            """);
        using var chat = new CapturingChatClient();

        var (exit, stderr) = await RunAsync("memory.yaml", chat);

        Assert.True(exit == 0, stderr);
        var properties = ReadCustomProperties(Path.Combine(_data, "crew-memory.db"));
        Assert.NotEmpty(properties);
        Assert.All(properties, json =>
        {
            Assert.Contains("\"kind\":\"crew-memory\"", json, StringComparison.Ordinal);
            Assert.Contains("\"crew\":\"memory-crew\"", json, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Every_memory_of_a_crew_with_memory_carries_its_vector_and_says_whose_it_is()
    {
        WriteSettings(MemorySettings);
        using var chat = new CapturingChatClient();

        var (exit, stderr) = await RunAsync("memory.yaml", chat);

        Assert.True(exit == 0, stderr);
        var rows = ReadRows(Path.Combine(_data, "crew-memory.db"));
        Assert.NotEmpty(rows);
        Assert.All(rows, row =>
        {
            Assert.True(row.HasEmbedding, "a memory without its vector");
            Assert.Contains("\"kind\":\"crew-memory\"", row.CustomProperties, StringComparison.Ordinal);
            Assert.Contains("\"crew\":\"memory-crew\"", row.CustomProperties, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task A_second_run_sends_the_model_what_the_first_run_produced_under_the_memory_header()
    {
        WriteSettings(MemorySettings);
        using var first = new CapturingChatClient { Answer = "The sea covers seventy one percent of the planet." };
        using var second = new CapturingChatClient { Answer = "Salt water is denser than fresh water." };

        var (firstExit, firstErr) = await RunAsync("memory.yaml", first);
        var (secondExit, secondErr) = await RunAsync("memory.yaml", second);

        Assert.True(firstExit == 0, firstErr);
        Assert.True(secondExit == 0, secondErr);
        Assert.DoesNotContain(first.Prompts, p => p.Contains(MemoriesHeader, StringComparison.Ordinal));
        Assert.Contains(second.Prompts, p =>
            p.Contains(MemoriesHeader, StringComparison.Ordinal)
            && p.Contains("The sea covers seventy one percent of the planet.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_same_crew_without_memory_stores_nothing_and_recalls_nothing()
    {
        WriteSettings(MemorySettings);
        using var first = new CapturingChatClient { Answer = "The sea covers seventy one percent of the planet." };
        using var second = new CapturingChatClient();

        var (firstExit, firstErr) = await RunAsync("forgetful.yaml", first);
        var (secondExit, secondErr) = await RunAsync("forgetful.yaml", second);

        Assert.True(firstExit == 0, firstErr);
        Assert.True(secondExit == 0, secondErr);
        Assert.Empty(ReadRows(Path.Combine(_data, "crew-memory.db"), mayBeAbsent: true));
        Assert.DoesNotContain(second.Prompts, p => p.Contains(MemoriesHeader, StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_rag_store_named_sqlite_writes_to_the_database_of_the_Sqlite_section()
    {
        WriteSettings("""
            { "RaggableTree": { "Enabled": false },
              "Orkeon": {
                "Rag": { "Provider": "sqlite" },
                "Sqlite": { "ConnectionString": "Data Source=/data/rag.db" } } }
            """);
        using var chat = new CapturingChatClient();

        var (exit, stderr) = await RunAsync("knowledge.yaml", chat);

        Assert.True(exit == 0, stderr);
        Assert.True(CountRows(Path.Combine(_data, "rag.db")) >= 1, stderr);
        Assert.Contains(chat.Prompts, p => p.Contains("full refund within 30 days", StringComparison.Ordinal));
    }

    [Fact]
    public void The_application_provider_is_the_factory_instance_of_its_type_whatever_the_alias()
    {
        WriteSettings("""
            { "RaggableTree": { "Enabled": false }, "Memory": { "Provider": "sqlite" } }
            """);
        using var host = RunnerHost.Build(
            Path.Combine(_root, "appsettings.json"),
            new RunnerMountPlan { CliMounts = [$"{FileSystemMount.Quote(_kb)}:/kb:ro"] });

        var factory = host.Services.GetRequiredService<IMemoryProviderFactory>();
        var ambient = host.Services.GetRequiredService<Orkeon.Domain.Memory.IMemoryProvider>();

        Assert.Same(factory.GetProvider("SQLite"), ambient);
        Assert.Same(factory.GetProvider("sqlite"), ambient);
    }

    private const string MemoriesHeader = Orkeon.Application.Constants.Orchestration.PromptDefaults.MemoriesHeader;

    private void WriteSettings(string json) =>
        File.WriteAllText(Path.Combine(_root, "appsettings.json"), json);

    private sealed record StoredRow(bool HasEmbedding, string CustomProperties);

    private static List<StoredRow> ReadRows(string databasePath, bool mayBeAbsent = false)
    {
        if (mayBeAbsent && !File.Exists(databasePath))
            return [];
        Assert.True(File.Exists(databasePath), $"{databasePath} was not created.");
        using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var exists = connection.CreateCommand();
        exists.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'memory_items'";
        if ((long)exists.ExecuteScalar()! == 0)
            return [];
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT embedding, custom_properties_json FROM memory_items";
        using var reader = command.ExecuteReader();
        var rows = new List<StoredRow>();
        while (reader.Read())
            rows.Add(new StoredRow(!reader.IsDBNull(0), reader.IsDBNull(1) ? string.Empty : reader.GetString(1)));
        return rows;
    }

    private static List<string> ReadCustomProperties(string databasePath)
    {
        Assert.True(File.Exists(databasePath), $"{databasePath} was not created.");
        using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT custom_properties_json FROM memory_items";
        using var reader = command.ExecuteReader();
        var rows = new List<string>();
        while (reader.Read())
            rows.Add(reader.IsDBNull(0) ? string.Empty : reader.GetString(0));
        return rows;
    }

    private static long CountRows(string databasePath)
    {
        Assert.True(File.Exists(databasePath), $"{databasePath} was not created.");
        using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM memory_items";
        return (long)command.ExecuteScalar()!;
    }

    private async Task<(int Exit, string Stderr)> RunAsync(string crewFile, CapturingChatClient chat)
    {
        var options = new TestOptions
        {
            ConfigPath = Path.Combine(_root, crewFile),
            Mounts =
            [
                $"{FileSystemMount.Quote(_kb)}:/kb:ro",
                $"{FileSystemMount.Quote(_data)}:/data:rw",
            ],
            SettingsPath = Path.Combine(_root, "appsettings.json"),
            AllowExternalMounts = true,
        };

        var origOut = Console.Out;
        var origErr = Console.Error;
        using var stdout = new StringWriter(new StringBuilder());
        using var stderr = new StringWriter(new StringBuilder());
        Console.SetOut(stdout);
        Console.SetError(stderr);
        try
        {
            var exit = await RunnerExecution.RunOneShotAsync(
                options,
                "Orkeon.Hosting.Tests",
                (_, services) =>
                {
                    services.AddSingleton<IEmbeddingProvider>(new LexicalEmbeddingProvider());
                    services.AddSingleton<IChatClient>(chat);
                },
                TestContext.Current.CancellationToken);
            return (exit, stderr.ToString() + stdout);
        }
        finally
        {
            Console.SetOut(origOut);
            Console.SetError(origErr);
            SqliteConnection.ClearAllPools();
        }
    }
}
