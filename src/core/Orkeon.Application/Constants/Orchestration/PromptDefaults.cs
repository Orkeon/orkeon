namespace Orkeon.Application.Constants.Orchestration;

/// <summary>
/// Prompt string constants used in the execution orchestrator and related services.
/// Centralises hardcoded prompt templates and message literals.
/// </summary>
public static class PromptDefaults
{
    /// <summary>Prefix header for the tool results section injected into the conversation.</summary>
    public const string ToolResultsPrefix = "Tool Results:\n";

    /// <summary>Message appended to the conversation to instruct the agent to continue after tool execution.</summary>
    public const string ContinueWorkingMessage = "Continue working on the task...";

    /// <summary>Message returned when the maximum number of iterations is reached without a final answer.</summary>
    public const string MaxIterationsMessage = "Max iterations reached...";

    /// <summary>Header label for the task description section in the user prompt.</summary>
    public const string TaskSectionHeader = "Task:";

    /// <summary>Prefix for the expected output line in the user prompt.</summary>
    public const string ExpectedOutputPrefix = "Expected output: ";

    /// <summary>Header label for the context variables section in the user prompt.</summary>
    public const string ContextVariablesHeader = "Context variables:";

    /// <summary>Header label for the previous task outputs section in the user prompt.</summary>
    public const string PreviousOutputsHeader = "Previous task results...";

    /// <summary>Message appended when previous outputs are truncated due to context size limits.</summary>
    public const string TruncationMessage = "[... truncated for brevity]";
}
