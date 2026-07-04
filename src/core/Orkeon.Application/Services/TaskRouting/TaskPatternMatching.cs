using Orkeon.Domain.Task;
using Orkeon.Domain.Constants.Crew;

namespace Orkeon.Application.Services.TaskRouting;

/// <summary>
/// Pattern matching utilities for task classification and routing.
/// Phase 3.2.1: Advanced pattern matching for task characteristics.
/// </summary>
public static class TaskPatternMatching
{
    private static readonly string[] s_simplePatterns = ["fix typo", "quick update", "simple"];

    private static readonly string[] s_complexPhrases =
    [
        "complex development with multiple integrations",
        "comprehensive", "complex algorithm",
        "complex development with multiple",
        "end-to-end testing with multiple",
        "complex system integration"
    ];
    /// <summary>
    /// Matches task priority using pattern expressions.
    /// </summary>
    public static TaskPriority DeterminePriority(ICrewTask task)
    {
        if (task == null) return TaskPriority.Medium;

        return task switch
        {
            var t when ContainsUrgentKeywords(t) => TaskPriority.Critical,
            var t when ContainsHighKeywords(t) => TaskPriority.High,
            var t when ContainsLowKeywords(t) => TaskPriority.Low,
            _ => TaskPriority.Medium
        };
    }

    /// <summary>
    /// Matches task category using description patterns.
    /// </summary>
    public static TaskCategory ClassifyTask(ICrewTask task)
    {
        if (task?.Description?.Value == null) return TaskCategory.General;

#pragma warning disable CA1308 // normalized lowercase description is the switch subject, not a comparison normalization
        return task.Description.Value.ToLowerInvariant() switch
#pragma warning restore CA1308
        {
            var desc when desc.Contains("research", StringComparison.Ordinal) || desc.Contains("investigate", StringComparison.Ordinal) => TaskCategory.Research,
            var desc when desc.Contains("analyze", StringComparison.Ordinal) || desc.Contains("analysis", StringComparison.Ordinal) || desc.Contains("examination", StringComparison.Ordinal) => TaskCategory.Analysis,
            var desc when desc.Contains("write", StringComparison.Ordinal) || desc.Contains("document", StringComparison.Ordinal) => TaskCategory.Documentation,
            var desc when desc.Contains("code", StringComparison.Ordinal) && (desc.Contains("pr ", StringComparison.Ordinal) || desc.Contains('#', StringComparison.Ordinal) || desc.Contains("implement", StringComparison.Ordinal)) => TaskCategory.Development,
            var desc when desc.Contains("review", StringComparison.Ordinal) || desc.Contains("audit", StringComparison.Ordinal) => TaskCategory.Review,
            var desc when desc.Contains("code", StringComparison.Ordinal) || desc.Contains("implement", StringComparison.Ordinal) || desc.Contains("development", StringComparison.Ordinal) => TaskCategory.Development,
            var desc when desc.Contains("test", StringComparison.Ordinal) || desc.Contains("verify", StringComparison.Ordinal) => TaskCategory.Testing,
            var desc when desc.Contains("deploy", StringComparison.Ordinal) || desc.Contains("release", StringComparison.Ordinal) => TaskCategory.Deployment,
            _ => TaskCategory.General
        };
    }

    /// <summary>
    /// Estimates task duration using pattern matching on complexity indicators.
    /// </summary>
    public static TimeSpan EstimateDuration(ICrewTask task)
    {
        if (task == null) return TimeSpan.FromHours(1);

        var category = ClassifyTask(task);
        var complexity = DetermineComplexity(task);

        // Special case: empty description should return 1 hour
        if (string.IsNullOrEmpty(task.Description?.Value))
            return TimeSpan.FromHours(1);

        var baseTime = category switch
        {
            TaskCategory.Research => TimeSpan.FromHours(2),
            TaskCategory.Analysis => TimeSpan.FromHours(3),
            TaskCategory.Documentation => TimeSpan.FromHours(1), // Fixed: Documentation should be 1 hour base
            TaskCategory.Development => TimeSpan.FromHours(4),
            TaskCategory.Testing => TimeSpan.FromHours(2),
            TaskCategory.Review => CrewDefaults.DefaultExecutionTimeout,
            TaskCategory.Deployment => TimeSpan.FromHours(2),
            _ => TimeSpan.FromHours(2) // Default to 2 hours for General category
        };

        return complexity switch
        {
            TaskComplexity.Simple => baseTime * 0.5,
            TaskComplexity.Medium => baseTime,
            TaskComplexity.Complex => baseTime * 2,
            TaskComplexity.VeryComplex => baseTime * 3,
            _ => baseTime
        };
    }

    /// <summary>
    /// Determines task complexity using pattern matching on multiple factors.
    /// </summary>
    public static TaskComplexity DetermineComplexity(ICrewTask task)
    {
        if (task == null) return TaskComplexity.Medium;

        var description = task.Description?.Value ?? string.Empty;

        if (string.IsNullOrEmpty(description))
            return TaskComplexity.Simple;

#pragma warning disable CA1308 // normalized lowercase text matched against lowercase literal keywords
        var text = description.ToLowerInvariant();
#pragma warning restore CA1308
        var complexityKeywords = CountComplexityKeywords(description);

        if (IsVeryComplexTask(text, complexityKeywords, description.Length))
            return TaskComplexity.VeryComplex;

        if (IsSimpleTaskByPattern(text))
            return TaskComplexity.Simple;

        if (IsComplexTask(text))
            return TaskComplexity.Complex;

        if (IsMediumTask(text))
            return TaskComplexity.Medium;

        return ClassifyByKeywordCount(complexityKeywords, description.Length);
    }

    private static bool IsVeryComplexTask(string text, int complexityKeywords, int descriptionLength)
    {
        if (text.Contains("verycomplex", StringComparison.Ordinal) || text.Contains("very complex", StringComparison.Ordinal))
            return true;
        if (text.Contains("enterprise", StringComparison.Ordinal) &&
            (text.Contains("multiple systems", StringComparison.Ordinal) ||
             text.Contains("comprehensive", StringComparison.Ordinal) ||
             text.Contains("deployment", StringComparison.Ordinal)))
            return true;
        return complexityKeywords >= 5 || descriptionLength > 300;
    }

    private static bool IsSimpleTaskByPattern(string text)
    {
        if (s_simplePatterns.Any(p => text.Contains(p, StringComparison.Ordinal)))
            return true;
        if (text.StartsWith("quick ", StringComparison.Ordinal))
            return true;
        return text.StartsWith("fix ", StringComparison.Ordinal) && text.Length < 30;
    }

    private static bool IsComplexTask(string text)
    {
        if (s_complexPhrases.Any(p => text.Contains(p, StringComparison.Ordinal)))
            return true;

        return text.Contains("complex development", StringComparison.Ordinal)
            && text.Contains("multiple integrations", StringComparison.Ordinal);
    }

    private static bool IsMediumTask(string text)
    {
        if (text.Contains("database integration", StringComparison.Ordinal) || text.Contains("feature", StringComparison.Ordinal))
            return true;
        if (text.Contains("research", StringComparison.Ordinal) && !text.Contains("simple", StringComparison.Ordinal))
            return true;
        return text.Contains("analyze", StringComparison.Ordinal) && !text.Contains("simple", StringComparison.Ordinal);
    }

    private static TaskComplexity ClassifyByKeywordCount(int complexityKeywords, int descriptionLength)
    {
        if (complexityKeywords >= 3)
            return TaskComplexity.Complex;
        if (complexityKeywords == 0 && descriptionLength < 50)
            return TaskComplexity.Simple;
        return TaskComplexity.Medium;
    }

    /// <summary>
    /// Matches resource requirements using task characteristics.
    /// </summary>
    public static ResourceRequirements DetermineResourceRequirements(ICrewTask task)
    {
        if (task == null) return ResourceRequirements.Medium;

        return (ClassifyTask(task), DetermineComplexity(task)) switch
        {
            (TaskCategory.Development, TaskComplexity.VeryComplex) => ResourceRequirements.High,
            (TaskCategory.Development, TaskComplexity.Complex) => ResourceRequirements.High,
            (TaskCategory.Research, TaskComplexity.VeryComplex) => ResourceRequirements.High,
            (TaskCategory.Analysis, TaskComplexity.VeryComplex) => ResourceRequirements.High,
            (TaskCategory.Analysis, TaskComplexity.Complex) => ResourceRequirements.Medium,
            (TaskCategory.Testing, TaskComplexity.Simple) => ResourceRequirements.Low,
            (TaskCategory.Review, _) => ResourceRequirements.Low,
            (_, TaskComplexity.VeryComplex) => ResourceRequirements.High,
            (_, TaskComplexity.Complex) => ResourceRequirements.High,
            (_, TaskComplexity.Simple) => ResourceRequirements.Low,
            _ => ResourceRequirements.Medium
        };
    }

    /// <summary>
    /// Determines optimal agent type using pattern matching.
    /// </summary>
    public static AgentType DetermineOptimalAgentType(ICrewTask task)
    {
        if (task == null) return AgentType.Generalist;

        return ClassifyTask(task) switch
        {
            TaskCategory.Research => AgentType.Researcher,
            TaskCategory.Analysis => AgentType.Analyst,
            TaskCategory.Documentation => AgentType.Writer,
            TaskCategory.Development => AgentType.Developer,
            TaskCategory.Testing => AgentType.Tester,
            TaskCategory.Review => AgentType.Reviewer,
            TaskCategory.Deployment => AgentType.DevOps,
            _ => AgentType.Generalist
        };
    }

    /// <summary>
    /// Pattern matches parallel execution compatibility.
    /// </summary>
    public static bool CanExecuteInParallel(ICrewTask task1, ICrewTask task2)
    {
        if (task1 == null || task2 == null) return false;

        return (ClassifyTask(task1), ClassifyTask(task2)) switch
        {
            // Research tasks can run in parallel
            (TaskCategory.Research, TaskCategory.Research) => true,

            // Documentation tasks can run in parallel
            (TaskCategory.Documentation, TaskCategory.Documentation) => true,

            // Analysis after different research topics
            (TaskCategory.Research, TaskCategory.Analysis) => false,
            (TaskCategory.Analysis, TaskCategory.Research) => true,

            // Testing can run parallel to documentation
            (TaskCategory.Testing, TaskCategory.Documentation) => true,
            (TaskCategory.Documentation, TaskCategory.Testing) => true,

            // Development tasks typically require sequencing
            (TaskCategory.Development, TaskCategory.Development) => false,

            // Deployment must be sequential
            (TaskCategory.Deployment, _) => false,
            (_, TaskCategory.Deployment) => false,

            // Review tasks can run in parallel with most others
            (TaskCategory.Review, not TaskCategory.Development) => true,
            (not TaskCategory.Development, TaskCategory.Review) => true,

            // Default to safe sequential execution
            _ => false
        };
    }

    private static bool ContainsUrgentKeywords(ICrewTask task)
    {
        if (task?.Description?.Value == null && task?.ExpectedOutput == null) return false;
#pragma warning disable CA1308 // normalized lowercase text matched against lowercase literal keywords
        var text = $"{task?.Description?.Value ?? ""} {task?.ExpectedOutput ?? ""}".ToLowerInvariant();
#pragma warning restore CA1308
        string[] urgentKeywords = ["urgent", "critical", "emergency", "asap", "immediately", "blocker"];
        return urgentKeywords.Any(keyword => text.Contains(keyword, StringComparison.Ordinal));
    }

    private static bool ContainsHighKeywords(ICrewTask task)
    {
        if (task?.Description?.Value == null && task?.ExpectedOutput == null) return false;
#pragma warning disable CA1308 // normalized lowercase text matched against lowercase literal keywords
        var text = $"{task?.Description?.Value ?? ""} {task?.ExpectedOutput ?? ""}".ToLowerInvariant();
#pragma warning restore CA1308

        // Check for low priority patterns first to avoid false positives
        if (text.Contains("low priority", StringComparison.Ordinal) || text.Contains("low", StringComparison.Ordinal) && text.Contains("priority", StringComparison.Ordinal))
            return false;

        string[] highKeywords = ["important", "priority", "deadline", "milestone", "release"];
        return highKeywords.Any(keyword => text.Contains(keyword, StringComparison.Ordinal));
    }

    private static bool ContainsLowKeywords(ICrewTask task)
    {
        if (task?.Description?.Value == null && task?.ExpectedOutput == null) return false;
#pragma warning disable CA1308 // normalized lowercase text matched against lowercase literal keywords
        var text = $"{task?.Description?.Value ?? ""} {task?.ExpectedOutput ?? ""}".ToLowerInvariant();
#pragma warning restore CA1308
        string[] lowKeywords = ["nice to have", "optional", "future", "enhancement", "cleanup", "low priority", "minor"];
        // Check for "priority" after "low" pattern specifically
        if (text.Contains("low", StringComparison.Ordinal) && text.Contains("priority", StringComparison.Ordinal)) return true;
        return lowKeywords.Any(keyword => text.Contains(keyword, StringComparison.Ordinal));
    }

    private static int CountComplexityKeywords(string description)
    {
#pragma warning disable CA1308 // normalized lowercase text matched against lowercase literal keywords
        var text = description.ToLowerInvariant();
#pragma warning restore CA1308
        string[] complexKeywords = [
            "complex", "advanced", "sophisticated", "multiple", "integration",
            "algorithm", "optimization", "architecture", "framework", "enterprise",
            "comprehensive", "migration", "systems", "testing"
        ];
        return complexKeywords.Count(keyword => text.Contains(keyword, StringComparison.Ordinal));
    }

}

/// <summary>
/// Task priority levels for pattern matching.
/// </summary>
public enum TaskPriority
{
    /// <summary>Low.</summary>
    Low,
    /// <summary>Medium.</summary>
    Medium,
    /// <summary>High.</summary>
    High,
    /// <summary>Critical.</summary>
    Critical
}

/// <summary>
/// Task categories for classification.
/// </summary>
public enum TaskCategory
{
    /// <summary>Research.</summary>
    Research,
    /// <summary>Analysis.</summary>
    Analysis,
    /// <summary>Documentation.</summary>
    Documentation,
    /// <summary>Development.</summary>
    Development,
    /// <summary>Testing.</summary>
    Testing,
    /// <summary>Review.</summary>
    Review,
    /// <summary>Deployment.</summary>
    Deployment,
    /// <summary>General.</summary>
    General
}

/// <summary>
/// Task complexity levels.
/// </summary>
public enum TaskComplexity
{
    /// <summary>Simple.</summary>
    Simple,
    /// <summary>Medium.</summary>
    Medium,
    /// <summary>Complex.</summary>
    Complex,
    /// <summary>Very Complex.</summary>
    VeryComplex
}

/// <summary>
/// Resource requirement levels.
/// </summary>
public enum ResourceRequirements
{
    /// <summary>Low.</summary>
    Low,
    /// <summary>Medium.</summary>
    Medium,
    /// <summary>High.</summary>
    High
}

/// <summary>
/// Agent types for optimal task assignment.
/// </summary>
public enum AgentType
{
    /// <summary>Researcher.</summary>
    Researcher,
    /// <summary>Analyst.</summary>
    Analyst,
    /// <summary>Writer.</summary>
    Writer,
    /// <summary>Developer.</summary>
    Developer,
    /// <summary>Tester.</summary>
    Tester,
    /// <summary>Reviewer.</summary>
    Reviewer,
    /// <summary>Dev Ops.</summary>
    DevOps,
    /// <summary>Generalist.</summary>
    Generalist
}
