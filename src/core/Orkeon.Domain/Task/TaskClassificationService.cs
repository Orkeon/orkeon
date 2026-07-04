namespace Orkeon.Domain.Task;

/// <summary>
/// Domain service that classifies tasks based on their type name, description, and expected output.
/// This encapsulates the business rule for determining which execution strategy applies to a task.
/// </summary>
internal static class TaskClassificationService
{
    /// <summary>
    /// Classifies a task into an execution strategy category based on heuristic matching
    /// against the task type name, description, and expected output.
    /// </summary>
    /// <param name="typeName">The type name of the task (e.g., "ResearchTask").</param>
    /// <param name="description">The task description text, or <see langword="null"/>.</param>
    /// <param name="expectedOutput">The expected output text, or <see langword="null"/>.</param>
    /// <returns>The classified <see cref="TaskCategory"/>.</returns>
    public static TaskCategory Classify(string typeName, string? description, string? expectedOutput)
    {
        // Priority 1: Match on task type name (most specific)
        if (typeName.Contains("Research", StringComparison.Ordinal)) return TaskCategory.Research;
        if (typeName.Contains("Analysis", StringComparison.Ordinal)) return TaskCategory.Analysis;
        if (typeName.Contains("Writing", StringComparison.Ordinal)) return TaskCategory.Writing;
        if (typeName.Contains("Code", StringComparison.Ordinal)) return TaskCategory.Coding;
        if (typeName.Contains("Review", StringComparison.Ordinal)) return TaskCategory.Review;

        // Priority 2: Match on description content (case-insensitive)
        if (description?.Contains("research", StringComparison.OrdinalIgnoreCase) == true) return TaskCategory.Research;
        if (description?.Contains("analyze", StringComparison.OrdinalIgnoreCase) == true) return TaskCategory.Analysis;
        if (description?.Contains("write", StringComparison.OrdinalIgnoreCase) == true) return TaskCategory.Writing;
        if (description?.Contains("code", StringComparison.OrdinalIgnoreCase) == true) return TaskCategory.Coding;
        if (description?.Contains("review", StringComparison.OrdinalIgnoreCase) == true) return TaskCategory.Review;

        // Priority 3: Match on expected output patterns
        if (expectedOutput?.Contains("report", StringComparison.OrdinalIgnoreCase) == true) return TaskCategory.Research;
        if (expectedOutput?.Contains("summary", StringComparison.OrdinalIgnoreCase) == true) return TaskCategory.Analysis;
        if (expectedOutput?.Contains("document", StringComparison.OrdinalIgnoreCase) == true) return TaskCategory.Writing;
        if (expectedOutput?.Contains("implementation", StringComparison.OrdinalIgnoreCase) == true) return TaskCategory.Coding;

        return TaskCategory.Generic;
    }
}

/// <summary>
/// Represents the domain-level category of a task, determined by heuristic classification.
/// </summary>
public enum TaskCategory
{
    /// <summary>Research-oriented task.</summary>
    Research,
    /// <summary>Analysis-oriented task.</summary>
    Analysis,
    /// <summary>Writing-oriented task.</summary>
    Writing,
    /// <summary>Coding-oriented task.</summary>
    Coding,
    /// <summary>Review-oriented task.</summary>
    Review,
    /// <summary>Generic/uncategorized task.</summary>
    Generic
}
