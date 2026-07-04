using System.Collections.Concurrent;
using Orkeon.Domain.Memory;
using Orkeon.Infrastructure.Memory.Base;
using Orkeon.Infrastructure.Memory.Migration;
using Orkeon.Infrastructure.Tests.TestDoubles;

namespace Orkeon.Infrastructure.Tests.Memory.Migration;

public class MemoryMigrationServiceTestsFixture
{
    private readonly TestLogger<MemoryMigrationService> _logger;

    public MemoryMigrationServiceTestsFixture()
    {
        _logger = new TestLogger<MemoryMigrationService>();
    }

    public MemoryMigrationService CreateService()
    {
        return new MemoryMigrationService(_logger);
    }

    public static FakeMemoryProvider CreateFakeProvider()
    {
        return new FakeMemoryProvider();
    }

    public TestLogger<MemoryMigrationService> GetLogger() => _logger;

    /// <summary>
    /// A simple in-memory fake implementation of MemoryProviderBase for testing.
    /// This does NOT reference InMemoryProvider from Infrastructure to avoid coupling.
    /// </summary>
    public class FakeMemoryProvider : MemoryProviderBase
    {
        private readonly ConcurrentDictionary<string, MemoryItem> _storage = new();
        private readonly Func<string, Exception?>? _storeExceptionFactory;
        private readonly Func<string, Exception?>? _getExceptionFactory;

        public override string Name => "FakeMemory";

        public FakeMemoryProvider(
            Func<string, Exception?>? storeExceptionFactory = null,
            Func<string, Exception?>? getExceptionFactory = null)
            : base(null)
        {
            _storeExceptionFactory = storeExceptionFactory;
            _getExceptionFactory = getExceptionFactory;
        }

        public override Task StoreAsync(string key, MemoryItem item, CancellationToken cancellationToken = default)
        {
            ValidateKey(key);
            ValidateMemoryItem(item);

            var ex = _storeExceptionFactory?.Invoke(key);
            if (ex != null) throw ex;

            _storage.AddOrUpdate(key, item, (_, _) => item);
            return Task.CompletedTask;
        }

        public override Task<MemoryItem?> GetAsync(string key, CancellationToken cancellationToken = default)
        {
            ValidateKey(key);

            var ex = _getExceptionFactory?.Invoke(key);
            if (ex != null) throw ex;

            _storage.TryGetValue(key, out var item);
            return Task.FromResult(item);
        }

        public override Task<bool> UpdateAsync(string key, MemoryItem item, CancellationToken cancellationToken = default)
        {
            ValidateKey(key);
            if (!_storage.ContainsKey(key)) return Task.FromResult(false);
            _storage[key] = item;
            return Task.FromResult(true);
        }

        public override Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            ValidateKey(key);
            return Task.FromResult(_storage.TryRemove(key, out _));
        }

        public override Task<IEnumerable<MemoryItem>> SearchAsync(string query, int limit = 10, CancellationToken cancellationToken = default)
        {
            var results = _storage.Values
                .Where(i => i.Content.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Take(limit);
            return Task.FromResult(results);
        }

        public override Task<IReadOnlyList<ScoredMemoryItem>> SearchSimilarAsync(float[] queryEmbedding, int topK = 10, float minScore = 0.0f, Dictionary<string, object>? filter = null, CancellationToken cancellationToken = default)
        {
            var scored = new List<ScoredMemoryItem>();
            foreach (var item in _storage.Values)
            {
                var embedding = item.Embedding;
                if (embedding == null || embedding.Count != queryEmbedding.Length)
                    continue;
                var score = Orkeon.Domain.SharedKernel.ValueObjects.VectorMath.CosineSimilarity(queryEmbedding, embedding.ToArray());
                if (score >= minScore)
                    scored.Add(new ScoredMemoryItem(item, score));
            }
            return Task.FromResult<IReadOnlyList<ScoredMemoryItem>>(scored.OrderByDescending(s => s.Score).Take(topK).ToList());
        }

        public override Task ClearAsync(CancellationToken cancellationToken = default)
        {
            _storage.Clear();
            return Task.CompletedTask;
        }

        public override Task<int> CountAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_storage.Count);
        }

        public override Task<List<string>> ListKeysAsync(int skip = 0, int take = 100, CancellationToken cancellationToken = default)
        {
            var keys = _storage.Keys.Skip(skip).Take(take).ToList();
            return Task.FromResult(keys);
        }

        /// <summary>Seeds the provider with test data.</summary>
        public void Seed(string key, MemoryItem item)
        {
            _storage[key] = item;
        }

        /// <summary>Gets the number of stored items.</summary>
        public int Count => _storage.Count;

        /// <summary>Checks if a key exists.</summary>
        public bool ContainsKey(string key) => _storage.ContainsKey(key);
    }
}
