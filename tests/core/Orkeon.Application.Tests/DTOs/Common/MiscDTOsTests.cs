using System.Collections.Immutable;
using Orkeon.Application.Common.DTOs;
using static Orkeon.Tests.Shared.Constants.TestEntityIds;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;
using static Orkeon.Tests.Shared.Constants.TestStatusConstants;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;

namespace Orkeon.Application.Tests.DTOs.Common;

public class MiscDTOsTests
{
    // ══════════════ CollaborationDtos ══════════════

    [Fact]
    public void ShouldReturnParticipantCount_WhenCollaborationHasParticipants()
    {
        // Arrange
        var dto = new CollaborationContextDto
        {
            Id = "c1",
            Type = "TeamWork",
            Participants = ImmutableList.Create(AgentId1, AgentId2, AgentId3)
        };

        // Assert
        Assert.Equal(3, dto.ParticipantCount);
    }

    [Fact]
    public void ShouldReturnZeroParticipantCount_WhenNoParticipants()
    {
        // Arrange
        var dto = new CollaborationContextDto { Id = "c1", Type = "Solo" };

        // Assert
        Assert.Equal(0, dto.ParticipantCount);
    }

    [Fact]
    public void ShouldReturnFalseForIsExpired_WhenNoDurationSet()
    {
        // Arrange
        var dto = new CollaborationContextDto { Id = "c1", Type = "T" };

        // Assert
        Assert.False(dto.IsExpired);
    }

    [Fact]
    public void ShouldReturnTrueForIsExpired_WhenDurationExceeded()
    {
        // Arrange
        var dto = new CollaborationContextDto
        {
            Id = "c1",
            Type = "T",
            CreatedAt = DateTime.UtcNow.AddHours(-2),
            MaxDuration = TimeSpan.FromMinutes(30)
        };

        // Assert
        Assert.True(dto.IsExpired);
    }

    [Fact]
    public void ShouldReturnFalseForIsExpired_WhenWithinDuration()
    {
        // Arrange
        var dto = new CollaborationContextDto
        {
            Id = "c1",
            Type = "T",
            CreatedAt = DateTime.UtcNow,
            MaxDuration = TimeSpan.FromHours(1)
        };

        // Assert
        Assert.False(dto.IsExpired);
    }

    [Fact]
    public void ShouldDefaultToActive_WhenCreatingCollaboration()
    {
        // Arrange
        var dto = new CollaborationContextDto { Id = "x", Type = "y" };

        // Assert
        Assert.Equal(Active, dto.Status);
    }

    [Fact]
    public void ShouldReturnTrueForIsAsync_WhenProtocolIsMessageQueue()
    {
        // Arrange
        var proto = new CommunicationProtocolDto { Type = "MessageQueue" };

        // Assert
        Assert.True(proto.IsAsync);
    }

    [Fact]
    public void ShouldReturnTrueForIsAsync_WhenProtocolIsEventStream()
    {
        // Arrange
        var proto = new CommunicationProtocolDto { Type = "EventStream" };

        // Assert
        Assert.True(proto.IsAsync);
    }

    [Fact]
    public void ShouldReturnFalseForIsAsync_WhenProtocolIsDirectCall()
    {
        // Arrange
        var proto = new CommunicationProtocolDto { Type = "DirectCall" };

        // Assert
        Assert.False(proto.IsAsync);
    }

    [Fact]
    public void ShouldSetProtocolDefaults_WhenConstructing()
    {
        // Arrange
        var proto = new CommunicationProtocolDto { Type = "HTTP" };

        // Assert
        Assert.Equal("json", proto.Format);
        Assert.Equal(TimeoutQuick, proto.Timeout);
        Assert.Equal(3, proto.MaxRetries);
    }

    // ══════════════ CrewSettingsDto ══════════════

    [Fact]
    public void ShouldSetCrewSettingsDefaults_WhenConstructing()
    {
        // Act
        var settings = new CrewSettingsDto();

        // Assert
        Assert.Null(settings.MaxRpm);
        Assert.False(settings.ShareCrew);
        Assert.Null(settings.MaxIterations);
        Assert.False(settings.MemoryEnabled);
        Assert.False(settings.CacheEnabled);
        Assert.Equal("en", settings.Language);
        Assert.Empty(settings.CustomOptions);
    }

    [Fact]
    public void ShouldSetMemoryConfigDefaults_WhenConstructing()
    {
        // Act
        var config = new MemoryConfigDto();

        // Assert
        Assert.Equal("InMemory", config.Provider);
        Assert.Null(config.ConnectionString);
    }

    [Fact]
    public void ShouldSetVectorConfigDefaults_WhenConstructing()
    {
        // Act
        var config = new VectorConfigDto();

        // Assert
        Assert.Equal(1536, config.Dimension);
        Assert.Equal("cosine", config.SimilarityMetric);
        Assert.Equal(0.7, config.MinSimilarity);
        Assert.Equal(10, config.MaxResults);
    }

    [Fact]
    public void ShouldSetRetryConfigDefaults_WhenConstructing()
    {
        // Act
        var config = new RetryConfigDto();

        // Assert
        Assert.Equal(3, config.MaxAttempts);
        Assert.Equal(TimeSpan.FromSeconds(1), config.RetryDelay);
        Assert.False(config.UseExponentialBackoff);
        Assert.Equal(2.0, config.BackoffMultiplier);
    }

    // ══════════════ LlmResponseMetadata ══════════════

    [Fact]
    public void ShouldReturnEmptyInstance_WhenAccessingStaticEmpty()
    {
        // Act
        var meta = LlmResponseMetadata.Empty;

        // Assert
        Assert.Null(meta.Model);
        Assert.Null(meta.PromptTokens);
        Assert.Null(meta.CompletionTokens);
        Assert.Null(meta.TotalTokens);
        Assert.Null(meta.Latency);
        Assert.Null(meta.FinishReason);
        Assert.Null(meta.RequestId);
        Assert.Null(meta.ProviderExtensions);
    }

    [Fact]
    public void ShouldPopulateAllFields_WhenCreatingLlmResponseMetadata()
    {
        // Act
        var meta = new LlmResponseMetadata
        {
            Model = ModelGpt4,
            PromptTokens = 100,
            CompletionTokens = 50,
            TotalTokens = 150,
            Latency = TimeSpan.FromMilliseconds(350),
            FinishReason = "stop",
            RequestId = "req-77",
            ProviderExtensions = new Dictionary<string, object> { ["system_fingerprint"] = "fp_1" }
        };

        // Assert
        Assert.Equal(ModelGpt4, meta.Model);
        Assert.Equal(100, meta.PromptTokens);
        Assert.Equal(50, meta.CompletionTokens);
        Assert.Equal(150, meta.TotalTokens);
        Assert.Equal(TimeSpan.FromMilliseconds(350), meta.Latency);
        Assert.Equal("stop", meta.FinishReason);
        Assert.Equal("req-77", meta.RequestId);
        Assert.Single(meta.ProviderExtensions!);
    }

    // ══════════════ PerformanceMetricsDto ══════════════

    [Fact]
    public void ShouldReturnZeroSuccessRate_WhenNoExecutions()
    {
        // Act
        var dto = new PerformanceMetricsDto();

        // Assert
        Assert.Equal(0.0, dto.SuccessRate);
    }

    [Theory]
    [InlineData(8, 2, 0.8)]
    [InlineData(0, 5, 0.0)]
    [InlineData(10, 0, 1.0)]
    public void ShouldCalculateSuccessRate_WhenGivenCounts(int success, int failure, double expected)
    {
        // Arrange
        var dto = new PerformanceMetricsDto
        {
            SuccessCount = success,
            FailureCount = failure
        };

        // Assert
        Assert.Equal(expected, dto.SuccessRate, precision: 5);
    }

    [Theory]
    [InlineData(1.0, 5.0, 50.0, "Excellent")]
    [InlineData(3.0, 20.0, 80.0, "Good")]
    [InlineData(7.0, 45.0, 90.0, "Fair")]
    [InlineData(15.0, 120.0, 99.0, "Poor")]
    public void ShouldCalculateHealthStatus_WhenGivenMetrics(
        double errorRate, double avgResponse, double utilization, string expected)
    {
        // Arrange
        var dto = new PerformanceMetricsDto
        {
            ErrorRate = errorRate,
            AverageResponseTime = avgResponse,
            ResourceUtilization = utilization
        };

        // Assert
        Assert.Equal(expected, dto.HealthStatus);
    }

    [Fact]
    public void ShouldSetPerformanceMetricsDefaults_WhenConstructing()
    {
        // Act
        var dto = new PerformanceMetricsDto();

        // Assert
        Assert.Equal(TimeSpan.FromHours(1), dto.MeasurementPeriod);
    }

    // ══════════════ PlanningDtos ══════════════

    [Fact]
    public void ShouldReturnStepCount_WhenPlanHasSteps()
    {
        // Arrange
        var plan = new ExecutionPlanDto
        {
            Id = "p1",
            Name = "Plan",
            Strategy = "Sequential",
            Steps = ImmutableList.Create(
                new ExecutionStepDto { Id = "s1", Name = "Step1", Description = "Desc1" },
                new ExecutionStepDto { Id = "s2", Name = "Step2", Description = "Desc2" })
        };

        // Assert
        Assert.Equal(2, plan.StepCount);
    }

    [Fact]
    public void ShouldReturnZeroProgress_WhenNoStepsExist()
    {
        // Arrange
        var plan = new ExecutionPlanDto { Id = "p1", Name = "Plan", Strategy = "S" };

        // Assert
        Assert.Equal(0.0, plan.ProgressPercentage);
    }

    [Fact]
    public void ShouldCalculateProgressPercentage_WhenSomeStepsCompleted()
    {
        // Arrange
        var plan = new ExecutionPlanDto
        {
            Id = "p1",
            Name = "Plan",
            Strategy = "Sequential",
            Steps = ImmutableList.Create(
                new ExecutionStepDto { Id = "s1", Name = "S1", Description = "D1", Status = Completed },
                new ExecutionStepDto { Id = "s2", Name = "S2", Description = "D2", Status = Pending },
                new ExecutionStepDto { Id = "s3", Name = "S3", Description = "D3", Status = Completed },
                new ExecutionStepDto { Id = "s4", Name = "S4", Description = "D4", Status = "Running" })
        };

        // Assert — 2/4 completed = 50%
        Assert.Equal(50.0, plan.ProgressPercentage, precision: 5);
    }

    [Fact]
    public void ShouldReturn100Percent_WhenAllStepsCompleted()
    {
        // Arrange
        var plan = new ExecutionPlanDto
        {
            Id = "p1",
            Name = "Plan",
            Strategy = "S",
            Steps = ImmutableList.Create(
                new ExecutionStepDto { Id = "s1", Name = "S1", Description = "D1", Status = Completed },
                new ExecutionStepDto { Id = "s2", Name = "S2", Description = "D2", Status = Completed })
        };

        // Assert
        Assert.Equal(100.0, plan.ProgressPercentage, precision: 5);
    }

    [Fact]
    public void ShouldSetPlanDefaults_WhenConstructing()
    {
        // Act
        var plan = new ExecutionPlanDto { Id = "p1", Name = "P", Strategy = "S" };

        // Assert
        Assert.Equal("Planned", plan.Status);
        Assert.Empty(plan.Steps);
    }

    [Fact]
    public void ShouldSetStepDefaults_WhenConstructing()
    {
        // Act
        var step = new ExecutionStepDto { Id = "s1", Name = "S", Description = "D" };

        // Assert
        Assert.Equal(Pending, step.Status);
        Assert.Empty(step.Dependencies);
    }
}
