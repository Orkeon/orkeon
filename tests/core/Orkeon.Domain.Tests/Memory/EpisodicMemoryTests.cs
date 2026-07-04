using Orkeon.Domain.Common;
using Orkeon.Domain.Memory;
using Orkeon.Domain.Memory.ValueObjects;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;
using Orkeon.Domain.Tests.Fixtures;

namespace Orkeon.Domain.Tests.Memory;

public class EpisodicMemoryTests
{
    private static readonly string[] AiCategories = ["LLMs", "Vision", "RL", "Ethics", "Efficiency"];
    private static readonly string[] s_dataTools = ["DataExtractor", "DataTransformer", "DataValidator"];
    private static readonly string[] s_collaborators = ["Research Agent", "Analysis Agent", "Writing Agent"];

    #region Constructor Tests

    [Fact]
    public void ShouldCreateEpisodicMemory_WhenConstructingWithValidParameters()
    {
        // Arrange
        var title = "Task Completion Episode";
        var agentId = AgentId.From(Guid.NewGuid());
        var taskId = TaskId.From(Guid.NewGuid());

        // Act
        var episode = EpisodicMemory.Create(title, agentId, taskId);

        // Assert
        Assert.NotNull(episode.Id);
        Assert.NotNull(episode.Id); // ID is ULID-based EntityId
        Assert.Equal(title, episode.Title);
        Assert.Equal(agentId, episode.AgentId);
        Assert.Equal(taskId, episode.TaskId);
        Assert.Empty(episode.Events);
        Assert.Empty(episode.LessonsLearned);
        Assert.Equal(EpisodeOutcome.InProgress, episode.Outcome);
        Assert.True(episode.StartedAt <= DateTime.UtcNow);
        Assert.Null(episode.CompletedAt);
        Assert.Null(episode.Duration);
        Assert.NotNull(episode.Metadata);
    }

    [Fact]
    public void ShouldCreateEpisodicMemory_WhenConstructingWithoutTaskId()
    {
        // Arrange
        var title = "General Episode";
        var agentId = AgentId.From(Guid.NewGuid());

        // Act
        var episode = EpisodicMemory.Create(title, agentId);

        // Assert
        Assert.Equal(title, episode.Title);
        Assert.Equal(agentId, episode.AgentId);
        Assert.Null(episode.TaskId);
    }

    [Fact]
    public void ShouldThrow_WhenConstructingWithEmptyTitle()
    {
        // Arrange
        var agentId = AgentId.From(Guid.NewGuid());

        // Act & Assert
        var exception = Assert.ThrowsAny<ArgumentException>(() =>
            EpisodicMemory.Create("", agentId));
        Assert.Equal("title", exception.ParamName);
    }

    [Fact]
    public void ShouldThrow_WhenConstructingWithWhitespaceTitle()
    {
        // Arrange
        var agentId = AgentId.From(Guid.NewGuid());

        // Act & Assert
        var exception = Assert.ThrowsAny<ArgumentException>(() =>
            EpisodicMemory.Create("   ", agentId));
    }

    [Fact]
    public void ShouldThrow_WhenConstructingWithNullAgentId()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            EpisodicMemory.Create("Title", null!));
        Assert.Equal("agentId", exception.ParamName);
    }

    [Fact]
    public void ShouldGenerateUniqueId_WhenConstructing()
    {
        // Arrange
        var agentId = AgentId.From(Guid.NewGuid());

        // Act
        var episode1 = EpisodicMemory.Create("Episode 1", agentId);
        var episode2 = EpisodicMemory.Create("Episode 2", agentId);

        // Assert
        Assert.NotEqual(episode1.Id, episode2.Id);
        Assert.NotNull(episode1.Id); // ID is ULID-based EntityId
        Assert.NotNull(episode2.Id); // ID is ULID-based EntityId
    }

    #endregion

    #region AddEvent Tests

    [Fact]
    public void ShouldAdd_WhenUsingAddEventWithValidEvent()
    {
        // Arrange
        var episode = EpisodicMemory.Create("Test Episode", AgentId.From(Guid.NewGuid()));
        var episodeEvent = EpisodeEvent.Create("action", "Performed calculation");

        // Act
        episode.AddEvent(episodeEvent);

        // Assert
        Assert.Single(episode.Events);
        Assert.Contains(episodeEvent, episode.Events);
    }

    [Fact]
    public void ShouldThrow_WhenUsingAddEventWithNullEvent()
    {
        // Arrange
        var episode = EpisodicMemory.Create("Test Episode", AgentId.From(Guid.NewGuid()));

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            episode.AddEvent(null!));
        Assert.Equal("episodeEvent", exception.ParamName);
    }

    [Fact]
    public void ShouldThrow_WhenUsingAddEventToCompletedEpisode()
    {
        // Arrange
        var episode = EpisodicMemory.Create("Test Episode", AgentId.From(Guid.NewGuid()));
        episode.Complete(EpisodeOutcome.Success);
        var episodeEvent = EpisodeEvent.Create("action", "Late event");

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
            episode.AddEvent(episodeEvent));
        Assert.Contains("Cannot add events to a completed episode", exception.Message);
    }

    [Fact]
    public void ShouldMaintainOrder_WhenUsingAddEventWithMultipleEvents()
    {
        // Arrange
        var episode = EpisodicMemory.Create("Test Episode", AgentId.From(Guid.NewGuid()));
        var events = new List<EpisodeEvent>();

        // Act
        for (int i = 0; i < 5; i++)
        {
            var evt = EpisodeEvent.Create("action", $"Action {i}");
            events.Add(evt);
            episode.AddEvent(evt);
            ClockAdvance.Tick(); // Ensure different timestamps (R5.6)
        }

        // Assert
        Assert.Equal(5, episode.Events.Count);
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(events[i], episode.Events[i]);
        }
    }

    #endregion

    #region Complete Tests

    [Fact]
    public void ShouldComplete_WhenCompletingWithSuccessOutcome()
    {
        // Arrange
        var episode = EpisodicMemory.Create("Test Episode", AgentId.From(Guid.NewGuid()));
        episode.AddEvent(EpisodeEvent.Create("action", "Performed task"));

        // Act
        episode.Complete(EpisodeOutcome.Success);

        // Assert
        Assert.Equal(EpisodeOutcome.Success, episode.Outcome);
        Assert.NotNull(episode.CompletedAt);
        Assert.NotNull(episode.Duration);
        Assert.True(episode.Duration.Value.TotalMilliseconds >= 0);
    }

    [Fact]
    public void ShouldAddLessons_WhenCompletingWithLessonsLearned()
    {
        // Arrange
        var episode = EpisodicMemory.Create("Test Episode", AgentId.From(Guid.NewGuid()));
        var lessons = new List<string>
        {
            "Always validate input",
            "Cache expensive operations",
            "Log important decisions"
        };

        // Act
        episode.Complete(EpisodeOutcome.Success, lessons);

        // Assert
        Assert.Equal(3, episode.LessonsLearned.Count);
        Assert.All(lessons, lesson => Assert.Contains(lesson, episode.LessonsLearned));
    }

    [Fact]
    public void ShouldThrow_WhenCompletingAlreadyCompletedEpisode()
    {
        // Arrange
        var episode = EpisodicMemory.Create("Test Episode", AgentId.From(Guid.NewGuid()));
        episode.Complete(EpisodeOutcome.Success);

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
            episode.Complete(EpisodeOutcome.Failure));
        Assert.Contains("Episode is already completed", exception.Message);
    }

    [Fact]
    public void ShouldSetCorrectly_WhenCompletingWithDifferentOutcomes()
    {
        // Arrange
        var outcomes = new[]
        {
            EpisodeOutcome.Success,
            EpisodeOutcome.PartialSuccess,
            EpisodeOutcome.Failure,
            EpisodeOutcome.Cancelled
        };

        // Act & Assert
        foreach (var outcome in outcomes)
        {
            var episode = EpisodicMemory.Create($"Test {outcome}", AgentId.From(Guid.NewGuid()));
            episode.Complete(outcome);
            Assert.Equal(outcome, episode.Outcome);
        }
    }

    [Fact]
    public void ShouldCalculateDurationCorrectly_WhenCompleting()
    {
        // Arrange
        var episode = EpisodicMemory.Create("Test Episode", AgentId.From(Guid.NewGuid()));
        var startTime = episode.StartedAt;
        // Wait >= 100 ms on the same clock the Duration is computed from (deterministic, R5.6)
        ClockAdvance.Until(() => DateTime.UtcNow - startTime >= TimeSpan.FromMilliseconds(100));

        // Act
        episode.Complete(EpisodeOutcome.Success);

        // Assert
        Assert.NotNull(episode.Duration);
        Assert.True(episode.Duration.Value.TotalMilliseconds >= 100);
        Assert.True(episode.CompletedAt > startTime);
    }

    #endregion

    #region AddLessonLearned Tests

    [Fact]
    public void ShouldAdd_WhenAddingLessonLearnedWithValidLesson()
    {
        // Arrange
        var episode = EpisodicMemory.Create("Test Episode", AgentId.From(Guid.NewGuid()));
        var lesson = "Always check null references";

        // Act
        episode.AddLessonLearned(lesson);

        // Assert
        Assert.Single(episode.LessonsLearned);
        Assert.Contains(lesson, episode.LessonsLearned);
    }

    [Fact]
    public void ShouldThrow_WhenAddingLessonLearnedWithEmptyLesson()
    {
        // Arrange
        var episode = EpisodicMemory.Create("Test Episode", AgentId.From(Guid.NewGuid()));

        // Act & Assert
        var exception = Assert.ThrowsAny<ArgumentException>(() =>
            episode.AddLessonLearned(""));
        Assert.Equal("lesson", exception.ParamName);
    }

    [Fact]
    public void ShouldThrow_WhenAddingLessonLearnedWithWhitespaceLesson()
    {
        // Arrange
        var episode = EpisodicMemory.Create("Test Episode", AgentId.From(Guid.NewGuid()));

        // Act & Assert
        var exception = Assert.ThrowsAny<ArgumentException>(() =>
            episode.AddLessonLearned("   "));
    }

    [Fact]
    public void ShouldMaintainAll_WhenAddingLessonLearnedWithMultipleLessons()
    {
        // Arrange
        var episode = EpisodicMemory.Create("Test Episode", AgentId.From(Guid.NewGuid()));
        var lessons = new[]
        {
            "Lesson 1: Validate early",
            "Lesson 2: Handle errors gracefully",
            "Lesson 3: Log important events"
        };

        // Act
        foreach (var lesson in lessons)
        {
            episode.AddLessonLearned(lesson);
        }

        // Assert
        Assert.Equal(3, episode.LessonsLearned.Count);
        Assert.All(lessons, lesson => Assert.Contains(lesson, episode.LessonsLearned));
    }

    #endregion

    #region UpdateMetadata Tests

    [Fact]
    public void ShouldUpdate_WhenUsingUpdateMetadataWithValidMetadata()
    {
        // Arrange
        var episode = EpisodicMemory.Create("Test Episode", AgentId.From(Guid.NewGuid()));
        var metadata = EpisodicMemoryMetadata.CreateBuilder()
            .AddContext("Production environment")
            .AddSuccessScore(0.95)
            .Build();

        // Act
        episode.UpdateMetadata(metadata);

        // Assert
        Assert.Equal(metadata, episode.Metadata);
    }

    [Fact]
    public void ShouldThrow_WhenUsingUpdateMetadataWithNullMetadata()
    {
        // Arrange
        var episode = EpisodicMemory.Create("Test Episode", AgentId.From(Guid.NewGuid()));

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            episode.UpdateMetadata(null!));
        Assert.Equal("metadata", exception.ParamName);
    }

    [Fact]
    public void ShouldKeepLatest_WhenUsingUpdateMetadataWithMultipleUpdates()
    {
        // Arrange
        var episode = EpisodicMemory.Create("Test Episode", AgentId.From(Guid.NewGuid()));
        var metadata1 = EpisodicMemoryMetadata.CreateBuilder()
            .AddContext("Test environment")
            .Build();
        var metadata2 = EpisodicMemoryMetadata.CreateBuilder()
            .AddContext("Production environment")
            .AddSuccessScore(0.85)
            .Build();

        // Act
        episode.UpdateMetadata(metadata1);
        episode.UpdateMetadata(metadata2);

        // Assert
        Assert.Equal(metadata2, episode.Metadata);
        Assert.Equal("Production environment", episode.Metadata.Get<string>("context"));
        Assert.Equal(0.85, episode.Metadata.Get<double>("success_score"));
    }

    #endregion

    #region EpisodeEvent Tests

    [Fact]
    public void ShouldCreate_WhenUsingEpisodeEventUsingConstructorWithValidParameters()
    {
        // Arrange
        var type = "decision";
        var description = "Decided to use caching strategy";
        var data = EpisodeEventData.CreateBuilder()
            .AddInput("cache_size=1000")
            .AddOutput("cache_initialized")
            .Build();

        // Act
        var episodeEvent = EpisodeEvent.Create(type, description, data);

        // Assert
        Assert.Equal(type, episodeEvent.Type);
        Assert.Equal(description, episodeEvent.Description);
        Assert.Equal(data, episodeEvent.Data);
        Assert.True(episodeEvent.Timestamp <= DateTime.UtcNow);
    }

    [Fact]
    public void ShouldUseEmptyData_WhenUsingEpisodeEventUsingConstructorWithoutData()
    {
        // Act
        var episodeEvent = EpisodeEvent.Create("action", "Performed action");

        // Assert
        Assert.NotNull(episodeEvent.Data);
        Assert.Equal(EpisodeEventData.Empty, episodeEvent.Data);
    }

    [Fact]
    public void ShouldThrow_WhenUsingEpisodeEventUsingConstructorWithEmptyType()
    {
        // Act & Assert
        var exception = Assert.ThrowsAny<ArgumentException>(() =>
            EpisodeEvent.Create("", "Description"));
        Assert.Equal("type", exception.ParamName);
    }

    [Fact]
    public void ShouldThrow_WhenUsingEpisodeEventUsingConstructorWithEmptyDescription()
    {
        // Act & Assert
        var exception = Assert.ThrowsAny<ArgumentException>(() =>
            EpisodeEvent.Create("type", ""));
        Assert.Equal("description", exception.ParamName);
    }

    [Fact]
    public void ShouldSetTimestamp_WhenUsingEpisodeEventUsingConstructor()
    {
        // Arrange
        var before = DateTime.UtcNow;

        // Act
        var episodeEvent = EpisodeEvent.Create("type", "description");
        var after = DateTime.UtcNow;

        // Assert
        Assert.True(episodeEvent.Timestamp >= before);
        Assert.True(episodeEvent.Timestamp <= after);
    }

    #endregion

    #region EpisodeOutcome Enum Tests

    [Fact]
    public void ShouldHaveExpectedValues_WhenUsingEpisodeOutcome()
    {
        // Assert
        Assert.Equal(0, (int)EpisodeOutcome.InProgress);
        Assert.Equal(1, (int)EpisodeOutcome.Success);
        Assert.Equal(2, (int)EpisodeOutcome.PartialSuccess);
        Assert.Equal(3, (int)EpisodeOutcome.Failure);
        Assert.Equal(4, (int)EpisodeOutcome.Cancelled);
    }

    [Fact]
    public void ShouldBeDefined_WhenUsingEpisodeOutcomeWithAllValues()
    {
        // Arrange
        var allValues = Enum.GetValues<EpisodeOutcome>();

        // Act & Assert
        Assert.Equal(5, allValues.Length);
        Assert.Contains(EpisodeOutcome.InProgress, allValues);
        Assert.Contains(EpisodeOutcome.Success, allValues);
        Assert.Contains(EpisodeOutcome.PartialSuccess, allValues);
        Assert.Contains(EpisodeOutcome.Failure, allValues);
        Assert.Contains(EpisodeOutcome.Cancelled, allValues);
    }

    #endregion

    #region Complex Scenarios

    [Fact]
    public void ShouldCompleteTaskExecutionEpisode_WhenUsingComplexScenario()
    {
        // Arrange
        var agentId = AgentId.From(Guid.NewGuid());
        var taskId = TaskId.From(Guid.NewGuid());
        var episode = EpisodicMemory.Create("Data Processing Task", agentId, taskId);

        // Update metadata with context
        var metadata = EpisodicMemoryMetadata.CreateBuilder()
            .AddContext("Processing customer data export")
            .AddEnvironment("production")
            .AddToolsUsed(s_dataTools)
            .AddIterationCount(3)
            .Build();
        episode.UpdateMetadata(metadata);

        // Act - Simulate task execution events
        // Event 1: Initial decision
        episode.AddEvent(EpisodeEvent.Create("decision", "Analyzed task requirements and chose batch processing approach",
            EpisodeEventData.CreateBuilder()
                .AddInput("10GB customer data file")
                .AddOutput("batch_size=1000")
                .AddConfidence(0.85)
                .Build()));

        ClockAdvance.Tick();

        // Event 2: Data extraction
        episode.AddEvent(EpisodeEvent.Create("action", "Extracted data from source system",
            EpisodeEventData.CreateBuilder()
                .AddToolName("DataExtractor")
                .AddDuration(TimeoutStandard)
                .AddOutput("Extracted 1M records")
                .Build()));

        ClockAdvance.Tick();

        // Event 3: Transformation
        episode.AddEvent(EpisodeEvent.Create("action", "Transformed data to target format",
            EpisodeEventData.CreateBuilder()
                .AddToolName("DataTransformer")
                .AddDuration(TimeoutLong)
                .AddOutput("Transformed 1M records, 50 errors")
                .Build()));

        ClockAdvance.Tick();

        // Event 4: Error handling
        episode.AddEvent(EpisodeEvent.Create("observation", "Detected transformation errors",
            EpisodeEventData.CreateBuilder()
                .AddError("50 records failed validation")
                .AddAgentRole("Data Quality Analyst")
                .Build()));

        ClockAdvance.Tick();

        // Event 5: Reflection
        episode.AddEvent(EpisodeEvent.Create("reflection", "Error rate within acceptable threshold",
            EpisodeEventData.CreateBuilder()
                .AddConfidence(0.95)
                .Add("error_rate", 0.005)
                .Build()));

        // Complete with partial success and lessons
        episode.Complete(EpisodeOutcome.PartialSuccess,
        [
            "Implement pre-validation to catch errors earlier",
            "Batch size of 1000 is optimal for this data volume",
            "Error threshold of 0.5% is acceptable for production"
        ]);

        // Assert
        Assert.Equal("Data Processing Task", episode.Title);
        Assert.Equal(agentId, episode.AgentId);
        Assert.Equal(taskId, episode.TaskId);
        Assert.Equal(5, episode.Events.Count);
        Assert.Equal(3, episode.LessonsLearned.Count);
        Assert.Equal(EpisodeOutcome.PartialSuccess, episode.Outcome);
        Assert.NotNull(episode.Duration);

        // Verify event sequence
        Assert.Equal("decision", episode.Events[0].Type);
        Assert.Equal("action", episode.Events[1].Type);
        Assert.Equal("action", episode.Events[2].Type);
        Assert.Equal("observation", episode.Events[3].Type);
        Assert.Equal("reflection", episode.Events[4].Type);

        // Verify metadata
        Assert.Equal("production", episode.Metadata.Get<string>("environment"));
        Assert.Equal(3, episode.Metadata.Get<int>("iteration_count"));
        var tools = episode.Metadata.Get<string[]>("tools_used");
        Assert.Equal(3, tools!.Length);
    }

    [Fact]
    public void ShouldFailedEpisodeWithRetries_WhenUsingComplexScenario()
    {
        // Arrange
        var agentId = AgentId.From(Guid.NewGuid());
        var episode = EpisodicMemory.Create("API Integration Attempt", agentId);

        // Act - Simulate failed integration with retries
        // Attempt 1
        episode.AddEvent(EpisodeEvent.Create("action", "First API call attempt",
            EpisodeEventData.CreateBuilder()
                .AddInput("POST /api/data")
                .AddDuration(TimeoutQuick)
                .AddError("Connection timeout")
                .Build()));

        // Attempt 2
        episode.AddEvent(EpisodeEvent.Create("action", "Retry with increased timeout",
            EpisodeEventData.CreateBuilder()
                .AddInput("POST /api/data with 60s timeout")
                .AddDuration(TimeSpan.FromSeconds(60))
                .AddError("503 Service Unavailable")
                .Build()));

        // Attempt 3
        episode.AddEvent(EpisodeEvent.Create("action", "Final retry with exponential backoff",
            EpisodeEventData.CreateBuilder()
                .AddInput("POST /api/data with 120s timeout")
                .AddDuration(TimeSpan.FromSeconds(45))
                .AddError("503 Service Unavailable")
                .Build()));

        // Decision to abort
        episode.AddEvent(EpisodeEvent.Create("decision", "Abort after 3 failed attempts",
            EpisodeEventData.CreateBuilder()
                .AddAgentRole("Integration Specialist")
                .Add("total_attempts", 3)
                .Add("total_duration_seconds", 135)
                .Build()));

        // Lessons learned from failure
        episode.AddLessonLearned("Service appears to be down - check status page before retrying");
        episode.AddLessonLearned("Implement circuit breaker pattern for external services");
        episode.AddLessonLearned("Alert operations team after 2 consecutive failures");

        episode.Complete(EpisodeOutcome.Failure);

        // Assert
        Assert.Equal(EpisodeOutcome.Failure, episode.Outcome);
        Assert.Equal(4, episode.Events.Count);
        Assert.Equal(3, episode.LessonsLearned.Count);

        // Verify all attempts recorded errors
        var actionEvents = episode.Events.Where(e => e.Type == "action").ToList();
        Assert.Equal(3, actionEvents.Count);
        Assert.All(actionEvents, evt =>
        {
            var error = evt.Data.Get<string>("error");
            Assert.NotNull(error);
        });
    }

    [Fact]
    public void ShouldCollaborativeEpisode_WhenUsingComplexScenario()
    {
        // Arrange
        var leadAgentId = AgentId.From(Guid.NewGuid());
        var episode = EpisodicMemory.Create("Multi-Agent Research Task", leadAgentId);

        var metadata = EpisodicMemoryMetadata.CreateBuilder()
            .AddContext("Research latest ML trends for quarterly report")
            .AddCollaborators(s_collaborators)
            .AddEnvironment("development")
            .Add("priority", "high")
            .Build();
        episode.UpdateMetadata(metadata);

        // Act - Simulate collaborative workflow
        episode.AddEvent(EpisodeEvent.Create("action", "Research Agent gathered 50 relevant papers",
            EpisodeEventData.CreateBuilder()
                .AddAgentRole("Research Agent")
                .AddOutput("50 papers from ArXiv, Google Scholar")
                .AddDuration(TimeoutExtended)
                .Build()));

        episode.AddEvent(EpisodeEvent.Create("action", "Analysis Agent processed and categorized papers",
            EpisodeEventData.CreateBuilder()
                .AddAgentRole("Analysis Agent")
                .AddOutput("5 main trends identified")
                .Add("categories", AiCategories)
                .Build()));

        episode.AddEvent(EpisodeEvent.Create("observation", "High overlap in LLM and Efficiency categories",
            EpisodeEventData.CreateBuilder()
                .AddAgentRole("Analysis Agent")
                .AddConfidence(0.92)
                .Build()));

        episode.AddEvent(EpisodeEvent.Create("action", "Writing Agent drafted report sections",
            EpisodeEventData.CreateBuilder()
                .AddAgentRole("Writing Agent")
                .AddOutput("5 sections, 3000 words")
                .Add("readability_score", 85)
                .Build()));

        episode.AddEvent(EpisodeEvent.Create("reflection", "Collaborative approach reduced total time by 60%",
            EpisodeEventData.CreateBuilder()
                .Add("time_saved_minutes", 45)
                .Add("quality_score", 0.88)
                .Build()));

        episode.Complete(EpisodeOutcome.Success,
        [
            "Parallel processing by specialized agents is highly effective",
            "Inter-agent communication protocol worked smoothly",
            "Consider adding a Review Agent for quality assurance"
        ]);

        // Assert
        Assert.Equal(EpisodeOutcome.Success, episode.Outcome);
        Assert.Equal(5, episode.Events.Count);

        // Verify collaborative nature
        var collaborators = episode.Metadata.Get<string[]>("collaborators");
        Assert.Equal(3, collaborators!.Length);

        // Verify different agents participated
        var agentRoles = episode.Events
            .Select(e => e.Data.Get<string>("agent_role"))
            .Where(role => role != null)
            .Distinct()
            .ToList();
        Assert.Equal(3, agentRoles.Count);
    }

    #endregion
}
