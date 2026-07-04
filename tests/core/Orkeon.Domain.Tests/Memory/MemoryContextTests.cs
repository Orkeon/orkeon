using Orkeon.Domain.Memory;
using Orkeon.Domain.Tests.Fixtures;

namespace Orkeon.Domain.Tests.Memory;

public class MemoryContextTests
{
    #region Test Helpers

    private static MemoryItem CreateMemoryItem(string content, float importance = 0.5f)
    {
        return MemoryItem.Create(content, importance: importance);
    }

    private static EntityMemory CreateEntityMemory(string name, string type, string description = "")
    {
        return new EntityMemory(name, type, description);
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void ShouldCreateContext_WhenConstructingWithValidCollections()
    {
        // Arrange
        var recentMemories = new List<MemoryItem>
        {
            CreateMemoryItem("Recent memory 1"),
            CreateMemoryItem("Recent memory 2")
        };

        var taskRelatedMemories = new List<MemoryItem>
        {
            CreateMemoryItem("Task related 1"),
            CreateMemoryItem("Task related 2"),
            CreateMemoryItem("Task related 3")
        };

        var relevantEntities = new List<EntityMemory>
        {
            CreateEntityMemory("Entity1", "person"),
            CreateEntityMemory("Entity2", "organization")
        };

        // Act
        var context = MemoryContext.Build(recentMemories, taskRelatedMemories, relevantEntities);

        // Assert
        Assert.Equal(2, context.RecentMemories.Count);
        Assert.Equal(3, context.TaskRelatedMemories.Count);
        Assert.Equal(2, context.RelevantEntities.Count);
        Assert.True(context.CreatedAt <= DateTime.UtcNow);
    }

    [Fact]
    public void ShouldCreateEmptyCollections_WhenConstructingWithNullCollections()
    {
        // Act
        var context = MemoryContext.Build(null!, null!, null!);

        // Assert
        Assert.NotNull(context.RecentMemories);
        Assert.NotNull(context.TaskRelatedMemories);
        Assert.NotNull(context.RelevantEntities);
        Assert.Empty(context.RecentMemories);
        Assert.Empty(context.TaskRelatedMemories);
        Assert.Empty(context.RelevantEntities);
    }

    [Fact]
    public void ShouldCreateEmptyContext_WhenConstructingWithEmptyCollections()
    {
        // Arrange
        var emptyMemories = new List<MemoryItem>();
        var emptyEntities = new List<EntityMemory>();

        // Act
        var context = MemoryContext.Build(emptyMemories, emptyMemories, emptyEntities);

        // Assert
        Assert.Empty(context.RecentMemories);
        Assert.Empty(context.TaskRelatedMemories);
        Assert.Empty(context.RelevantEntities);
    }

    [Fact]
    public void ShouldCreateReadOnlyCollections_WhenConstructing()
    {
        // Arrange
        var mutableList = new List<MemoryItem> { CreateMemoryItem("Test") };
        var context = MemoryContext.Build(mutableList, mutableList, []);

        // Act & Assert
        Assert.Throws<NotSupportedException>(() =>
            ((IList<MemoryItem>)context.RecentMemories).Add(CreateMemoryItem("New")));
        Assert.Throws<NotSupportedException>(() =>
            ((IList<MemoryItem>)context.TaskRelatedMemories).Clear());
    }

    [Fact]
    public void ShouldNotBeAffectedByOriginalListModification_WhenConstructing()
    {
        // Arrange
        var originalMemories = new List<MemoryItem> { CreateMemoryItem("Original") };
        var context = MemoryContext.Build(originalMemories, null!, null!);

        // Act
        originalMemories.Add(CreateMemoryItem("Added after"));
        originalMemories[0] = CreateMemoryItem("Modified");

        // Assert
        Assert.Single(context.RecentMemories);
        Assert.Equal("Original", context.RecentMemories[0].Content);
    }

    [Fact]
    public void ShouldSetCreatedAtToCurrentTime_WhenConstructing()
    {
        // Arrange
        var before = DateTime.UtcNow;

        // Act
        var context = MemoryContext.Build(null!, null!, null!);
        var after = DateTime.UtcNow;

        // Assert
        Assert.True(context.CreatedAt >= before);
        Assert.True(context.CreatedAt <= after);
    }

    #endregion

    #region GetSummary Tests

    [Fact]
    public void ShouldReturnFormattedSummary_WhenGettingSummaryWithAllMemoryTypes()
    {
        // Arrange
        var recentMemories = new List<MemoryItem>
        {
            CreateMemoryItem("Completed data analysis"),
            CreateMemoryItem("Generated report")
        };

        var taskRelatedMemories = new List<MemoryItem>
        {
            CreateMemoryItem("Previous analysis results"),
            CreateMemoryItem("Template for reports"),
            CreateMemoryItem("Best practices guide")
        };

        var relevantEntities = new List<EntityMemory>
        {
            CreateEntityMemory("DataAnalyst", "role", "Performs data analysis"),
            CreateEntityMemory("ReportTemplate", "document", "Standard report format")
        };

        var context = MemoryContext.Build(recentMemories, taskRelatedMemories, relevantEntities);

        // Act
        var summary = context.GetSummary();

        // Assert
        Assert.Contains("Recent context (2 items):", summary);
        Assert.Contains("- Completed data analysis", summary);
        Assert.Contains("- Generated report", summary);

        Assert.Contains("Task-related memories (3 items):", summary);
        Assert.Contains("- Previous analysis results", summary);
        Assert.Contains("- Template for reports", summary);
        Assert.Contains("- Best practices guide", summary);

        Assert.Contains("Relevant entities (2):", summary);
        Assert.Contains("- DataAnalyst (role): Performs data analysis", summary);
        Assert.Contains("- ReportTemplate (document): Standard report format", summary);
    }

    [Fact]
    public void ShouldReturnPartialSummary_WhenGettingSummaryWithOnlyRecentMemories()
    {
        // Arrange
        var recentMemories = new List<MemoryItem>
        {
            CreateMemoryItem("Memory 1"),
            CreateMemoryItem("Memory 2")
        };
        var context = MemoryContext.Build(recentMemories, null!, null!);

        // Act
        var summary = context.GetSummary();

        // Assert
        Assert.Contains("Recent context (2 items):", summary);
        Assert.Contains("- Memory 1", summary);
        Assert.Contains("- Memory 2", summary);
        Assert.DoesNotContain("Task-related memories", summary);
        Assert.DoesNotContain("Relevant entities", summary);
    }

    [Fact]
    public void ShouldReturnEmptyString_WhenGettingSummaryWithEmptyContext()
    {
        // Arrange
        var context = MemoryContext.Build(null!, null!, null!);

        // Act
        var summary = context.GetSummary();

        // Assert
        Assert.Equal(string.Empty, summary);
    }

    [Fact]
    public void ShouldMaintainOrderOfItems_WhenGettingSummary()
    {
        // Arrange
        var memories = new List<MemoryItem>();
        for (int i = 1; i <= 5; i++)
        {
            memories.Add(CreateMemoryItem($"Memory {i}"));
        }
        var context = MemoryContext.Build(memories, null!, null!);

        // Act
        var summary = context.GetSummary();
        var lines = summary.Split('\n');

        // Assert
        for (int i = 1; i <= 5; i++)
        {
            Assert.Contains($"- Memory {i}", lines[i]);
        }
    }

    #endregion

    #region HasContent Tests

    [Fact]
    public void ShouldReturnTrue_WhenUsingHasContentWithRecentMemories()
    {
        // Arrange
        var context = MemoryContext.Build(
            [CreateMemoryItem("Test")],
            null!,
            null!);

        // Act & Assert
        Assert.True(context.HasContent);
    }

    [Fact]
    public void ShouldReturnTrue_WhenUsingHasContentWithTaskRelatedMemories()
    {
        // Arrange
        var context = MemoryContext.Build(
            null!,
            [CreateMemoryItem("Test")],
            null!);

        // Act & Assert
        Assert.True(context.HasContent);
    }

    [Fact]
    public void ShouldReturnTrue_WhenUsingHasContentWithRelevantEntities()
    {
        // Arrange
        var context = MemoryContext.Build(
            null!,
            null!,
            [CreateEntityMemory("Test", "type")]);

        // Act & Assert
        Assert.True(context.HasContent);
    }

    [Fact]
    public void ShouldReturnFalse_WhenUsingHasContentWithAllEmpty()
    {
        // Arrange
        var context = MemoryContext.Build(null!, null!, null!);

        // Act & Assert
        Assert.False(context.HasContent);
    }

    [Fact]
    public void ShouldReturnTrue_WhenUsingHasContentWithMixedContent()
    {
        // Arrange
        var context = MemoryContext.Build(
            [CreateMemoryItem("Recent")],
            [CreateMemoryItem("Task")],
            [CreateEntityMemory("Entity", "type")]);

        // Act & Assert
        Assert.True(context.HasContent);
    }

    #endregion

    #region TotalItems Tests

    [Fact]
    public void ShouldReturnZero_WhenUsingTotalItemsWithEmptyContext()
    {
        // Arrange
        var context = MemoryContext.Build(null!, null!, null!);

        // Act & Assert
        Assert.Equal(0, context.TotalItems);
    }

    [Fact]
    public void ShouldReturnCorrectCount_WhenUsingTotalItemsWithOnlyRecentMemories()
    {
        // Arrange
        var memories = new List<MemoryItem>
        {
            CreateMemoryItem("1"),
            CreateMemoryItem("2"),
            CreateMemoryItem("3")
        };
        var context = MemoryContext.Build(memories, null!, null!);

        // Act & Assert
        Assert.Equal(3, context.TotalItems);
    }

    [Fact]
    public void ShouldReturnSumOfAll_WhenUsingTotalItemsWithAllTypes()
    {
        // Arrange
        var recent = new List<MemoryItem> { CreateMemoryItem("R1"), CreateMemoryItem("R2") };
        var taskRelated = new List<MemoryItem> { CreateMemoryItem("T1"), CreateMemoryItem("T2"), CreateMemoryItem("T3") };
        var entities = new List<EntityMemory> { CreateEntityMemory("E1", "type"), CreateEntityMemory("E2", "type") };

        var context = MemoryContext.Build(recent, taskRelated, entities);

        // Act & Assert
        Assert.Equal(7, context.TotalItems); // 2 + 3 + 2
    }

    [Fact]
    public void ShouldBeConsistentWithCollections_WhenUsingTotalItems()
    {
        // Arrange
        var context = MemoryContext.Build(
            [CreateMemoryItem("1"), CreateMemoryItem("2")],
            [CreateMemoryItem("3")],
            [CreateEntityMemory("E", "type")]);

        // Act & Assert
        var expectedTotal = context.RecentMemories.Count +
                          context.TaskRelatedMemories.Count +
                          context.RelevantEntities.Count;
        Assert.Equal(expectedTotal, context.TotalItems);
    }

    #endregion

    #region Complex Scenarios

    [Fact]
    public void ShouldRichMemoryContext_WhenUsingComplexScenario()
    {
        // Arrange - Create a rich memory context for a data processing task
        var recentMemories = new List<MemoryItem>
        {
            CreateMemoryItem("Loaded 10,000 customer records", 0.7f),
            CreateMemoryItem("Applied data validation rules", 0.8f),
            CreateMemoryItem("Found 150 validation errors", 0.9f),
            CreateMemoryItem("Generated error report", 0.6f),
            CreateMemoryItem("Sent notification to data team", 0.5f)
        };

        var taskRelatedMemories = new List<MemoryItem>
        {
            CreateMemoryItem("Previous run had 200 errors - improvement noted", 0.85f),
            CreateMemoryItem("Validation rules updated last week", 0.7f),
            CreateMemoryItem("Data quality threshold is 98%", 0.9f),
            CreateMemoryItem("Error patterns: mostly email format issues", 0.8f)
        };

        var relevantEntities = new List<EntityMemory>
        {
            CreateEntityMemory("CustomerDatabase", "datasource", "Primary customer data repository"),
            CreateEntityMemory("ValidationEngine", "tool", "Rule-based data validator"),
            CreateEntityMemory("DataTeam", "team", "Responsible for data quality"),
            CreateEntityMemory("ErrorThreshold", "policy", "Maximum 2% error rate allowed")
        };

        // Update entity attributes
        relevantEntities[0].SetAttribute("record_count", "10000");
        relevantEntities[0].SetAttribute("last_updated", DateTime.UtcNow.AddHours(-2).ToString());
        relevantEntities[1].SetAttribute("rules_count", "25");
        relevantEntities[1].SetAttribute("version", "2.3.1");

        var context = MemoryContext.Build(recentMemories, taskRelatedMemories, relevantEntities);

        // Act
        var summary = context.GetSummary();
        var hasContent = context.HasContent;
        var totalItems = context.TotalItems;

        // Assert
        Assert.True(hasContent);
        Assert.Equal(13, totalItems); // 5 + 4 + 4

        // Verify summary contains key information
        Assert.Contains("Recent context (5 items):", summary);
        Assert.Contains("Loaded 10,000 customer records", summary);
        Assert.Contains("Found 150 validation errors", summary);

        Assert.Contains("Task-related memories (4 items):", summary);
        Assert.Contains("Previous run had 200 errors", summary);
        Assert.Contains("Data quality threshold is 98%", summary);

        Assert.Contains("Relevant entities (4):", summary);
        Assert.Contains("CustomerDatabase (datasource)", summary);
        Assert.Contains("ValidationEngine (tool)", summary);

        // Verify specific memories maintained their properties
        var highImportanceMemory = recentMemories.First(m => m.Content.Contains("150 validation errors"));
        Assert.Equal(0.9f, highImportanceMemory.Importance);
    }

    [Fact]
    public void ShouldContextEvolution_WhenUsingComplexScenario()
    {
        // Arrange - Simulate building context over time
        var contexts = new List<MemoryContext>
        {
            // Initial context - empty
            MemoryContext.Build(null!, null!, null!)
        };
        ClockAdvance.UntilStrictlyAfter(contexts[^1].CreatedAt);

        // Add recent memories
        contexts.Add(MemoryContext.Build(
            [CreateMemoryItem("Started task")],
            [],
            []));
        ClockAdvance.UntilStrictlyAfter(contexts[^1].CreatedAt);

        // Add task-related memories
        contexts.Add(MemoryContext.Build(
            [CreateMemoryItem("Started task"), CreateMemoryItem("Loaded data")],
            [CreateMemoryItem("Previous results available")],
            []));
        ClockAdvance.UntilStrictlyAfter(contexts[^1].CreatedAt);

        // Full context
        contexts.Add(MemoryContext.Build(
            [
                CreateMemoryItem("Started task"),
                CreateMemoryItem("Loaded data"),
                CreateMemoryItem("Processing started")
            ],
            [
                CreateMemoryItem("Previous results available"),
                CreateMemoryItem("Best practices loaded")
            ],
            [
                CreateEntityMemory("DataProcessor", "tool"),
                CreateEntityMemory("OutputFormat", "specification")
            ]));

        // Assert - Verify context growth
        Assert.Equal(0, contexts[0].TotalItems);
        Assert.Equal(1, contexts[1].TotalItems);
        Assert.Equal(3, contexts[2].TotalItems);
        Assert.Equal(7, contexts[3].TotalItems);

        // Verify timestamps are increasing
        for (int i = 1; i < contexts.Count; i++)
        {
            Assert.True(contexts[i].CreatedAt > contexts[i - 1].CreatedAt);
        }

        // Verify content flags
        Assert.False(contexts[0].HasContent);
        Assert.True(contexts[1].HasContent);
        Assert.True(contexts[2].HasContent);
        Assert.True(contexts[3].HasContent);
    }

    [Fact]
    public void ShouldLargeMemoryContext_WhenUsingComplexScenario()
    {
        // Arrange - Create a large context to test performance and limits
        var recentMemories = new List<MemoryItem>();
        var taskRelatedMemories = new List<MemoryItem>();
        var relevantEntities = new List<EntityMemory>();

        // Add 100 recent memories
        for (int i = 0; i < 100; i++)
        {
            recentMemories.Add(CreateMemoryItem($"Recent event {i}: Processing batch {i}", i / 100f));
        }

        // Add 500 task-related memories
        for (int i = 0; i < 500; i++)
        {
            taskRelatedMemories.Add(CreateMemoryItem($"Historical data point {i}", 0.5f));
        }

        // Add 50 entities
        for (int i = 0; i < 50; i++)
        {
            var entity = CreateEntityMemory($"Entity{i}", i % 2 == 0 ? "tool" : "datasource", $"Description for entity {i}");
            entity.SetAttribute("index", i.ToString());
            entity.SetAttribute("active", (i % 3 == 0).ToString());
            relevantEntities.Add(entity);
        }

        // Act
        var context = MemoryContext.Build(recentMemories, taskRelatedMemories, relevantEntities);
        var summary = context.GetSummary();

        // Assert
        Assert.Equal(650, context.TotalItems);
        Assert.True(context.HasContent);

        // Verify summary handles large counts correctly
        Assert.Contains("Recent context (100 items):", summary);
        Assert.Contains("Task-related memories (500 items):", summary);
        Assert.Contains("Relevant entities (50):", summary);

        // Verify all items are included in summary
        var summaryLines = summary.Split('\n');
        Assert.True(summaryLines.Length > 650); // At least one line per item plus headers

        // Verify collections are properly isolated
        Assert.Equal(100, context.RecentMemories.Count);
        Assert.Equal(500, context.TaskRelatedMemories.Count);
        Assert.Equal(50, context.RelevantEntities.Count);
    }

    #endregion
}
