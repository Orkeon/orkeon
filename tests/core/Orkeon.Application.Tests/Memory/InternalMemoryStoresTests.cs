using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Memory;
using Orkeon.Domain.Common;
using Orkeon.Domain.Memory;

namespace Orkeon.Application.Tests.Memory;

/// <summary>
/// Direct coverage for the internal memory store implementations that live inside
/// <c>MemoryService.cs</c> (<see cref="SimpleEntityMemory"/>, <see cref="SimpleContextualMemory"/>,
/// <see cref="CrewMemorySystem"/>, <see cref="SimpleShortTermMemory"/>,
/// <see cref="InternalLongTermMemory"/>). These are exercised through the
/// <c>InternalsVisibleTo("Orkeon.Application.Tests")</c> hook declared in the production csproj.
/// The public <see cref="MemoryService"/> API never touches the Entities/Contextual stores, so the
/// only way to cover them is to drive their methods directly.
/// </summary>
public class InternalMemoryStoresTests
{
    private static MemoryItem Item(string content, float importance = 0.5f)
        => MemoryItem.Create(content: content, embedding: null, importance: importance, source: "test");

    // ── SimpleEntityMemory ───────────────────────────────────────

    [Fact]
    public async System.Threading.Tasks.Task EntityMemory_AddThenGet_ReturnsStoredEntity()
    {
        var memory = new SimpleEntityMemory();
        var attributes = new Dictionary<string, string> { ["role"] = "ceo" };

        await memory.AddEntityAsync("Alice", EntityType.Person, attributes);
        var fetched = await memory.GetEntityAsync("Alice");

        Assert.NotNull(fetched);
        Assert.Equal("Alice", fetched!.Name);
        Assert.Equal(EntityType.Person, fetched.Type);
        Assert.Equal("ceo", fetched.Attributes["role"]);
    }

    [Fact]
    public async System.Threading.Tasks.Task EntityMemory_GetMissingEntity_ReturnsNull()
    {
        var memory = new SimpleEntityMemory();

        var fetched = await memory.GetEntityAsync("Unknown");

        Assert.Null(fetched);
    }

    [Fact]
    public async System.Threading.Tasks.Task EntityMemory_GetEntitiesByType_FiltersByType()
    {
        var memory = new SimpleEntityMemory();
        await memory.AddEntityAsync("Alice", EntityType.Person, new Dictionary<string, string>());
        await memory.AddEntityAsync("Bob", EntityType.Person, new Dictionary<string, string>());
        await memory.AddEntityAsync("Acme", EntityType.Organization, new Dictionary<string, string>());

        var people = await memory.GetEntitiesByTypeAsync(EntityType.Person);
        var orgs = await memory.GetEntitiesByTypeAsync(EntityType.Organization);

        Assert.Equal(2, people.Count);
        Assert.Single(orgs);
        Assert.Equal("Acme", orgs[0].Name);
    }

    [Fact]
    public async System.Threading.Tasks.Task EntityMemory_GetEntitiesByType_NoMatch_ReturnsEmpty()
    {
        var memory = new SimpleEntityMemory();
        await memory.AddEntityAsync("Alice", EntityType.Person, new Dictionary<string, string>());

        var tools = await memory.GetEntitiesByTypeAsync(EntityType.Tool);

        Assert.Empty(tools);
    }

    [Fact]
    public async System.Threading.Tasks.Task EntityMemory_UpdateEntity_ReplacesAttributesAndBumpsTimestamp()
    {
        var memory = new SimpleEntityMemory();
        await memory.AddEntityAsync("Alice", EntityType.Person,
            new Dictionary<string, string> { ["role"] = "ceo" });
        var before = (await memory.GetEntityAsync("Alice"))!;

        await memory.UpdateEntityAsync("Alice",
            new Dictionary<string, string> { ["role"] = "cto", ["team"] = "eng" });
        var after = (await memory.GetEntityAsync("Alice"))!;

        Assert.Equal("cto", after.Attributes["role"]);
        Assert.Equal("eng", after.Attributes["team"]);
        Assert.True(after.LastUpdated >= before.LastUpdated);
    }

    [Fact]
    public async System.Threading.Tasks.Task EntityMemory_UpdateMissingEntity_IsNoOp()
    {
        var memory = new SimpleEntityMemory();

        // Updating an entity that was never added must not throw and must not create it.
        await memory.UpdateEntityAsync("Ghost",
            new Dictionary<string, string> { ["x"] = "y" });

        Assert.Null(await memory.GetEntityAsync("Ghost"));
    }

    // ── SimpleContextualMemory ───────────────────────────────────

    [Fact]
    public async System.Threading.Tasks.Task ContextualMemory_GetRelevantMemories_CombinesShortAndLongTerm()
    {
        var shortTerm = new SimpleShortTermMemory();
        using var longTerm = new InternalLongTermMemory();
        await shortTerm.AddAsync(Item("short term note about kubernetes"));
        await longTerm.AddAsync(Item("long term note about kubernetes"));
        var contextual = new SimpleContextualMemory(shortTerm, longTerm);

        var relevant = await contextual.GetRelevantMemoriesAsync("kubernetes", count: 10);

        Assert.NotEmpty(relevant);
        Assert.Contains(relevant, m => m.Content.Contains("short term", StringComparison.Ordinal));
        Assert.Contains(relevant, m => m.Content.Contains("long term", StringComparison.Ordinal));
    }

    [Fact]
    public async System.Threading.Tasks.Task ContextualMemory_GetContext_JoinsMemoryContent()
    {
        var shortTerm = new SimpleShortTermMemory();
        using var longTerm = new InternalLongTermMemory();
        await shortTerm.AddAsync(Item("alpha"));
        await longTerm.AddAsync(Item("beta keyword"));
        var contextual = new SimpleContextualMemory(shortTerm, longTerm);

        var context = await contextual.GetContextAsync("keyword", maxTokens: 1000);

        Assert.Contains("beta keyword", context, StringComparison.Ordinal);
    }

    [Fact]
    public async System.Threading.Tasks.Task ContextualMemory_GetContext_TruncatesToMaxTokens()
    {
        var shortTerm = new SimpleShortTermMemory();
        using var longTerm = new InternalLongTermMemory();
        // maxTokens * 4 is the char budget; build a payload that exceeds it.
        var maxTokens = 5;
        await shortTerm.AddAsync(Item(new string('x', 500)));
        var contextual = new SimpleContextualMemory(shortTerm, longTerm);

        var context = await contextual.GetContextAsync("x", maxTokens);

        Assert.Equal(maxTokens * 4, context.Length);
    }

    [Fact]
    public async System.Threading.Tasks.Task ContextualMemory_GetContext_EmptyStores_ReturnsEmptyString()
    {
        using var longTerm = new InternalLongTermMemory();
        var contextual = new SimpleContextualMemory(new SimpleShortTermMemory(), longTerm);

        var context = await contextual.GetContextAsync("anything");

        Assert.Equal(string.Empty, context);
    }

    // ── SimpleShortTermMemory ────────────────────────────────────

    [Fact]
    public async System.Threading.Tasks.Task ShortTermMemory_GetRecent_ReturnsLastN()
    {
        var memory = new SimpleShortTermMemory();
        for (int i = 0; i < 5; i++)
            await memory.AddAsync(Item($"item-{i}"));

        var recent = await memory.GetRecentAsync(2);

        Assert.Equal(2, recent.Count);
        Assert.Equal("item-3", recent[0].Content);
        Assert.Equal("item-4", recent[1].Content);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShortTermMemory_ExceedsCapacity_EvictsOldest()
    {
        var memory = new SimpleShortTermMemory();
        // MaxItems is 50; push 60 and ensure only the most recent survive.
        for (int i = 0; i < 60; i++)
            await memory.AddAsync(Item($"item-{i}"));

        var recent = await memory.GetRecentAsync(100);

        Assert.Equal(50, recent.Count);
        Assert.Equal("item-10", recent[0].Content);
        Assert.Equal("item-59", recent[^1].Content);
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1849:Call async methods when in an async method",
        Justification = "The synchronous Clear() API is precisely what this test covers; switching to ClearAsync() would change the code path under test.")]
    public async System.Threading.Tasks.Task ShortTermMemory_Clear_RemovesAll()
    {
        var memory = new SimpleShortTermMemory();
        await memory.AddAsync(Item("a"));
        await memory.AddAsync(Item("b"));

        memory.Clear();
        var recent = await memory.GetRecentAsync(10);

        Assert.Empty(recent);
    }

    // ── InternalLongTermMemory ───────────────────────────────────

    [Fact]
    public async System.Threading.Tasks.Task LongTermMemory_Search_MatchesByKeywordAndOrdersByRecency()
    {
        using var memory = new InternalLongTermMemory();
        await memory.AddAsync(Item("oldest about ai"));
        await System.Threading.Tasks.Task.Delay(5, TestContext.Current.CancellationToken);
        await memory.AddAsync(Item("newest about ai"));
        await memory.AddAsync(Item("unrelated text"));

        var results = await memory.SearchAsync("ai", maxResults: 10);

        Assert.Equal(2, results.Count);
        Assert.Equal("newest about ai", results[0].Content);
    }

    [Fact]
    public async System.Threading.Tasks.Task LongTermMemory_Search_RespectsMaxResults()
    {
        using var memory = new InternalLongTermMemory();
        for (int i = 0; i < 5; i++)
            await memory.AddAsync(Item($"match {i}"));

        var results = await memory.SearchAsync("match", maxResults: 2);

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async System.Threading.Tasks.Task LongTermMemory_Clear_RemovesAll()
    {
        using var memory = new InternalLongTermMemory();
        await memory.AddAsync(Item("persisted"));

        await memory.ClearAsync();
        var results = await memory.SearchAsync("persisted");

        Assert.Empty(results);
    }

    // ── CrewMemorySystem ─────────────────────────────────────────

    [Fact]
    public void CrewMemorySystem_ExposesAllFourStores()
    {
        using var system = new CrewMemorySystem(
            CrewId.From(Guid.NewGuid()),
            new Orkeon.Application.Tests.TestDoubles.TestMemoryProviderFactory(),
            NullLogger.Instance);

        Assert.NotNull(system.ShortTerm);
        Assert.NotNull(system.LongTerm);
        Assert.NotNull(system.Entities);
        Assert.NotNull(system.Contextual);
    }

    [Fact]
    public void CrewMemorySystem_Dispose_IsIdempotent()
    {
        var system = new CrewMemorySystem(
            CrewId.From(Guid.NewGuid()),
            new Orkeon.Application.Tests.TestDoubles.TestMemoryProviderFactory(),
            NullLogger.Instance);

        system.Dispose();
        var ex = Record.Exception(() => system.Dispose());

        Assert.Null(ex);
    }
}
