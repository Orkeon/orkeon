using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Memory;
using Orkeon.Infrastructure.Memory;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;
using static Orkeon.Tests.Shared.Constants.TestStatusConstants;

namespace Orkeon.Infrastructure.Tests.Memory;

public class ContextualMemoryTests
{
    private class TestShortTerm : IShortTermMemory
    {
        private readonly List<MemoryItem> _items = [];
        public Task AddAsync(MemoryItem item) { _items.Add(item); return Task.CompletedTask; }
        public Task<IReadOnlyList<MemoryItem>> GetRecentAsync(int count = 10)
            => Task.FromResult((IReadOnlyList<MemoryItem>)_items.TakeLast(Math.Min(count, _items.Count)).ToList());
        public Task ClearAsync() { _items.Clear(); return Task.CompletedTask; }
        public void Clear() => _items.Clear();
    }

    private class TestLongTerm : ILongTermMemory
    {
        private readonly List<MemoryItem> _items = [];
        public Task AddAsync(MemoryItem item) { _items.Add(item); return Task.CompletedTask; }
        public Task<IReadOnlyList<MemoryItem>> SearchAsync(string query, int maxResults = 10)
        {
            var queryWords = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return Task.FromResult((IReadOnlyList<MemoryItem>)_items
                .Where(i => queryWords.Any(word => i.Content.Contains(word, StringComparison.OrdinalIgnoreCase)))
                .Take(maxResults)
                .ToList());
        }
        public Task ClearAsync() { _items.Clear(); return Task.CompletedTask; }
    }

    private class TestEntities : IEntityMemory
    {
        private readonly Dictionary<string, MemoryEntity> _entities = new(StringComparer.OrdinalIgnoreCase);
        public Task AddEntityAsync(string entityName, EntityType type, Dictionary<string, string> attributes)
        {
            _entities[entityName] = MemoryEntity.Create(entityName, type, attributes);
            return Task.CompletedTask;
        }
        public Task<MemoryEntity?> GetEntityAsync(string entityName)
            => Task.FromResult(_entities.TryGetValue(entityName, out var e) ? e : null);
        public Task<IReadOnlyList<MemoryEntity>> GetEntitiesByTypeAsync(EntityType type)
            => Task.FromResult((IReadOnlyList<MemoryEntity>)_entities.Values.Where(e => e.Type == type).ToList());
        public Task UpdateEntityAsync(string entityName, Dictionary<string, string> attributes)
        {
            if (_entities.TryGetValue(entityName, out var e))
                _entities[entityName] = MemoryEntity.Create(e.Name, e.Type, attributes);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task ShouldCombineShortLongTermAndEntities_WhenGetContextAsync()
    {
        var st = new TestShortTerm();
        var lt = new TestLongTerm();
        var em = new TestEntities();

        await st.AddAsync(MemoryItem.Create("Short one"));
        await st.AddAsync(MemoryItem.Create("Short two"));
        await lt.AddAsync(MemoryItem.Create("Relevant info about ProjectX"));
        await em.AddEntityAsync("ProjectX", EntityType.Concept, new Dictionary<string, string> { { "Status", Active } });

        var ctx = new ContextualMemory(st, lt, em);
        var text = await ctx.GetContextAsync("Tell me about ProjectX", maxTokens: 1000);

        Assert.Contains("Recent context:", text);
        Assert.Contains("Relevant information:", text);
        Assert.Contains("ProjectX (Concept):", text);
    }

    [Fact]
    public async Task ShouldSortByCombinedScore_WhenGetRelevantMemoriesAsync()
    {
        var st = new TestShortTerm();
        var lt = new TestLongTerm();
        var em = new TestEntities();
        await st.AddAsync(MemoryItem.Create("alpha beta gamma", importance: 0.1f));
        await st.AddAsync(MemoryItem.Create("delta epsilon", importance: 0.9f));
        await lt.AddAsync(MemoryItem.Create("beta related content", importance: 0.4f));

        var ctx = new ContextualMemory(st, lt, em);
        var res = await ctx.GetRelevantMemoriesAsync("beta topic", 3);

        Assert.Equal(3, res.Count);
        // Expect the one containing 'beta' with decent importance to rank high
        Assert.Contains(res, m => m.Content.Contains("beta"));
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructorWithNullShortTermMemory()
    {
        // Arrange
        var longTerm = new TestLongTerm();
        var entities = new TestEntities();

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new ContextualMemory(null!, longTerm, entities));
        Assert.Equal("shortTerm", exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructorWithNullLongTermMemory()
    {
        // Arrange
        var shortTerm = new TestShortTerm();
        var entities = new TestEntities();

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new ContextualMemory(shortTerm, null!, entities));
        Assert.Equal("longTerm", exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructorWithNullEntityMemory()
    {
        // Arrange
        var shortTerm = new TestShortTerm();
        var longTerm = new TestLongTerm();

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new ContextualMemory(shortTerm, longTerm, null!));
        Assert.Equal("entities", exception.ParamName);
    }

    [Fact]
    public async Task ShouldReturnEmptyString_WhenGetContextAsyncWithEmptyMemories()
    {
        // Arrange
        var shortTerm = new TestShortTerm();
        var longTerm = new TestLongTerm();
        var entities = new TestEntities();
        var memory = new ContextualMemory(shortTerm, longTerm, entities);

        // Act
        var context = await memory.GetContextAsync("test query");

        // Assert
        Assert.Equal(string.Empty, context);
    }

    [Fact]
    public async Task ShouldIncludeRecentContext_WhenGetContextAsyncWithOnlyShortTermMemories()
    {
        // Arrange
        var shortTerm = new TestShortTerm();
        var longTerm = new TestLongTerm();
        var entities = new TestEntities();

        await shortTerm.AddAsync(MemoryItem.Create("Memory 1"));
        await shortTerm.AddAsync(MemoryItem.Create("Memory 2"));
        await shortTerm.AddAsync(MemoryItem.Create("Memory 3"));

        var memory = new ContextualMemory(shortTerm, longTerm, entities);

        // Act
        var context = await memory.GetContextAsync("test query");

        // Assert
        Assert.Contains("Recent context:", context);
        Assert.Contains("Memory 1", context);
        Assert.Contains("Memory 2", context);
        Assert.Contains("Memory 3", context);
    }

    [Fact]
    public async Task ShouldIncludeRelevantInformation_WhenGetContextAsyncWithOnlyLongTermMemories()
    {
        // Arrange
        var shortTerm = new TestShortTerm();
        var longTerm = new TestLongTerm();
        var entities = new TestEntities();

        await longTerm.AddAsync(MemoryItem.Create("Information about Python"));
        await longTerm.AddAsync(MemoryItem.Create("Details about Java"));
        await longTerm.AddAsync(MemoryItem.Create("Notes on C#"));

        var memory = new ContextualMemory(shortTerm, longTerm, entities);

        // Act
        var context = await memory.GetContextAsync("Python programming");

        // Assert
        Assert.Contains("Relevant information:", context);
        Assert.Contains("Python", context);
    }

    [Fact]
    public async Task ShouldIncludeEntityInformation_WhenGetContextAsyncWithCapitalizedWords()
    {
        // Arrange
        var shortTerm = new TestShortTerm();
        var longTerm = new TestLongTerm();
        var entities = new TestEntities();

        await entities.AddEntityAsync("Microsoft", EntityType.Organization,
            new Dictionary<string, string>
            {
                { "Industry", "Technology" },
                { "Founded", "1975" },
                { "CEO", "Satya Nadella" }
            });

        var memory = new ContextualMemory(shortTerm, longTerm, entities);

        // Act
        var context = await memory.GetContextAsync("Tell me about Microsoft products");

        // Assert
        Assert.Contains("Microsoft (Organization):", context);
        Assert.Contains("Industry: Technology", context);
        Assert.Contains("Founded: 1975", context);
        Assert.Contains("CEO: Satya Nadella", context);
    }

    [Fact]
    public async Task ShouldLimitOutput_WhenGetContextAsyncWithMaxTokensLimit()
    {
        // Arrange
        var shortTerm = new TestShortTerm();
        var longTerm = new TestLongTerm();
        var entities = new TestEntities();

        // Add many memories to exceed token limit
        for (int i = 0; i < 20; i++)
        {
            await shortTerm.AddAsync(MemoryItem.Create($"Short term memory {i} with lots of text to increase token count"));
            await longTerm.AddAsync(MemoryItem.Create($"Long term memory {i} with even more text to ensure we exceed the token limit"));
        }

        var memory = new ContextualMemory(shortTerm, longTerm, entities);

        // Act
        var context = await memory.GetContextAsync("test", maxTokens: 100);

        // Assert
        // With token limit of 100 and rough estimate of 4 chars per token, should be around 400 chars
        Assert.True(context.Length < 500, $"Context length {context.Length} should be limited by maxTokens");
    }

    [Fact]
    public async Task ShouldIncludeAllEntities_WhenGetContextAsyncWithMultipleEntityReferences()
    {
        // Arrange
        var shortTerm = new TestShortTerm();
        var longTerm = new TestLongTerm();
        var entities = new TestEntities();

        await entities.AddEntityAsync("Alice", EntityType.Person,
            new Dictionary<string, string> { { "Role", RoleDeveloper } });
        await entities.AddEntityAsync("Bob", EntityType.Person,
            new Dictionary<string, string> { { "Role", RoleManager } });

        var memory = new ContextualMemory(shortTerm, longTerm, entities);

        // Act
        var context = await memory.GetContextAsync("Alice and Bob are working together");

        // Assert
        Assert.Contains("Alice (Person):", context);
        Assert.Contains("Role: Developer", context);
        Assert.Contains("Bob (Person):", context);
        Assert.Contains("Role: Manager", context);
    }

    [Fact]
    public async Task ShouldReturnAllMemories_WhenGetRelevantMemoriesAsyncWithEmptyContext()
    {
        // Arrange
        var shortTerm = new TestShortTerm();
        var longTerm = new TestLongTerm();
        var entities = new TestEntities();

        await shortTerm.AddAsync(MemoryItem.Create("Memory 1"));
        await shortTerm.AddAsync(MemoryItem.Create("Memory 2"));

        var memory = new ContextualMemory(shortTerm, longTerm, entities);

        // Act
        var results = await memory.GetRelevantMemoriesAsync("", 10);

        // Assert
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task ShouldScoreHigherForMatches_WhenGetRelevantMemoriesAsyncWithKeywordMatching()
    {
        // Arrange
        var shortTerm = new TestShortTerm();
        var longTerm = new TestLongTerm();
        var entities = new TestEntities();

        await shortTerm.AddAsync(MemoryItem.Create("programming python code development", importance: 0.2f));
        await shortTerm.AddAsync(MemoryItem.Create("cooking recipe food", importance: 0.8f));
        await longTerm.AddAsync(MemoryItem.Create("python tutorial programming", importance: 0.3f));

        var memory = new ContextualMemory(shortTerm, longTerm, entities);

        // Act
        var results = await memory.GetRelevantMemoriesAsync("python programming", 3);

        // Assert
        Assert.Equal(3, results.Count);
        // The two memories with "python" and "programming" should rank higher
        var topTwo = results.Take(2).ToList();
        Assert.All(topTwo, m => Assert.Contains("python", m.Content.ToLower()));
    }

    [Fact]
    public async Task ShouldInfluenceScore_WhenGetRelevantMemoriesAsyncWithHighImportance()
    {
        // Arrange
        var shortTerm = new TestShortTerm();
        var longTerm = new TestLongTerm();
        var entities = new TestEntities();

        await shortTerm.AddAsync(MemoryItem.Create("unrelated content", importance: 0.95f));
        await shortTerm.AddAsync(MemoryItem.Create("test keyword match", importance: 0.1f));

        var memory = new ContextualMemory(shortTerm, longTerm, entities);

        // Act
        var results = await memory.GetRelevantMemoriesAsync("different topic", 2);

        // Assert
        Assert.Equal(2, results.Count);
        // High importance memory should still be included due to importance weight
        Assert.Contains(results, m => m.Content == "unrelated content");
    }

    [Fact]
    public async Task ShouldLimitResults_WhenGetRelevantMemoriesAsyncWithRequestedCount()
    {
        // Arrange
        var shortTerm = new TestShortTerm();
        var longTerm = new TestLongTerm();
        var entities = new TestEntities();

        for (int i = 0; i < 10; i++)
        {
            await shortTerm.AddAsync(MemoryItem.Create($"Memory {i}"));
            await longTerm.AddAsync(MemoryItem.Create($"Long memory {i}"));
        }

        var memory = new ContextualMemory(shortTerm, longTerm, entities);

        // Act
        var results = await memory.GetRelevantMemoriesAsync("memory", 5);

        // Assert
        Assert.Equal(5, results.Count);
    }

    [Fact]
    public async Task ShouldReturnEmptyList_WhenGetRelevantMemoriesAsyncWithNoMemories()
    {
        // Arrange
        var shortTerm = new TestShortTerm();
        var longTerm = new TestLongTerm();
        var entities = new TestEntities();
        var memory = new ContextualMemory(shortTerm, longTerm, entities);

        // Act
        var results = await memory.GetRelevantMemoriesAsync("test context", 10);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task ShouldCombineAll_WhenGetContextAsyncWithAllMemoryTypes()
    {
        // Arrange
        var shortTerm = new TestShortTerm();
        var longTerm = new TestLongTerm();
        var entities = new TestEntities();

        await shortTerm.AddAsync(MemoryItem.Create("Recent discussion about Project"));
        await longTerm.AddAsync(MemoryItem.Create("Project requirements from last month"));
        await entities.AddEntityAsync("Project", EntityType.Concept,
            new Dictionary<string, string> { { "Status", "In Progress" } });

        var memory = new ContextualMemory(shortTerm, longTerm, entities);

        // Act
        var context = await memory.GetContextAsync("Update on Project status");

        // Assert
        Assert.Contains("Recent context:", context);
        Assert.Contains("Recent discussion", context);
        Assert.Contains("Relevant information:", context);
        Assert.Contains("requirements", context);
        Assert.Contains("Project (Concept):", context);
        Assert.Contains("Status: In Progress", context);
    }

    [Fact]
    public async Task ShouldHandleGracefully_WhenGetRelevantMemoriesAsyncWithDuplicateMemories()
    {
        // Arrange
        var shortTerm = new TestShortTerm();
        var longTerm = new TestLongTerm();
        var entities = new TestEntities();

        var sameContent = "Same memory content";
        await shortTerm.AddAsync(MemoryItem.Create(sameContent, importance: 0.5f));
        await longTerm.AddAsync(MemoryItem.Create(sameContent, importance: 0.5f));

        var memory = new ContextualMemory(shortTerm, longTerm, entities);

        // Act
        var results = await memory.GetRelevantMemoriesAsync("memory", 10);

        // Assert
        Assert.Equal(2, results.Count);
        Assert.All(results, m => Assert.Equal(sameContent, m.Content));
    }

    [Fact]
    public async Task ShouldStillReturnContext_WhenGetContextAsyncWithEmptyQuery()
    {
        // Arrange
        var shortTerm = new TestShortTerm();
        var longTerm = new TestLongTerm();
        var entities = new TestEntities();

        await shortTerm.AddAsync(MemoryItem.Create("Recent memory"));
        await longTerm.AddAsync(MemoryItem.Create("Old memory"));

        var memory = new ContextualMemory(shortTerm, longTerm, entities);

        // Act
        var context = await memory.GetContextAsync("");

        // Assert
        Assert.Contains("Recent context:", context);
        Assert.Contains("Recent memory", context);
        // Empty query won't match anything in long-term search
    }

    [Fact]
    public async Task ShouldPreserveTimestampOrdering_WhenGetRelevantMemoriesAsync()
    {
        // Arrange
        var shortTerm = new TestShortTerm();
        var longTerm = new TestLongTerm();
        var entities = new TestEntities();

        var older = MemoryItem.Create("older content", importance: 0.5f);
        var newer = MemoryItem.Create("newer content", importance: 0.5f);

        await shortTerm.AddAsync(older);
        await shortTerm.AddAsync(newer);

        var memory = new ContextualMemory(shortTerm, longTerm, entities);

        // Act
        var results = await memory.GetRelevantMemoriesAsync("unrelated", 2);

        // Assert
        Assert.Equal(2, results.Count);
        // When relevance scores are equal, newer should come first
        Assert.Equal("newer content", results[0].Content);
    }

    [Fact]
    public async Task ShouldLimitByTokens_WhenGetContextAsyncWithManyEntities()
    {
        // Arrange
        var shortTerm = new TestShortTerm();
        var longTerm = new TestLongTerm();
        var entities = new TestEntities();

        // Create many entities
        for (int i = 0; i < 10; i++)
        {
            await entities.AddEntityAsync($"Entity{i}", EntityType.Concept,
                new Dictionary<string, string>
                {
                    { "Prop1", $"Value{i}_1" },
                    { "Prop2", $"Value{i}_2" },
                    { "Prop3", $"Value{i}_3" },
                    { "Prop4", $"Value{i}_4" },
                    { "Prop5", $"Value{i}_5" }
                });
        }

        var memory = new ContextualMemory(shortTerm, longTerm, entities);

        // Act
        // Query mentions all entities
        var query = string.Join(" ", Enumerable.Range(0, 10).Select(i => $"Entity{i}"));
        var context = await memory.GetContextAsync(query, maxTokens: 200);

        // Assert
        // Should stop adding entities when token limit is reached
        Assert.True(context.Length < 1000); // Rough check based on maxTokens=200
        Assert.Contains("Entity0", context); // At least the first should be included
    }
}
