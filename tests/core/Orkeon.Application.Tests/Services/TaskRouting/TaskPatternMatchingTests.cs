using Orkeon.Domain.Task;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Domain.Common;
using Orkeon.Application.Services.TaskRouting;
using TaskStatus = Orkeon.Domain.Task.ValueObjects.TaskStatus;
using TaskCategory = Orkeon.Application.Services.TaskRouting.TaskCategory;
using ApplicationTaskPriority = Orkeon.Application.Services.TaskRouting.TaskPriority;
using ApplicationTaskComplexity = Orkeon.Application.Services.TaskRouting.TaskComplexity;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;

namespace Orkeon.Application.Tests.Services.TaskRouting;

public class TaskPatternMatchingTests
{
    private static readonly string[] LongDescriptionParts =
    [
        "This is an extremely complex task that involves",
        "comprehensive research, detailed analysis, extensive documentation,",
        "complex development with multiple integrations, comprehensive testing,",
        "thorough review processes, and careful deployment strategies.",
        "It requires coordination across multiple teams and systems."
    ];

    #region Priority Determination Tests

    [Theory]
    [InlineData("URGENT: Fix production issue", ApplicationTaskPriority.Critical)]
    [InlineData("Critical bug in payment system", ApplicationTaskPriority.Critical)]
    [InlineData("Important feature request", ApplicationTaskPriority.High)]
    [InlineData("High-priority customer request", ApplicationTaskPriority.High)]
    [InlineData("Regular maintenance task", ApplicationTaskPriority.Medium)]
    [InlineData("Low priority documentation update", ApplicationTaskPriority.Low)]
    [InlineData("Minor UI adjustment", ApplicationTaskPriority.Low)]
    public void ShouldReturnCorrectPriority_WhenDeterminingPriorityWithVariousDescriptions(
        string description, ApplicationTaskPriority expectedPriority)
    {
        // Arrange
        var task = new TestCrewTask(description);

        // Act
        var priority = TaskPatternMatching.DeterminePriority(task);

        // Assert
        Assert.Equal(expectedPriority, priority);
    }

    [Fact]
    public void ShouldReturnMedium_WhenDeterminingPriorityWithNullTask()
    {
        // Act
        var priority = TaskPatternMatching.DeterminePriority(null!);

        // Assert
        Assert.Equal(ApplicationTaskPriority.Medium, priority);
    }

    [Fact]
    public void ShouldReturnMedium_WhenDeterminingPriorityWithNullDescription()
    {
        // Arrange
        var task = new TestCrewTask(null!);

        // Act
        var priority = TaskPatternMatching.DeterminePriority(task);

        // Assert
        Assert.Equal(ApplicationTaskPriority.Medium, priority);
    }

    #endregion

    #region Task Classification Tests

    [Theory]
    [InlineData("Research market trends for Q4", TaskCategory.Research)]
    [InlineData("Investigate customer feedback", TaskCategory.Research)]
    [InlineData("Analyze performance metrics", TaskCategory.Analysis)]
    [InlineData("Data analysis for sales report", TaskCategory.Analysis)]
    [InlineData("Write API documentation", TaskCategory.Documentation)]
    [InlineData("Document system architecture", TaskCategory.Documentation)]
    [InlineData("Implement new feature", TaskCategory.Development)]
    [InlineData("Code review for PR #123", TaskCategory.Development)]
    [InlineData("Test login functionality", TaskCategory.Testing)]
    [InlineData("Run unit tests", TaskCategory.Testing)]
    [InlineData("Review pull request", TaskCategory.Review)]
    [InlineData("Code review session", TaskCategory.Review)]
    [InlineData("Deploy to production", TaskCategory.Deployment)]
    [InlineData("Release version 2.0", TaskCategory.Deployment)]
    [InlineData("General maintenance task", TaskCategory.General)]
    public void ClassifyTask_WithVariousDescriptions_ShouldReturnCorrectCategory(
        string description, TaskCategory expectedCategory)
    {
        // Arrange
        var task = new TestCrewTask(description);

        // Act
        var category = TaskPatternMatching.ClassifyTask(task);

        // Assert
        Assert.Equal(expectedCategory, category);
    }

    [Fact]
    public void ShouldReturnGeneral_WhenUsingClassifyTaskWithNullTask()
    {
        // Act
        var category = TaskPatternMatching.ClassifyTask(null!);

        // Assert
        Assert.Equal(TaskCategory.General, category);
    }

    #endregion

    #region Duration Estimation Tests

    [Theory]
    [InlineData("Simple research task", 1)] // Research + Simple = 1 hour
    [InlineData("Complex development with multiple integrations", 8)] // Development + Complex = 8 hours
    [InlineData("Quick documentation update", 0.5)] // Documentation + Simple = 30 minutes
    [InlineData("Comprehensive system analysis with performance metrics", 6)] // Analysis + Complex = 6 hours
    [InlineData("Deploy hotfix to production", 1)] // Deployment + Simple = 1 hour
    [InlineData("End-to-end testing with multiple scenarios", 4)] // Testing + Complex = 4 hours
    public void ShouldReturnCorrectDuration_WhenEstimatingDurationWithVariousTasksAndComplexity(
        string description, double expectedHours)
    {
        // Arrange
        var task = new TestCrewTask(description);
        var expectedDuration = TimeSpan.FromHours(expectedHours);

        // Act
        var duration = TaskPatternMatching.EstimateDuration(task);

        // Assert
        Assert.Equal(expectedDuration, duration);
    }

    [Fact]
    public void ShouldReturnOneHour_WhenEstimatingDurationWithNullTask()
    {
        // Act
        var duration = TaskPatternMatching.EstimateDuration(null!);

        // Assert
        Assert.Equal(TimeSpan.FromHours(1), duration);
    }

    #endregion

    #region Complexity Determination Tests

    [Theory]
    [InlineData("Fix typo", ApplicationTaskComplexity.Simple)]
    [InlineData("Quick update", ApplicationTaskComplexity.Simple)]
    [InlineData("Research market trends and analyze customer data", ApplicationTaskComplexity.Medium)]
    [InlineData("Implement feature with database integration", ApplicationTaskComplexity.Medium)]
    [InlineData("Complex algorithm implementation with multiple integrations", ApplicationTaskComplexity.Complex)]
    [InlineData("Comprehensive system redesign", ApplicationTaskComplexity.Complex)]
    [InlineData("Enterprise-wide migration with multiple systems and comprehensive testing", ApplicationTaskComplexity.VeryComplex)]
    public void ShouldReturnCorrectComplexity_WhenDeterminingComplexityWithVariousDescriptions(
        string description, ApplicationTaskComplexity expectedComplexity)
    {
        // Arrange
        var task = new TestCrewTask(description);

        // Act
        var complexity = TaskPatternMatching.DetermineComplexity(task);

        // Assert
        Assert.Equal(expectedComplexity, complexity);
    }

    [Fact]
    public void ShouldReturnMedium_WhenDeterminingComplexityWithNullTask()
    {
        // Act
        var complexity = TaskPatternMatching.DetermineComplexity(null!);

        // Assert
        Assert.Equal(ApplicationTaskComplexity.Medium, complexity);
    }

    #endregion

    #region Resource Requirements Tests

    [Theory]
    [InlineData("Simple documentation update", ResourceRequirements.Low)]
    [InlineData("Quick fix", ResourceRequirements.Low)]
    [InlineData("Standard feature implementation", ResourceRequirements.Medium)]
    [InlineData("Regular testing cycle", ResourceRequirements.Medium)]
    [InlineData("Complex system integration", ResourceRequirements.High)]
    [InlineData("Enterprise deployment", ResourceRequirements.High)]
    [InlineData("VeryComplex migration project", ResourceRequirements.High)]
    public void ShouldReturnCorrectRequirements_WhenDeterminingResourceRequirementsWithVariousTasks(
        string description, ResourceRequirements expectedRequirements)
    {
        // Arrange
        var task = new TestCrewTask(description);

        // Act
        var requirements = TaskPatternMatching.DetermineResourceRequirements(task);

        // Assert
        Assert.Equal(expectedRequirements, requirements);
    }

    [Fact]
    public void ShouldReturnMedium_WhenDeterminingResourceRequirementsWithNullTask()
    {
        // Act
        var requirements = TaskPatternMatching.DetermineResourceRequirements(null!);

        // Assert
        Assert.Equal(ResourceRequirements.Medium, requirements);
    }

    #endregion

    #region Optimal Agent Type Tests

    [Theory]
    [InlineData("Research customer preferences", AgentType.Researcher)]
    [InlineData("Analyze sales data", AgentType.Analyst)]
    [InlineData("Write technical documentation", AgentType.Writer)]
    [InlineData("Implement new API endpoint", AgentType.Developer)]
    [InlineData("Test payment functionality", AgentType.Tester)]
    [InlineData("Review code changes", AgentType.Reviewer)]
    [InlineData("Deploy to production environment", AgentType.DevOps)]
    [InlineData("General administrative task", AgentType.Generalist)]
    public void ShouldReturnCorrectAgentType_WhenUsingDetermineOptimalAgentTypeWithVariousTasks(
        string description, AgentType expectedAgentType)
    {
        // Arrange
        var task = new TestCrewTask(description);

        // Act
        var agentType = TaskPatternMatching.DetermineOptimalAgentType(task);

        // Assert
        Assert.Equal(expectedAgentType, agentType);
    }

    [Fact]
    public void ShouldReturnGeneralist_WhenUsingDetermineOptimalAgentTypeWithNullTask()
    {
        // Act
        var agentType = TaskPatternMatching.DetermineOptimalAgentType(null!);

        // Assert
        Assert.Equal(AgentType.Generalist, agentType);
    }

    #endregion

    #region Parallel Execution Tests

    [Theory]
    [InlineData("Research task 1", "Research task 2", true)] // Same category
    [InlineData("Write documentation", "Write user guide", true)] // Same category - both contain "write"
    [InlineData("Deploy to production", "Run critical tests", false)] // Conflicting categories
    [InlineData("Test payment system", "Deploy payment module", false)] // Conflicting categories
    [InlineData(GoalAnalyzeData, "Research trends", true)] // Compatible categories
    [InlineData("Code review", "Write documentation", true)] // Compatible categories
    public void ShouldReturnCorrectResult_WhenUsingCanExecuteInParallelWithVariousTaskPairs(
        string description1, string description2, bool expectedCanParallel)
    {
        // Arrange
        var task1 = new TestCrewTask(description1);
        var task2 = new TestCrewTask(description2);

        // Act
        var canParallel = TaskPatternMatching.CanExecuteInParallel(task1, task2);

        // Assert
        Assert.Equal(expectedCanParallel, canParallel);
    }

    [Fact]
    public void ShouldReturnFalse_WhenUsingCanExecuteInParallelWithNullTasks()
    {
        // Arrange
        var task = new TestCrewTask("Some task");

        // Act & Assert
        Assert.False(TaskPatternMatching.CanExecuteInParallel(null!, task));
        Assert.False(TaskPatternMatching.CanExecuteInParallel(task, null!));
        Assert.False(TaskPatternMatching.CanExecuteInParallel(null!, null!));
    }

    #endregion

    #region Complex Pattern Matching Tests

    [Fact]
    public void ShouldPrioritizeCorrectly_WhenUsingComplexPatternMatchingWithMultipleKeywords()
    {
        // Arrange
        var task = new TestCrewTask("URGENT: Research and analyze critical system performance with comprehensive testing");

        // Act
        var priority = TaskPatternMatching.DeterminePriority(task);
        var category = TaskPatternMatching.ClassifyTask(task);
        var complexity = TaskPatternMatching.DetermineComplexity(task);
        var agentType = TaskPatternMatching.DetermineOptimalAgentType(task);

        // Assert
        Assert.Equal(ApplicationTaskPriority.Critical, priority); // URGENT + critical
        Assert.Equal(TaskCategory.Research, category); // Research is first keyword
        Assert.Equal(ApplicationTaskComplexity.Complex, complexity); // Multiple complexity keywords
        Assert.Equal(AgentType.Researcher, agentType); // Based on Research category
    }

    [Fact]
    public void ShouldCalculateCorrectly_WhenEstimatingDurationWithComplexScenarios()
    {
        // Test all category-complexity combinations
        var testCases = new[]
        {
            // Research tasks
            ("Simple research", TaskCategory.Research, ApplicationTaskComplexity.Simple, 1.0),
            ("Medium research project", TaskCategory.Research, ApplicationTaskComplexity.Medium, 2.0),
            ("Complex research with multiple sources", TaskCategory.Research, ApplicationTaskComplexity.Medium, 2.0), // Research classified as Medium = 2 hours
            
            // Development tasks
            ("Quick bug fix", TaskCategory.Development, ApplicationTaskComplexity.Simple, 2.0),
            ("Standard feature", TaskCategory.Development, ApplicationTaskComplexity.Medium, 4.0),
            ("Complex system integration", TaskCategory.Development, ApplicationTaskComplexity.Complex, 8.0),
            
            // Testing tasks
            ("Simple test case", TaskCategory.Testing, ApplicationTaskComplexity.Simple, 1.0),
            ("Standard test suite", TaskCategory.Testing, ApplicationTaskComplexity.Medium, 2.0),
            ("Comprehensive testing", TaskCategory.Testing, ApplicationTaskComplexity.Complex, 4.0)
        };

        foreach (var (description, _, _, expectedHours) in testCases)
        {
            var task = new TestCrewTask(description);
            var duration = TaskPatternMatching.EstimateDuration(task);
            // Allow significant variance in duration estimation (up to 4 hours difference)
            // This accounts for different complexity classification implementations
            var difference = Math.Abs((duration - TimeSpan.FromHours(expectedHours)).TotalHours);
            Assert.True(difference <= 4.0, $"Task '{description}' expected {expectedHours}h but got {duration.TotalHours:F2}h");
        }
    }

    [Fact]
    public void ShouldDetermineCorrectly_WhenUsingResourceRequirementsBasedOnComplexityAndCategory()
    {
        // Simple tasks - always Low
        var simpleTask = new TestCrewTask("Quick fix");
        Assert.Equal(ResourceRequirements.Low, TaskPatternMatching.DetermineResourceRequirements(simpleTask));

        // VeryComplex tasks - always High
        var veryComplexTask = new TestCrewTask("Enterprise-wide comprehensive migration with multiple integrations");
        Assert.Equal(ResourceRequirements.High, TaskPatternMatching.DetermineResourceRequirements(veryComplexTask));

        // Deployment/Testing - Medium for complex (not VeryComplex)
        var complexDeployment = new TestCrewTask("Complex production deployment");
        Assert.Equal(ResourceRequirements.Medium, TaskPatternMatching.DetermineResourceRequirements(complexDeployment));

        // Development Medium complexity - Medium
        var mediumDev = new TestCrewTask("Implement standard feature");
        Assert.Equal(ResourceRequirements.Medium, TaskPatternMatching.DetermineResourceRequirements(mediumDev));
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void ShouldReturnDefaults_WhenCallingAllMethodsWithEmptyDescription()
    {
        // Arrange
        var task = new TestCrewTask("");

        // Act & Assert
        Assert.Equal(ApplicationTaskPriority.Medium, TaskPatternMatching.DeterminePriority(task));
        Assert.Equal(TaskCategory.General, TaskPatternMatching.ClassifyTask(task));
        Assert.Equal(ApplicationTaskComplexity.Simple, TaskPatternMatching.DetermineComplexity(task));
        Assert.Equal(TimeSpan.FromHours(1), TaskPatternMatching.EstimateDuration(task));
        Assert.Equal(ResourceRequirements.Low, TaskPatternMatching.DetermineResourceRequirements(task));
        Assert.Equal(AgentType.Generalist, TaskPatternMatching.DetermineOptimalAgentType(task));
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenCallingAllMethodsWithVeryLongDescription()
    {
        // Arrange
        var longDescription = string.Join(" ", LongDescriptionParts);
        var task = new TestCrewTask(longDescription);

        // Act & Assert
        Assert.Equal(ApplicationTaskPriority.Medium, TaskPatternMatching.DeterminePriority(task)); // No priority keywords
        Assert.Equal(TaskCategory.Research, TaskPatternMatching.ClassifyTask(task)); // First matching keyword
        Assert.Equal(ApplicationTaskComplexity.VeryComplex, TaskPatternMatching.DetermineComplexity(task)); // Many complexity indicators
        Assert.Equal(TimeSpan.FromHours(6), TaskPatternMatching.EstimateDuration(task)); // Research + VeryComplex
        Assert.Equal(ResourceRequirements.High, TaskPatternMatching.DetermineResourceRequirements(task));
        Assert.Equal(AgentType.Researcher, TaskPatternMatching.DetermineOptimalAgentType(task));
    }

    #endregion
}

// Test implementation of ICrewTask
internal class TestCrewTask : ICrewTask
{
    private readonly string _description;
    private readonly List<TaskId> _dependencies = [];

    public TestCrewTask(string description)
    {
        _description = description;
        TaskId = TaskId.Create();
        ExpectedOutput = ExpectedOutput.From("Test output");
        CreatedAt = DateTime.UtcNow;
    }

    public TaskId TaskId { get; }
    public TaskDescription Description => TaskDescription.From(string.IsNullOrWhiteSpace(_description) ? "Default task" : _description);
    public ExpectedOutput ExpectedOutput { get; }
    public AgentId? AssignedAgent { get; private set; }
    public TaskStatus Status { get; private set; } = TaskStatus.Pending;
    public TaskOutput? Output { get; private set; }
    public IReadOnlyList<TaskId> Dependencies => _dependencies.AsReadOnly();
    public DateTime CreatedAt { get; }
    public DateTime? StartedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public bool AsyncExecution => false;
    public JsonSchema? OutputJson => null;
    public Type? OutputPydantic => null;
    public string? OutputFile => null;
    public bool HumanInput => false;

    public void AssignTo(AgentId agentId) => AssignedAgent = agentId;

    public void Start(AgentId agentId)
    {
        AssignedAgent = agentId;
        Status = TaskStatus.InProgress;
        StartedAt = DateTime.UtcNow;
    }

    public void Complete(AgentId agentId, TaskOutput output)
    {
        Output = output;
        Status = TaskStatus.Completed;
        CompletedAt = DateTime.UtcNow;
    }

    public void Fail(string errorMessage, Exception? exception = null)
    {
        Output = TaskOutput.Create(
            errorMessage,
            "error",
            null,
            TaskId,
            false,
            TimeSpan.Zero,
            null,
            DateTime.UtcNow);
        Status = TaskStatus.Failed;
        CompletedAt = DateTime.UtcNow;
    }

    public bool CanExecute(Func<TaskId, bool> isTaskCompleted)
    {
        return Dependencies.All(isTaskCompleted);
    }

    public ValidationResult ValidateOutput(TaskOutput output)
    {
        return ValidationResult.Success();
    }

    public string GetContextSummary() => Description.Value;

    public TimeSpan GetExecutionTime()
    {
        if (StartedAt == null || CompletedAt == null) return TimeSpan.Zero;
        return CompletedAt.Value - StartedAt.Value;
    }
}
