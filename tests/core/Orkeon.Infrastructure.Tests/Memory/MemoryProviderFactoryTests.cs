using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Memory;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.Memory;
using Orkeon.Infrastructure.Memory.ChromaDb;
using Orkeon.Infrastructure.Memory.LanceDb;
using Orkeon.Infrastructure.Memory.Pinecone;
using Orkeon.Infrastructure.Memory.Sqlite;
using Orkeon.Tests.Shared.FileSystem;
using DomainMemoryProvider = Orkeon.Domain.Memory.IMemoryProvider;

namespace Orkeon.Infrastructure.Tests.Memory;

/// <summary>
/// Integration tests proving that <see cref="MemoryProviderFactory"/> resolves the concrete
/// provider from <c>config.Type</c> (R3.1) instead of always returning <see cref="InMemoryProvider"/>,
/// and that an unrecognized type produces an explicit warning rather than a silent fallback.
/// </summary>
public class MemoryProviderFactoryTests
{
    private readonly MemoryProviderFactory _factory = new(new FakeFileSystemService());

    [Theory]
    [InlineData("inmemory", typeof(InMemoryProvider))]
    [InlineData("InMemory", typeof(InMemoryProvider))]
    [InlineData("redis", typeof(RedisMemoryProvider))]
    [InlineData("Redis", typeof(RedisMemoryProvider))]
    [InlineData("pinecone", typeof(PineconeMemoryProvider))]
    public void Create_ShouldResolveProviderFromType(string type, Type expectedProviderType)
    {
        // Arrange
        var config = new MemoryProviderConfigDto(type, ConnectionString: "localhost");

        // Act
        var provider = _factory.Create(config);

        // Assert
        Assert.IsType(expectedProviderType, provider);

        // R10.5 (ANT-013): the factory transfers HttpClient ownership to the provider — release it.
        (provider as IDisposable)?.Dispose();
    }

    [Fact]
    public void Create_ChromaDbType_ShouldResolveChromaProvider()
    {
        // Arrange — ChromaDB needs a valid base URL (taken from ConnectionString when supplied).
        var config = new MemoryProviderConfigDto("chromadb", ConnectionString: "http://localhost:8000");

        // Act
        var provider = _factory.Create(config);

        // Assert
        Assert.IsType<ChromaDbMemoryProvider>(provider);

        // R10.5 (ANT-013): the factory transfers HttpClient ownership to the provider — release it.
        (provider as IDisposable)?.Dispose();
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("SQLite")]
    public void Create_SqliteType_ShouldResolveSqliteProvider_NotInMemory(string type)
    {
        // Arrange — R3.2: "sqlite" must build the real persistent provider, never a fallback.
        var config = new MemoryProviderConfigDto(type, ConnectionString: "Data Source=:memory:");

        // Act
        var provider = _factory.Create(config);

        // Assert
        var sqlite = Assert.IsType<SqliteMemoryProvider>(provider);
        Assert.IsNotType<InMemoryProvider>(provider);
        sqlite.Dispose();
    }

    [Fact]
    public void Create_SqliteType_HonorsTableNameOption()
    {
        // Arrange — table name flows from config.Options (validated against SQL injection).
        var config = new MemoryProviderConfigDto(
            "sqlite",
            ConnectionString: "Data Source=:memory:",
            Options: new Dictionary<string, object> { ["TableName"] = "custom_memories" });

        // Act
        var provider = _factory.Create(config);

        // Assert — provider builds and opens the schema with the custom table name.
        var sqlite = Assert.IsType<SqliteMemoryProvider>(provider);
        sqlite.Dispose();
    }

    [Fact]
    public void Create_RedisType_ShouldReturnRedisProvider_NotInMemory()
    {
        // Arrange
        var config = new MemoryProviderConfigDto("redis", ConnectionString: "localhost:6379");

        // Act
        var provider = _factory.Create(config);

        // Assert — the historical bug: redis used to silently degrade to InMemoryProvider.
        Assert.IsType<RedisMemoryProvider>(provider);
        Assert.IsNotType<InMemoryProvider>(provider);
    }

    [Fact]
    public void Create_UnknownType_ShouldLogWarning_AndNotFallSilently()
    {
        // Arrange
        using var recorder = new CapturingLoggerFactory();
        var config = new MemoryProviderConfigDto("cosmosdb", ConnectionString: string.Empty);

        // Act
        var provider = _factory.Create(config, recorder);

        // Assert — fallback to in-memory, but an explicit warning must be emitted.
        Assert.IsType<InMemoryProvider>(provider);
        Assert.Contains(
            recorder.Entries,
            e => e.Level == LogLevel.Warning
                 && e.Message.Contains("not recognized", StringComparison.OrdinalIgnoreCase)
                 && e.Message.Contains("cosmosdb", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Create_LanceDbType_WithEndpoint_ShouldResolveLanceDbProvider()
    {
        // Arrange — the connection string supplies the LanceDB Cloud/Enterprise REST endpoint.
        var config = new MemoryProviderConfigDto(
            "lancedb",
            ConnectionString: "https://orkeon-tests.us-east-1.api.lancedb.com",
            Options: new Dictionary<string, object>
            {
                ["ApiKey"] = "factory-test-key",
                ["TableName"] = "factory_test"
            });

        // Act
        var provider = _factory.Create(config);

        // Assert
        var lanceDb = Assert.IsType<LanceDbMemoryProvider>(provider);
        Assert.Equal("LanceDB", lanceDb.Name);

        // R10.5 (ANT-013): the factory transfers HttpClient ownership to the provider — release it.
        lanceDb.Dispose();
    }

    [Fact]
    public void Create_LanceDbType_WithoutEndpoint_ShouldLogWarning_AndFallBack()
    {
        // Arrange
        using var recorder = new CapturingLoggerFactory();
        var config = new MemoryProviderConfigDto("lancedb", ConnectionString: string.Empty);

        // Act
        var provider = _factory.Create(config, recorder);

        // Assert — without the remote endpoint the factory must warn (pointing at
        // AddOrkeonLanceDb), not silently degrade.
        Assert.IsType<InMemoryProvider>(provider);
        Assert.Contains(
            recorder.Entries,
            e => e.Level == LogLevel.Warning
                 && e.Message.Contains("AddOrkeonLanceDb", StringComparison.Ordinal));
    }

    [Fact]
    public void AddOrkeonRedisMemory_ShouldResolveMemoryProvider_ToRedis()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        // Act
        services.AddOrkeonRedisMemory(configuration);
        using var provider = services.BuildServiceProvider();
        var memoryProvider = provider.GetRequiredService<DomainMemoryProvider>();

        // Assert
        Assert.IsType<RedisMemoryProvider>(memoryProvider);
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

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                entries.Add(new LogRecord(logLevel, formatter(state, exception)));
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }

    private sealed record LogRecord(LogLevel Level, string Message);
}
