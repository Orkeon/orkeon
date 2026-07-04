using Orkeon.Domain.Delegation;

using Orkeon.Domain.Common;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;
namespace Orkeon.Domain.Tests.Delegation;

/// <summary>
/// Tests for Delegation Models following Clean Architecture principles.
/// Tests the various delegation-related classes and their behavior.
/// </summary>
public class DelegationModelsTests
{
    #region DelegationRequest Tests

    [Fact]
    public void ShouldInitializeWithDefaults_WhenUsingDelegationRequestWithDefaultConstructor()
    {
        // Arrange
        var beforeCreation = DateTime.UtcNow;

        // Act
        var request = new DelegationRequest();

        // Assert
        var afterCreation = DateTime.UtcNow;
        Assert.NotNull(request.TaskId);
        Assert.NotNull(request.FromAgentId);
        Assert.NotNull(request.ToAgentId);
        Assert.Equal(string.Empty, request.Reason);
        Assert.Equal(DelegationPriority.Normal, request.Priority);
        Assert.NotNull(request.Context);
        Assert.Empty(request.Context);
        Assert.True(request.RequestedAt >= beforeCreation);
        Assert.True(request.RequestedAt <= afterCreation);
        Assert.Equal(DateTimeKind.Utc, request.RequestedAt.Kind);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingDelegationRequestUsingProperties()
    {
        // Arrange
        var requestedAt = DateTime.UtcNow.AddMinutes(-5);
        var context = new Dictionary<string, object>
        {
            { "urgency", "high" },
            { "retryCount", 2 },
            { "metadata", new { source = "automated" } }
        };

        // Act
        var request = new DelegationRequest
        {
            TaskId = TaskId.Create(),
            FromAgentId = AgentId.Create(),
            ToAgentId = AgentId.Create(),
            Reason = "Agent 456 lacks required expertise",
            Priority = DelegationPriority.High,
            Context = context,
            RequestedAt = requestedAt
        };

        // Assert
        Assert.NotNull(request.FromAgentId);
        Assert.NotNull(request.ToAgentId);
        Assert.Equal("Agent 456 lacks required expertise", request.Reason);
        Assert.Equal(DelegationPriority.High, request.Priority);
        Assert.Equal(context, request.Context);
        Assert.Equal(requestedAt, request.RequestedAt);
    }

    #endregion

    #region DelegationDecision Tests

    [Fact]
    public void ShouldInitializeWithDefaults_WhenUsingDelegationDecisionWithDefaultConstructor()
    {
        // Arrange
        var beforeCreation = DateTime.UtcNow;

        // Act
        var decision = new Orkeon.Domain.Delegation.DelegationOutcome();

        // Assert
        var afterCreation = DateTime.UtcNow;
        Assert.NotNull(decision.RequestId);
        Assert.False(decision.Accepted);
        Assert.Null(decision.Reason);
        Assert.Null(decision.AlternativeAgentId);
        Assert.True(decision.DecidedAt >= beforeCreation);
        Assert.True(decision.DecidedAt <= afterCreation);
        Assert.Equal(DateTimeKind.Utc, decision.DecidedAt.Kind);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingDelegationDecisionUsingProperties()
    {
        // Arrange
        var decidedAt = DateTime.UtcNow.AddSeconds(-30);

        // Act
        var decision = new Orkeon.Domain.Delegation.DelegationOutcome
        {
            RequestId = DelegationRequestId.Create(),
            Accepted = true,
            Reason = "Agent has capacity and required skills",
            AlternativeAgentId = null,
            DecidedAt = decidedAt
        };

        // Assert
        Assert.True(decision.Accepted);
        Assert.Equal("Agent has capacity and required skills", decision.Reason);
        Assert.Null(decision.AlternativeAgentId);
        Assert.Equal(decidedAt, decision.DecidedAt);
    }

    [Fact]
    public void ShouldWithAlternativeAgent_WhenUsingDelegationDecisionUsingRejection()
    {
        // Arrange & Act
        var decision = new Orkeon.Domain.Delegation.DelegationOutcome
        {
            RequestId = DelegationRequestId.Create(),
            Accepted = false,
            Reason = "Agent is overloaded",
            AlternativeAgentId = AgentId.Create(),
            DecidedAt = DateTime.UtcNow
        };

        // Assert
        Assert.False(decision.Accepted);
        Assert.NotNull(decision.AlternativeAgentId);
        Assert.NotNull(decision.AlternativeAgentId);
    }

    #endregion

    #region DelegationPriority Enum Tests

    [Fact]
    public void ShouldHaveExpectedValues_WhenUsingDelegationPriority()
    {
        // Act & Assert
        var expectedValues = new[]
        {
            DelegationPriority.Low,
            DelegationPriority.Normal,
            DelegationPriority.High,
            DelegationPriority.Critical
        };

        foreach (var expectedValue in expectedValues)
        {
            Assert.True(Enum.IsDefined<DelegationPriority>(expectedValue));
        }

        var allValues = Enum.GetValues<DelegationPriority>();
        Assert.Equal(expectedValues.Length, allValues.Length);
    }

    [Fact]
    public void ShouldBeLow_WhenUsingDelegationPriorityWithDefaultValue()
    {
        // Act
        var defaultValue = default(DelegationPriority);

        // Assert
        Assert.Equal(DelegationPriority.Low, defaultValue);
    }

    [Fact]
    public void ShouldBeInOrder_WhenUsingDelegationPriorityNumericValues()
    {
        // Assert
        Assert.True((int)DelegationPriority.Low < (int)DelegationPriority.Normal);
        Assert.True((int)DelegationPriority.Normal < (int)DelegationPriority.High);
        Assert.True((int)DelegationPriority.High < (int)DelegationPriority.Critical);
    }

    #endregion

    #region AgentSkills Tests

    [Fact]
    public void ShouldInitializeWithDefaults_WhenUsingAgentSkillsWithDefaultConstructor()
    {
        // Act
        var agentSkills = new AgentSkills();

        // Assert
        Assert.NotNull(agentSkills.AgentId);
        Assert.NotNull(agentSkills.Skills);
        Assert.Empty(agentSkills.Skills);
        Assert.NotNull(agentSkills.SkillScores);
        Assert.Empty(agentSkills.SkillScores);
        Assert.Equal(0.0, agentSkills.OverallScore);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingAgentSkillsProperties()
    {
        // Arrange
        var skills = new List<string> { "C#", "Python", "Docker", "Kubernetes" };
        var skillScores = new Dictionary<string, double>
        {
            { "C#", 0.95 },
            { "Python", 0.80 },
            { "Docker", 0.70 },
            { "Kubernetes", 0.65 }
        };

        // Act
        var agentSkills = new AgentSkills
        {
            AgentId = AgentId.Create(),
            Skills = skills,
            SkillScores = skillScores,
            OverallScore = 0.775
        };

        // Assert
        Assert.Equal(skills, agentSkills.Skills);
        Assert.Equal(skillScores, agentSkills.SkillScores);
        Assert.Equal(0.775, agentSkills.OverallScore);
    }

    [Fact]
    public void ShouldCompleteScenario_WhenUsingAgentSkills()
    {
        // Arrange & Act
        var agentSkills = new AgentSkills
        {
            AgentId = AgentId.Create(),
            Skills = ["JavaScript", "React", "Node.js", "MongoDB", "AWS"],
            SkillScores = new Dictionary<string, double>
            {
                { "JavaScript", 0.90 },
                { "React", 0.85 },
                { "Node.js", 0.80 },
                { "MongoDB", 0.75 },
                { "AWS", 0.70 }
            },
            OverallScore = 0.80
        };

        // Assert
        Assert.Equal(5, agentSkills.Skills.Count);
        Assert.Equal(5, agentSkills.SkillScores.Count);
        Assert.Equal(0.80, agentSkills.OverallScore);
        Assert.Equal(0.90, agentSkills.SkillScores["JavaScript"]);
    }

    #endregion

    #region DelegationResult Tests

    [Fact]
    public void ShouldInitializeWithDefaults_WhenUsingDelegationResultWithDefaultConstructor()
    {
        // Arrange
        var beforeCreation = DateTime.UtcNow;

        // Act
        var result = new Orkeon.Domain.Delegation.DelegationResult();

        // Assert
        var afterCreation = DateTime.UtcNow;
        Assert.NotNull(result.RequestId);
        Assert.False(result.Success);
        Assert.Equal(string.Empty, result.Output);
        Assert.Null(result.Error);
        Assert.True(result.CompletedAt >= beforeCreation);
        Assert.True(result.CompletedAt <= afterCreation);
        Assert.Equal(DateTimeKind.Utc, result.CompletedAt.Kind);
        Assert.Equal(TimeSpan.Zero, result.Duration);
        Assert.Empty(result.Metadata);
    }

    [Fact]
    public void ShouldCreateSuccessResult_WhenUsingDelegationResultCreatingSuccess()
    {
        // Arrange
        var requestId = DelegationRequestId.Create();
        var output = "Task completed successfully with result X";

        // Act
        var result = Orkeon.Domain.Delegation.DelegationResult.CreateSuccess(requestId, output);

        // Assert
        Assert.Equal(requestId, result.RequestId);
        Assert.True(result.Success);
        Assert.Equal(output, result.Output);
        Assert.Null(result.Error);
    }

    [Fact]
    public void ShouldCreateFailureResult_WhenUsingDelegationResultCreatingFailure()
    {
        // Arrange
        var requestId = DelegationRequestId.Create();
        var error = "Failed to execute task: Timeout exceeded";

        // Act
        var result = Orkeon.Domain.Delegation.DelegationResult.CreateFailure(requestId, error);

        // Assert
        Assert.Equal(requestId, result.RequestId);
        Assert.False(result.Success);
        Assert.Equal(error, result.Error);
        Assert.Equal(string.Empty, result.Output);
    }

    [Fact]
    public void ShouldWithMetadata_WhenUsingDelegationResultWithCompleteScenario()
    {
        // Arrange & Act
        var result = new Orkeon.Domain.Delegation.DelegationResult
        {
            RequestId = DelegationRequestId.Create(),
            Success = true,
            Output = "Analysis complete: 95% confidence",
            Error = null,
            CompletedAt = DateTime.UtcNow,
            Duration = TimeSpan.FromSeconds(45.5),
            Metadata = new Dictionary<string, object>
            {
                { "confidence", 0.95 },
                { "iterations", 3 },
                { "model", ModelGpt4 },
                { "tokens", 1500 }
            }
        };

        // Assert
        Assert.True(result.Success);
        Assert.Equal(TimeSpan.FromSeconds(45.5), result.Duration);
        Assert.Equal(4, result.Metadata.Count);
        Assert.Equal(0.95, result.Metadata["confidence"]);
        Assert.Equal(3, result.Metadata["iterations"]);
    }

    #endregion

    #region QuestionRequest Tests

    [Fact]
    public void ShouldInitializeWithDefaults_WhenUsingQuestionRequestWithDefaultConstructor()
    {
        // Arrange
        var beforeCreation = DateTime.UtcNow;

        // Act
        var question = new QuestionRequest();

        // Assert
        var afterCreation = DateTime.UtcNow;
        Assert.NotNull(question.Id);
        Assert.NotNull(question.Id);
        Assert.NotNull(question.Id); // ID is ULID-based EntityId
        Assert.NotNull(question.FromAgentId);
        Assert.NotNull(question.ToAgentId);
        Assert.Equal(string.Empty, question.Question);
        Assert.Equal(string.Empty, question.Context);
        Assert.True(question.AskedAt >= beforeCreation);
        Assert.True(question.AskedAt <= afterCreation);
        Assert.Equal(DateTimeKind.Utc, question.AskedAt.Kind);
        Assert.Null(question.Timeout);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingQuestionRequestUsingProperties()
    {
        // Arrange
        var id = QuestionRequestId.Create();
        var fromAgentId = AgentId.Create();
        var toAgentId = AgentId.Create();
        var askedAt = DateTime.UtcNow.AddMinutes(-2);
        var timeout = TimeoutQuick;

        // Act
        var question = new QuestionRequest
        {
            Id = id,
            FromAgentId = fromAgentId,
            ToAgentId = toAgentId,
            Question = "What are the best practices for microservices architecture?",
            Context = "Working on system design for scalable application",
            AskedAt = askedAt,
            Timeout = timeout
        };

        // Assert
        Assert.Equal(id, question.Id);
        Assert.Equal(fromAgentId, question.FromAgentId);
        Assert.Equal(toAgentId, question.ToAgentId);
        Assert.Equal("What are the best practices for microservices architecture?", question.Question);
        Assert.Equal("Working on system design for scalable application", question.Context);
        Assert.Equal(askedAt, question.AskedAt);
        Assert.Equal(timeout, question.Timeout);
    }

    [Fact]
    public void ShouldHaveUniqueIds_WhenUsingQuestionRequestWithMultipleInstances()
    {
        // Act
        var question1 = new QuestionRequest();
        var question2 = new QuestionRequest();
        var question3 = new QuestionRequest();

        // Assert
        Assert.NotEqual(question1.Id, question2.Id);
        Assert.NotEqual(question2.Id, question3.Id);
        Assert.NotEqual(question1.Id, question3.Id);
        Assert.All([question1.Id, question2.Id, question3.Id],
            id => Assert.NotNull(id));
    }

    #endregion

    #region AgentInfo Tests

    [Fact]
    public void ShouldInitializeWithDefaults_WhenUsingAgentInfoWithDefaultConstructor()
    {
        // Act
        var agentInfo = new AgentInfo();

        // Assert
        Assert.NotNull(agentInfo.Id);
        Assert.Equal(string.Empty, agentInfo.Role);
        Assert.NotNull(agentInfo.Skills);
        Assert.Empty(agentInfo.Skills);
        Assert.NotNull(agentInfo.Tools);
        Assert.Empty(agentInfo.Tools);
        Assert.True(agentInfo.AllowsDelegation);
        Assert.Equal(0, agentInfo.CurrentWorkload);
        Assert.Equal(1.0, agentInfo.AvailabilityScore);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingAgentInfoUsingProperties()
    {
        // Arrange
        var skills = new List<string> { "Analysis", "Research", "Writing" };
        var tools = new List<string> { "WebSearch", "DataAnalyzer", "ReportGenerator" };

        // Act
        var agentInfo = new AgentInfo
        {
            Id = AgentId.Create(),
            Role = RoleDataAnalyst,
            Skills = skills,
            Tools = tools,
            AllowsDelegation = false,
            CurrentWorkload = 3,
            AvailabilityScore = 0.4
        };

        // Assert
        Assert.NotNull(agentInfo.Id);
        Assert.Equal(RoleDataAnalyst, agentInfo.Role);
        Assert.Equal(skills, agentInfo.Skills);
        Assert.Equal(tools, agentInfo.Tools);
        Assert.False(agentInfo.AllowsDelegation);
        Assert.Equal(3, agentInfo.CurrentWorkload);
        Assert.Equal(0.4, agentInfo.AvailabilityScore);
    }

    [Theory]
    [InlineData(0, 1.0)]    // No workload = fully available
    [InlineData(1, 0.8)]    // Light workload
    [InlineData(3, 0.4)]    // Medium workload
    [InlineData(5, 0.0)]    // Heavy workload = not available
    public void ShouldBasedOnWorkload_WhenUsingAgentInfoUsingAvailabilityScore(int workload, double expectedScore)
    {
        // Arrange & Act
        var agentInfo = new AgentInfo
        {
            CurrentWorkload = workload,
            AvailabilityScore = 1.0 - (workload * 0.2) // Simple calculation
        };

        // Assert
        Assert.Equal(expectedScore, agentInfo.AvailabilityScore, precision: 10);
    }

    #endregion

    #region AgentCapabilityProfile Tests

    [Fact]
    public void ShouldInitializeWithDefaults_WhenUsingAgentCapabilityProfileWithDefaultConstructor()
    {
        // Arrange
        var beforeCreation = DateTime.UtcNow;

        // Act
        var profile = new AgentCapabilityProfile();

        // Assert
        var afterCreation = DateTime.UtcNow;
        Assert.NotNull(profile.AgentId);
        Assert.NotNull(profile.SkillScores);
        Assert.Empty(profile.SkillScores);
        Assert.NotNull(profile.Specializations);
        Assert.Empty(profile.Specializations);
        Assert.NotNull(profile.TaskHistory);
        Assert.Empty(profile.TaskHistory);
        Assert.Equal(1.0, profile.OverallPerformanceScore);
        Assert.True(profile.LastUpdated >= beforeCreation);
        Assert.True(profile.LastUpdated <= afterCreation);
        Assert.Equal(DateTimeKind.Utc, profile.LastUpdated.Kind);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingAgentCapabilityProfileProperties()
    {
        // Arrange
        var lastUpdated = DateTime.UtcNow.AddDays(-1);
        var skillScores = new Dictionary<string, double>
        {
            { "MachineLearning", 0.92 },
            { "DataAnalysis", 0.88 },
            { "Python", 0.95 },
            { "Statistics", 0.85 }
        };
        var specializations = new List<string> { "NLP", "Computer Vision", "Time Series Analysis" };
        var taskHistory = new Dictionary<string, int>
        {
            { "TextClassification", 25 },
            { "ImageRecognition", 15 },
            { "PredictiveModeling", 30 }
        };

        // Act
        var profile = new AgentCapabilityProfile
        {
            AgentId = AgentId.Create(),
            SkillScores = skillScores,
            Specializations = specializations,
            TaskHistory = taskHistory,
            OverallPerformanceScore = 0.895,
            LastUpdated = lastUpdated
        };

        // Assert
        Assert.Equal(skillScores, profile.SkillScores);
        Assert.Equal(specializations, profile.Specializations);
        Assert.Equal(taskHistory, profile.TaskHistory);
        Assert.Equal(0.895, profile.OverallPerformanceScore);
        Assert.Equal(lastUpdated, profile.LastUpdated);
    }

    [Fact]
    public void ShouldScenario_WhenUsingAgentCapabilityProfileWithCompleteProfile()
    {
        // Arrange & Act
        var profile = new AgentCapabilityProfile
        {
            AgentId = AgentId.Create(),
            Specializations =
            [
                "Microservices Architecture",
                "Cloud Native Development",
                "DevOps Practices"
            ],
            TaskHistory = new Dictionary<string, int>
            {
                { "API Development", 45 },
                { "Infrastructure Setup", 20 },
                { "Performance Optimization", 15 },
                { "Code Review", 60 }
            }
        };

        // Add skill scores via mutable Dict
        var skills = new[] { "Java", "Spring Boot", "Kubernetes", "AWS", "Terraform" };
        foreach (var skill in skills)
        {
            profile.SkillScores[skill] = Random.Shared.NextDouble() * 0.3 + 0.7; // 0.7 to 1.0
        }

        // Assert
        Assert.Equal(5, profile.SkillScores.Count);
        Assert.Equal(3, profile.Specializations.Count);
        Assert.Equal(4, profile.TaskHistory.Count);
        Assert.Equal(60, profile.TaskHistory["Code Review"]); // Most frequent task
    }

    #endregion

    #region DelegationRequest Validate Tests

    [Fact]
    public void ShouldNotThrow_WhenValidatingDelegationRequestWithValidData()
    {
        // Arrange
        var fromAgentId = AgentId.Create();
        var toAgentId = AgentId.Create();
        var request = new DelegationRequest
        {
            FromAgentId = fromAgentId,
            ToAgentId = toAgentId,
            Reason = "Agent lacks required expertise"
        };

        // Act & Assert (should not throw)
        request.Validate();
    }

    [Fact]
    public void ShouldThrowInvalidOperationException_WhenValidatingRequestWithSameFromAndToAgentId()
    {
        // Arrange
        var agentId = AgentId.Create();
        var request = new DelegationRequest
        {
            FromAgentId = agentId,
            ToAgentId = agentId,
            Reason = "Some reason"
        };

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => request.Validate());
        Assert.Contains("FromAgentId and ToAgentId must be different", exception.Message);
    }

    [Fact]
    public void ShouldThrowInvalidOperationException_WhenValidatingRequestWithEmptyReason()
    {
        // Arrange
        var request = new DelegationRequest
        {
            FromAgentId = AgentId.Create(),
            ToAgentId = AgentId.Create(),
            Reason = ""
        };

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => request.Validate());
        Assert.Contains("Reason must not be empty", exception.Message);
    }

    [Fact]
    public void ShouldThrowInvalidOperationException_WhenValidatingRequestWithWhitespaceReason()
    {
        // Arrange
        var request = new DelegationRequest
        {
            FromAgentId = AgentId.Create(),
            ToAgentId = AgentId.Create(),
            Reason = "   "
        };

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => request.Validate());
    }

    #endregion

    #region DelegationRequest CreateValidated Tests

    [Fact]
    public void ShouldCreateValidatedRequest_WhenUsingCreateValidatedWithValidData()
    {
        // Arrange
        var taskId = TaskId.Create();
        var fromAgentId = AgentId.Create();
        var toAgentId = AgentId.Create();
        var reason = "Need help with backend tasks";

        // Act
        var request = DelegationRequest.CreateValidated(taskId, fromAgentId, toAgentId, reason);

        // Assert
        Assert.Equal(taskId, request.TaskId);
        Assert.Equal(fromAgentId, request.FromAgentId);
        Assert.Equal(toAgentId, request.ToAgentId);
        Assert.Equal(reason, request.Reason);
        Assert.Equal(DelegationPriority.Normal, request.Priority);
    }

    [Fact]
    public void ShouldCreateValidatedRequestWithPriority_WhenUsingCreateValidatedWithPriority()
    {
        // Arrange
        var taskId = TaskId.Create();
        var fromAgentId = AgentId.Create();
        var toAgentId = AgentId.Create();

        // Act
        var request = DelegationRequest.CreateValidated(
            taskId, fromAgentId, toAgentId, "Urgent task", DelegationPriority.Critical);

        // Assert
        Assert.Equal(DelegationPriority.Critical, request.Priority);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenUsingCreateValidatedWithSameAgentIds()
    {
        // Arrange
        var agentId = AgentId.Create();

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(
            () => DelegationRequest.CreateValidated(
                TaskId.Create(), agentId, agentId, "Some reason"));
        Assert.Contains("FromAgentId and ToAgentId must be different", exception.Message);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenUsingCreateValidatedWithEmptyReason()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(
            () => DelegationRequest.CreateValidated(
                TaskId.Create(), AgentId.Create(), AgentId.Create(), ""));
        Assert.Contains("Reason must not be empty", exception.Message);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenUsingCreateValidatedWithWhitespaceReason()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(
            () => DelegationRequest.CreateValidated(
                TaskId.Create(), AgentId.Create(), AgentId.Create(), "   "));
    }

    #endregion

    #region AgentInfo AvailabilityScore Clamping Tests

    [Fact]
    public void ShouldClampTo1_WhenSettingAvailabilityScoreAbove1()
    {
        // Arrange & Act
        var agentInfo = new AgentInfo { AvailabilityScore = 1.5 };

        // Assert
        Assert.Equal(1.0, agentInfo.AvailabilityScore);
    }

    [Fact]
    public void ShouldClampTo0_WhenSettingAvailabilityScoreBelow0()
    {
        // Arrange & Act
        var agentInfo = new AgentInfo { AvailabilityScore = -0.5 };

        // Assert
        Assert.Equal(0.0, agentInfo.AvailabilityScore);
    }

    [Fact]
    public void ShouldAcceptExactly0_WhenSettingAvailabilityScoreTo0()
    {
        // Arrange & Act
        var agentInfo = new AgentInfo { AvailabilityScore = 0.0 };

        // Assert
        Assert.Equal(0.0, agentInfo.AvailabilityScore);
    }

    [Fact]
    public void ShouldAcceptExactly1_WhenSettingAvailabilityScoreTo1()
    {
        // Arrange & Act
        var agentInfo = new AgentInfo { AvailabilityScore = 1.0 };

        // Assert
        Assert.Equal(1.0, agentInfo.AvailabilityScore);
    }

    [Fact]
    public void ShouldAcceptMiddleValue_WhenSettingAvailabilityScoreInRange()
    {
        // Arrange & Act
        var agentInfo = new AgentInfo { AvailabilityScore = 0.5 };

        // Assert
        Assert.Equal(0.5, agentInfo.AvailabilityScore);
    }

    #endregion

    #region Record Immutability Tests

    [Fact]
    public void ShouldCreateNewInstance_WhenUsingDelegationRequestWith()
    {
        // Arrange
        var original = new DelegationRequest
        {
            TaskId = TaskId.Create(),
            FromAgentId = AgentId.Create(),
            ToAgentId = AgentId.Create(),
            Reason = "Original reason",
            Priority = DelegationPriority.Normal
        };

        // Act
        var modified = original with { Reason = "Updated reason", Priority = DelegationPriority.High };

        // Assert
        Assert.Equal("Original reason", original.Reason);
        Assert.Equal(DelegationPriority.Normal, original.Priority);
        Assert.Equal("Updated reason", modified.Reason);
        Assert.Equal(DelegationPriority.High, modified.Priority);
        Assert.Equal(original.TaskId, modified.TaskId);
        Assert.Equal(original.FromAgentId, modified.FromAgentId);
    }

    [Fact]
    public void ShouldCreateNewInstance_WhenUsingDelegationResultWith()
    {
        // Arrange
        var requestId = DelegationRequestId.Create();
        var original = Orkeon.Domain.Delegation.DelegationResult.CreateSuccess(requestId, "output");

        // Act
        var modified = original with { Duration = TimeSpan.FromSeconds(10) };

        // Assert
        Assert.Equal(TimeSpan.Zero, original.Duration);
        Assert.Equal(TimeSpan.FromSeconds(10), modified.Duration);
        Assert.Equal(original.RequestId, modified.RequestId);
        Assert.Equal(original.Output, modified.Output);
    }

    [Fact]
    public void ShouldCreateNewInstance_WhenUsingDelegationOutcomeWith()
    {
        // Arrange
        var original = new Orkeon.Domain.Delegation.DelegationOutcome
        {
            RequestId = DelegationRequestId.Create(),
            Accepted = false,
            Reason = "Agent is overloaded"
        };

        // Act
        var modified = original with { Accepted = true, Reason = "Agent accepted" };

        // Assert
        Assert.False(original.Accepted);
        Assert.Equal("Agent is overloaded", original.Reason);
        Assert.True(modified.Accepted);
        Assert.Equal("Agent accepted", modified.Reason);
    }

    #endregion

    #region Edge Cases and Validation Tests

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingDelegationModelsWithNullCollections()
    {
        // Act
        var request = new DelegationRequest { Context = null! };
        var agentSkills = new AgentSkills { Skills = null!, SkillScores = null! };
        var result = new Orkeon.Domain.Delegation.DelegationResult { Metadata = null! };
        var agentInfo = new AgentInfo { Skills = null!, Tools = null! };
        var profile = new AgentCapabilityProfile { SkillScores = null!, Specializations = null!, TaskHistory = null! };

        // Assert
        Assert.Null(request.Context);
        Assert.Null(agentSkills.Skills);
        Assert.Null(agentSkills.SkillScores);
        Assert.Null(result.Metadata);
        Assert.Null(agentInfo.Skills);
        Assert.Null(agentInfo.Tools);
        Assert.Null(profile.SkillScores);
        Assert.Null(profile.Specializations);
        Assert.Null(profile.TaskHistory);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingDelegationModelsWithUnicodeContent()
    {
        // Arrange & Act
        var request = new DelegationRequest
        {
            TaskId = TaskId.Create(),
            Reason = "需要专家协助 🤝",
            Context = new Dictionary<string, object>
            {
                { "语言", "中文" },
                { "紧急度", "高 🔴" }
            }
        };

        var question = new QuestionRequest
        {
            Question = "如何实现高可用性？🚀",
            Context = "构建分布式系统 🌐"
        };

        var agentInfo = new AgentInfo
        {
            Role = "开发工程师 👨‍💻",
            Skills = ["编程", "架构设计", "性能优化"]
        };

        // Assert
        Assert.NotNull(request.TaskId);
        Assert.Contains("🤝", request.Reason);
        Assert.Contains("中文", request.Context["语言"].ToString());
        Assert.Contains("🚀", question.Question);
        Assert.Contains("👨‍💻", agentInfo.Role);
        Assert.Contains("编程", agentInfo.Skills);
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(double.NaN)]
    public void ShouldAcceptAnyValue_WhenScoringValues(double score)
    {
        // Arrange & Act
        var agentSkills = new AgentSkills { OverallScore = score };
        var agentInfo = new AgentInfo { AvailabilityScore = score };
        var profile = new AgentCapabilityProfile { OverallPerformanceScore = score };

        // Assert
        Assert.Equal(score, agentSkills.OverallScore);
        // AvailabilityScore is clamped to 0.0-1.0
        Assert.Equal(Math.Clamp(score, 0.0, 1.0), agentInfo.AvailabilityScore);
        Assert.Equal(score, profile.OverallPerformanceScore);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingDelegationResultWithLargeDuration()
    {
        // Arrange & Act
        var result = new Orkeon.Domain.Delegation.DelegationResult { Duration = TimeSpan.MaxValue };

        // Assert
        Assert.Equal(TimeSpan.MaxValue, result.Duration);
    }

    [Fact]
    public void ShouldBeValid_WhenUsingQuestionRequestWithNullTimeout()
    {
        // Act
        var question = new QuestionRequest { Timeout = null };

        // Assert
        Assert.Null(question.Timeout);

        // Act - Set timeout via new instance
        var question2 = new QuestionRequest { Timeout = TimeoutStandard };

        // Assert
        Assert.NotNull(question2.Timeout);
        Assert.Equal(TimeoutStandard, question2.Timeout);
    }

    [Fact]
    public void ShouldProvideReadableRepresentation_WhenUsingDelegationModelsToString()
    {
        // Arrange
        var request = new DelegationRequest { TaskId = TaskId.Create() };
        var decision = new Orkeon.Domain.Delegation.DelegationOutcome { RequestId = DelegationRequestId.Create() };
        var agentSkills = new AgentSkills { AgentId = AgentId.Create() };
        var result = new Orkeon.Domain.Delegation.DelegationResult { RequestId = DelegationRequestId.Create() };
        var question = new QuestionRequest { Question = "Test question?" };
        var agentInfo = new AgentInfo { Id = AgentId.Create() };
        var profile = new AgentCapabilityProfile { AgentId = AgentId.Create() };

        // Act & Assert
        Assert.NotNull(request.ToString());
        Assert.NotNull(decision.ToString());
        Assert.NotNull(agentSkills.ToString());
        Assert.NotNull(result.ToString());
        Assert.NotNull(question.ToString());
        Assert.NotNull(agentInfo.ToString());
        Assert.NotNull(profile.ToString());
    }

    #endregion

    #region Integration and Scenario Tests

    [Fact]
    public void ShouldCompleteScenario_WhenUsingDelegationFlow()
    {
        // Arrange - Setup agents
        var sourceAgent = new AgentInfo
        {
            Id = AgentId.Create(),
            Role = "Frontend Developer",
            Skills = ["React", "TypeScript", "CSS"],
            CurrentWorkload = 4,
            AvailabilityScore = 0.2
        };

        var targetAgent = new AgentInfo
        {
            Id = AgentId.Create(),
            Role = "Backend Developer",
            Skills = ["Node.js", "MongoDB", "REST APIs"],
            CurrentWorkload = 2,
            AvailabilityScore = 0.6,
            AllowsDelegation = true
        };

        // Create delegation request
        var request = new DelegationRequest
        {
            TaskId = TaskId.Create(),
            FromAgentId = sourceAgent.Id,
            ToAgentId = targetAgent.Id,
            Reason = "Frontend developer needs help with backend API implementation",
            Priority = DelegationPriority.High,
            Context = new Dictionary<string, object>
            {
                { "endpoint", "/api/users" },
                { "method", "POST" },
                { "deadline", DateTime.UtcNow.AddHours(4) }
            }
        };

        // Create decision
        var decision = new Orkeon.Domain.Delegation.DelegationOutcome
        {
            RequestId = DelegationRequestId.Create(),
            Accepted = true,
            Reason = "Backend developer has capacity and required skills",
            DecidedAt = DateTime.UtcNow.AddMinutes(1)
        };

        // Execute and create result
        var baseTime = DateTime.UtcNow;
        var startTime = baseTime.AddMinutes(2);
        var endTime = baseTime.AddMinutes(45);
        var result = new Orkeon.Domain.Delegation.DelegationResult
        {
            RequestId = decision.RequestId,
            Success = true,
            Output = "API endpoint implemented successfully with validation and error handling",
            CompletedAt = endTime,
            Duration = endTime - startTime,
            Metadata = new Dictionary<string, object>
            {
                { "linesOfCode", 150 },
                { "testsWritten", 8 },
                { "coveragePercent", 95 }
            }
        };

        // Assert the flow
        Assert.True(sourceAgent.AvailabilityScore < 0.5); // Source agent is busy
        Assert.True(targetAgent.AvailabilityScore > 0.5); // Target agent has capacity
        Assert.True(decision.Accepted);
        Assert.True(result.Success);
        Assert.Equal(TimeSpan.FromMinutes(43), result.Duration);
        Assert.Equal(95, result.Metadata["coveragePercent"]);
    }

    [Fact]
    public void ShouldBySkillsAndAvailability_WhenUsingAgentMatching()
    {
        // Arrange - Create multiple agents with different profiles
        var agents = new List<(AgentInfo info, AgentCapabilityProfile profile)>
        {
            (
                new AgentInfo
                {
                    Id = AgentId.Create(),
                    Role = "Full Stack Developer",
                    Skills = ["C#", "React", "SQL"],
                    CurrentWorkload = 1,
                    AvailabilityScore = 0.8
                },
                new AgentCapabilityProfile
                {
                    AgentId = AgentId.Create(),
                    SkillScores = new Dictionary<string, double>
                    {
                        { "C#", 0.9 }, { "React", 0.85 }, { "SQL", 0.8 }
                    },
                    OverallPerformanceScore = 0.85
                }
            ),
            (
                new AgentInfo
                {
                    Id = AgentId.Create(),
                    Role = "Backend Specialist",
                    Skills = ["C#", "SQL", "Redis"],
                    CurrentWorkload = 3,
                    AvailabilityScore = 0.4
                },
                new AgentCapabilityProfile
                {
                    AgentId = AgentId.Create(),
                    SkillScores = new Dictionary<string, double>
                    {
                        { "C#", 0.95 }, { "SQL", 0.9 }, { "Redis", 0.85 }
                    },
                    OverallPerformanceScore = 0.9
                }
            ),
            (
                new AgentInfo
                {
                    Id = AgentId.Create(),
                    Role = "Junior Developer",
                    Skills = ["JavaScript", "HTML", "CSS"],
                    CurrentWorkload = 0,
                    AvailabilityScore = 1.0
                },
                new AgentCapabilityProfile
                {
                    AgentId = AgentId.Create(),
                    SkillScores = new Dictionary<string, double>
                    {
                        { "JavaScript", 0.6 }, { "HTML", 0.7 }, { "CSS", 0.65 }
                    },
                    OverallPerformanceScore = 0.65
                }
            )
        };

        // Task requiring C# and SQL
        var requiredSkills = new[] { "C#", "SQL" };

        // Find best agent considering both skills and availability
        var bestAgent = agents
            .Where(a => requiredSkills.All(skill => a.info.Skills.Contains(skill)))
            .OrderByDescending(a => a.info.AvailabilityScore * a.profile.OverallPerformanceScore)
            .FirstOrDefault();

        // Assert
        Assert.NotNull(bestAgent.info.Id); // agent1 has better availability * performance
        Assert.True(bestAgent.info.AvailabilityScore > 0.5);
    }

    #endregion
}
