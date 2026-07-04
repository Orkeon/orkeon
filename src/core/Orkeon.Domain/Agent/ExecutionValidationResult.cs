namespace Orkeon.Domain.Agent;

/// <summary>
/// Result of validating whether an agent can execute a task.
/// </summary>
public sealed record ExecutionValidationResult
{
    /// <summary>
    /// Gets whether the agent can execute the task.
    /// </summary>
    public bool CanExecute { get; }

    /// <summary>
    /// Gets the validation issues found.
    /// </summary>
    public IReadOnlyList<string> Issues { get; }

    private ExecutionValidationResult(bool canExecute, IReadOnlyList<string> issues)
    {
        CanExecute = canExecute;
        Issues = issues;
    }

    /// <summary>
    /// Creates a successful validation result.
    /// </summary>
    public static ExecutionValidationResult Success()
        => new(true, Array.Empty<string>());

    /// <summary>
    /// Creates a failed validation result with the given issues.
    /// </summary>
    public static ExecutionValidationResult Failure(IReadOnlyList<string> issues)
        => new(false, issues);
}
