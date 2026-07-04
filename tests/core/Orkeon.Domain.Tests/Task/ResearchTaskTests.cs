using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Domain.Task.Contexts;
using Orkeon.Domain.Task;
using Orkeon.Domain.Common;
using DomainTask = Orkeon.Domain.Task;
using Orkeon.Domain.Tests.Fixtures;

namespace Orkeon.Domain.Tests.Task;

public class ResearchTaskTests
{
    // Test doubles
    private class TestTaskCallback : ITaskCallback
    {
        public List<string> CallbackEvents { get; } = [];

        public System.Threading.Tasks.Task OnTaskStartAsync(ICrewTask task)
        {
            CallbackEvents.Add($"Started: {task.TaskId}");
            return System.Threading.Tasks.Task.CompletedTask;
        }

        public System.Threading.Tasks.Task OnTaskCompletedAsync(ICrewTask task, TaskOutput output)
        {
            CallbackEvents.Add($"Completed: {task.TaskId}");
            return System.Threading.Tasks.Task.CompletedTask;
        }

        public System.Threading.Tasks.Task OnTaskFailedAsync(ICrewTask task, string error)
        {
            CallbackEvents.Add($"Failed: {task.TaskId} - {error}");
            return System.Threading.Tasks.Task.CompletedTask;
        }
    }

    [Fact]
    public void ShouldInitializeTask_WhenConstructingWithValidParameters()
    {
        // Arrange
        var description = TaskDescription.From("Research AI ethics implications");
        var expectedOutput = ExpectedOutput.From("Comprehensive research report");
        var researchTopic = "AI Ethics and Society";

        // Act
        var task = DomainTask.ResearchTask.Create(
            TaskId.Create(),
            description,
            expectedOutput,
            researchTopic);

        // Assert
        Assert.NotNull(task);
        Assert.Equal(description, task.Description);
        Assert.Equal(expectedOutput, task.ExpectedOutput);
        Assert.NotNull(task.Context);
        var context = task.TypedContext;
        Assert.Equal(researchTopic, context.Topic);
        Assert.Equal(Orkeon.Domain.Task.ValueObjects.TaskStatus.Pending, task.Status);
        Assert.NotEqual(default(TaskId), task.Id);
        Assert.NotNull(context.Sources);
        Assert.NotNull(context.KeyFindings);
        Assert.NotNull(context.SearchQueries);
        Assert.NotNull(context.References);
    }

    [Fact]
    public void ShouldInitializeCorrectly_WhenConstructingWithAllOptionalParameters()
    {
        // Arrange
        var description = TaskDescription.From("Research quantum computing");
        var expectedOutput = ExpectedOutput.From("Technical analysis report");
        var researchTopic = "Quantum Computing Applications";
        var priority = TaskPriority.High;
        var asyncExecution = true;
        var outputJson = JsonSchema.From("{\"type\":\"object\",\"properties\":{\"findings\":{\"type\":\"array\"}}}");
        var outputPydantic = typeof(ResearchTaskContext);
        var outputFile = "research_report.md";
        var callback = new TestTaskCallback();
        var humanInput = true;

        // Act
        var task = DomainTask.ResearchTask.Create(
            TaskId.Create(),
            description,
            expectedOutput,
            researchTopic,
            priority,
            new TaskOutputOptions
            {
                AsyncExecution = asyncExecution,
                OutputJson = outputJson,
                OutputPydantic = outputPydantic,
                OutputFile = outputFile,
                Callback = callback,
                HumanInput = humanInput
            });

        // Assert
        Assert.Equal(priority, task.Priority);
        Assert.Equal(asyncExecution, task.AsyncExecution);
        Assert.Equal(outputJson, task.OutputJson);
        Assert.Equal(outputPydantic, task.OutputPydantic);
        Assert.Equal(outputFile, task.OutputFile);
        Assert.Equal(callback, task.Callback);
        Assert.Equal(humanInput, task.HumanInput);
        Assert.Equal(researchTopic, task.TypedContext.Topic);
    }

    [Fact]
    public void ShouldStillCreateTask_WhenConstructingWithEmptyTopic()
    {
        // Arrange
        var description = TaskDescription.From("General research");
        var expectedOutput = ExpectedOutput.From("Research findings");

        // Act
        var task = DomainTask.ResearchTask.Create(TaskId.Create(), description, expectedOutput, "");

        // Assert
        Assert.NotNull(task);
        Assert.Equal("", task.TypedContext.Topic);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructingWithNullDescription()
    {
        // Arrange
        var expectedOutput = ExpectedOutput.From("Research output");
        var researchTopic = "Climate Change";

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => DomainTask.ResearchTask.Create(TaskId.Create(), null!, expectedOutput, researchTopic));
        Assert.Equal("description", exception.ParamName);
    }

    [Fact]
    public void ShouldAddToContext_WhenAddingSourceWithValidSource()
    {
        // Arrange
        var task = DomainTask.ResearchTask.Create(
            TaskId.Create(),
            TaskDescription.From("Research blockchain"),
            ExpectedOutput.From("Blockchain analysis"),
            "Blockchain Technology");
        var source = "https://blockchain.research.org/paper1.pdf";
        var relevance = 0.85f;

        // Act
        task.AddSource(source, relevance);

        // Assert
        var context = task.TypedContext;
        Assert.Contains(source, context.Sources);
        Assert.Equal(relevance, context.RelevanceScores[source]);
    }

    [Fact]
    public void ShouldUseDefaultValue_WhenAddingSourceWithDefaultRelevance()
    {
        // Arrange
        var task = DomainTask.ResearchTask.Create(
            TaskId.Create(),
            TaskDescription.From("Research ML algorithms"),
            ExpectedOutput.From("Algorithm comparison"),
            "Machine Learning");
        var source = "https://ml-papers.org/study.pdf";

        // Act
        task.AddSource(source);

        // Assert
        var context = task.TypedContext;
        Assert.Contains(source, context.Sources);
        Assert.Equal(1.0f, context.RelevanceScores[source]);
    }

    [Fact]
    public void ShouldClamp_WhenAddingSourceWithOutOfRangeRelevance()
    {
        // Arrange
        var task = DomainTask.ResearchTask.Create(
            TaskId.Create(),
            TaskDescription.From("Research topic"),
            ExpectedOutput.From("Output"),
            "Topic");

        // Act
        task.AddSource("source1", -0.5f);
        task.AddSource("source2", 1.5f);

        // Assert
        var context = task.TypedContext;
        Assert.Equal(0.0f, context.RelevanceScores["source1"]);
        Assert.Equal(1.0f, context.RelevanceScores["source2"]);
    }

    [Fact]
    public void ShouldAddToContext_WhenAddingKeyFinding()
    {
        // Arrange
        var task = DomainTask.ResearchTask.Create(
            TaskId.Create(),
            TaskDescription.From("Research security vulnerabilities"),
            ExpectedOutput.From("Security report"),
            "Cybersecurity");
        var finding = "SQL injection remains the most common web vulnerability";

        // Act
        task.AddKeyFinding(finding);

        // Assert
        Assert.Contains(finding, task.TypedContext.KeyFindings);
    }

    [Fact]
    public void ShouldAddAll_WhenAddingKeyFindingWithMultiple()
    {
        // Arrange
        var task = DomainTask.ResearchTask.Create(
            TaskId.Create(),
            TaskDescription.From("Research data privacy"),
            ExpectedOutput.From("Privacy analysis"),
            "Data Privacy Regulations");
        var findings = new[]
        {
            "GDPR has increased privacy awareness",
            "60% of companies now have dedicated privacy officers",
            "Data breaches cost average $4.35M in 2022"
        };

        // Act
        foreach (var finding in findings)
        {
            task.AddKeyFinding(finding);
        }

        // Assert
        var context = task.TypedContext;
        Assert.Equal(findings.Length, context.KeyFindings.Count);
        foreach (var finding in findings)
        {
            Assert.Contains(finding, context.KeyFindings);
        }
    }

    [Fact]
    public void ShouldAddToContext_WhenAddingSearchQuery()
    {
        // Arrange
        var task = DomainTask.ResearchTask.Create(
            TaskId.Create(),
            TaskDescription.From("Research renewable energy"),
            ExpectedOutput.From("Energy report"),
            "Renewable Energy Sources");
        var query = "solar panel efficiency 2023";

        // Act
        task.AddSearchQuery(query);

        // Assert
        Assert.Contains(query, task.TypedContext.SearchQueries);
    }

    [Fact]
    public void ShouldAddToContext_WhenAddingReferenceWithValidReference()
    {
        // Arrange
        var task = DomainTask.ResearchTask.Create(
            TaskId.Create(),
            TaskDescription.From("Research AI advancements"),
            ExpectedOutput.From("AI progress report"),
            "Artificial Intelligence");
        var reference = new ResearchReference
        {
            Title = "Attention Is All You Need",
            Url = new Uri("https://arxiv.org/abs/1706.03762"),
            Author = "Vaswani et al.",
            PublishedDate = new DateTime(2017, 6, 12),
            Summary = "Introduced the Transformer architecture",
            Relevance = 0.95f
        };

        // Act
        task.AddReference(reference);

        // Assert
        var context = task.TypedContext;
        Assert.Single(context.References);
        var addedRef = context.References[0];
        Assert.Equal(reference.Title, addedRef.Title);
        Assert.Equal(reference.Url, addedRef.Url);
        Assert.Equal(reference.Author, addedRef.Author);
        Assert.Equal(reference.PublishedDate, addedRef.PublishedDate);
        Assert.Equal(reference.Summary, addedRef.Summary);
        Assert.Equal(reference.Relevance, addedRef.Relevance);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenAddingReferenceWithNullReference()
    {
        // Arrange
        var task = DomainTask.ResearchTask.Create(
            TaskId.Create(),
            TaskDescription.From("Research topic"),
            ExpectedOutput.From("Output"),
            "Topic");

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => task.AddReference(null!));
        Assert.Equal("reference", exception.ParamName);
    }

    [Fact]
    public void ShouldReturnSourcesByRelevance_WhenGettingTopSources()
    {
        // Arrange
        var task = DomainTask.ResearchTask.Create(
            TaskId.Create(),
            TaskDescription.From("Research cloud computing"),
            ExpectedOutput.From("Cloud analysis"),
            "Cloud Computing Trends");

        task.AddSource("source1", 0.3f);
        task.AddSource("source2", 0.9f);
        task.AddSource("source3", 0.6f);
        task.AddSource("source4", 0.8f);
        task.AddSource("source5", 0.4f);
        task.AddSource("source6", 0.7f);

        // Act
        var topSources = task.GetTopSources(3).ToList();

        // Assert
        Assert.Equal(3, topSources.Count);
        Assert.Equal("source2", topSources[0]); // 0.9
        Assert.Equal("source4", topSources[1]); // 0.8
        Assert.Equal("source6", topSources[2]); // 0.7
    }

    [Fact]
    public void ShouldReturnAllSources_WhenGettingTopSourcesWithFewerSourcesThanRequested()
    {
        // Arrange
        var task = DomainTask.ResearchTask.Create(
            TaskId.Create(),
            TaskDescription.From("Research topic"),
            ExpectedOutput.From("Output"),
            "Topic");

        task.AddSource("source1", 0.5f);
        task.AddSource("source2", 0.7f);

        // Act
        var topSources = task.GetTopSources(5).ToList();

        // Assert
        Assert.Equal(2, topSources.Count);
    }

    [Fact]
    public void ShouldReturnFormattedSummary_WhenGettingContextSummary()
    {
        // Arrange
        var task = DomainTask.ResearchTask.Create(
            TaskId.Create(),
            TaskDescription.From("Research IoT security"),
            ExpectedOutput.From("IoT security analysis"),
            "Internet of Things Security");

        task.AddSource("source1", 0.8f);
        task.AddSource("source2", 0.6f);
        task.AddKeyFinding("Finding 1");
        task.AddKeyFinding("Finding 2");
        task.AddKeyFinding("Finding 3");

        // Act
        var summary = task.GetContextSummary();

        // Assert
        Assert.Contains("Research on 'Internet of Things Security'", summary);
        Assert.Contains("2 sources", summary);
        Assert.Contains("3 findings", summary);
    }

    [Fact]
    public void ShouldMaintainAllContext_WhenUsingCompleteResearchTask()
    {
        // Arrange
        var task = DomainTask.ResearchTask.Create(
            TaskId.Create(),
            TaskDescription.From("Research distributed systems"),
            ExpectedOutput.From("Distributed systems analysis"),
            "Distributed Computing");

        // Add various research data
        task.AddSource("https://papers.dist-sys.org/paper1", 0.9f);
        task.AddSource("https://papers.dist-sys.org/paper2", 0.85f);
        task.AddKeyFinding("CAP theorem remains fundamental");
        task.AddKeyFinding("Consensus algorithms are critical");
        task.AddSearchQuery("distributed consensus algorithms");
        task.AddSearchQuery("byzantine fault tolerance");
        task.AddReference(new ResearchReference
        {
            Title = "Time, Clocks, and the Ordering of Events",
            Author = "Leslie Lamport",
            Url = new Uri("https://lamport.azurewebsites.net/pubs/time-clocks.pdf"),
            PublishedDate = new DateTime(1978, 7, 1),
            Summary = "Fundamental paper on distributed systems",
            Relevance = 1.0f
        });

        // Assign and start task
        task.AssignTo(Orkeon.Domain.Common.AgentId.Create());
        task.Start(task.AssignedAgent!);

        // Complete task
        task.Complete(task.AssignedAgent!, TaskOutput.Text("Research completed with comprehensive findings"));

        // Assert
        var context = task.TypedContext;
        Assert.Equal(2, context.Sources.Count);
        Assert.Equal(2, context.KeyFindings.Count);
        Assert.Equal(2, context.SearchQueries.Count);
        Assert.Single(context.References);
        Assert.Equal(Orkeon.Domain.Task.ValueObjects.TaskStatus.Completed, task.Status);
    }

    [Fact]
    public void ShouldTrackAll_WhenUsingResearchTaskWithMultipleReferences()
    {
        // Arrange
        var task = DomainTask.ResearchTask.Create(
            TaskId.Create(),
            TaskDescription.From("Research neural networks"),
            ExpectedOutput.From("Neural network survey"),
            "Deep Learning Architectures");

        var references = new[]
        {
            new ResearchReference
            {
                Title = "ImageNet Classification with Deep CNNs",
                Author = "Krizhevsky et al.",
                Url = new Uri("https://papers.nips.cc/paper/4824"),
                PublishedDate = new DateTime(2012, 1, 1),
                Summary = "AlexNet breakthrough",
                Relevance = 0.9f
            },
            new ResearchReference
            {
                Title = "Deep Residual Learning",
                Author = "He et al.",
                Url = new Uri("https://arxiv.org/abs/1512.03385"),
                PublishedDate = new DateTime(2015, 12, 10),
                Summary = "ResNet architecture",
                Relevance = 0.95f
            },
            new ResearchReference
            {
                Title = "BERT: Pre-training of Deep Bidirectional Transformers",
                Author = "Devlin et al.",
                Url = new Uri("https://arxiv.org/abs/1810.04805"),
                PublishedDate = new DateTime(2018, 10, 11),
                Summary = "Transformer-based language model",
                Relevance = 0.98f
            }
        };

        // Act
        foreach (var reference in references)
        {
            task.AddReference(reference);
        }

        // Assert
        var context = task.TypedContext;
        Assert.Equal(references.Length, context.References.Count);
        foreach (var reference in references)
        {
            Assert.Contains(context.References, r => r.Title == reference.Title);
        }
    }

    [Fact]
    public void ShouldTrackProperly_WhenUsingResearchTaskUsingTimeline()
    {
        // Arrange
        var task = DomainTask.ResearchTask.Create(
            TaskId.Create(),
            TaskDescription.From("Research time-sensitive topic"),
            ExpectedOutput.From("Time analysis"),
            "Real-time Systems");

        // Act - Get initial research started time
        var initialStartTime = task.TypedContext.ResearchStarted;

        // Wait for the clock to advance, then add data (deterministic, R5.6)
        ClockAdvance.UntilStrictlyAfter(initialStartTime);
        task.AddSource("source1", 0.8f);
        task.AddKeyFinding("Real-time constraints are critical");

        // Assert
        var context = task.TypedContext;
        Assert.True(context.ResearchStarted <= DateTime.UtcNow);
        Assert.Equal(initialStartTime, context.ResearchStarted); // Should not change
    }

    [Fact]
    public void ShouldHaveEmptyCollections_WhenUsingEmptyResearchTask()
    {
        // Arrange & Act
        var task = DomainTask.ResearchTask.Create(
            TaskId.Create(),
            TaskDescription.From("New research"),
            ExpectedOutput.From("To be determined"),
            "Unexplored Topic");

        // Assert
        var context = task.TypedContext;
        Assert.Empty(context.Sources);
        Assert.Empty(context.KeyFindings);
        Assert.Empty(context.SearchQueries);
        Assert.Empty(context.References);
        Assert.Empty(context.RelevanceScores);
    }
}
