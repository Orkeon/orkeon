using Orkeon.Infrastructure.Constants.Llm;
using Orkeon.Infrastructure.Constants.Memory;

namespace Orkeon.Infrastructure.Memory;

/// <summary>
/// Connection options of the Redis memory provider, bound from the host's
/// <c>Orkeon:Redis</c> section (GAP-08). Every path that selects Redis by type — the
/// application-wide <c>Memory:Provider</c>, a crew's <c>memoryProvider:</c>,
/// <c>Orkeon:Rag:Provider</c>, <c>AddOrkeonRedisMemory</c> — connects with these.
/// </summary>
public sealed class RedisMemoryOptions
{
    /// <summary>The configuration section bound to these options.</summary>
    public const string SectionName = "Orkeon:Redis";

    /// <summary>
    /// Gets or sets the StackExchange.Redis connection string
    /// (e.g. <c>localhost:6379</c>, or <c>redis.internal:6379,password=…,ssl=true</c>).
    /// </summary>
    public string ConnectionString { get; set; } = LlmEndpoints.RedisDefault;

    /// <summary>Gets or sets the prefix namespacing every key the provider writes.</summary>
    public string KeyPrefix { get; set; } = RedisDefaults.MemoryKeyPrefix;
}
