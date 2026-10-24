namespace Orkeon.Application.Constants.Orchestration;

/// <summary>
/// Logging-related default values for the execution orchestrator.
/// </summary>
public static class LoggingDefaults
{
    /// <summary>Maximum number of characters from a tool result to include in log messages.</summary>
    public const int MaxToolResultLogLength = 200;

    /// <summary>
    /// Maximum number of characters from a model answer in the Information log line; the whole answer
    /// is logged at Debug, and the run's final answer is printed whole at its end.
    /// </summary>
    public const int MaxLlmResponseLogLength = 300;
}
