namespace Orkeon.Application.Constants.Execution;

/// <summary>
/// Default values for execution time and timeout configuration.
/// Centralizes the magic number 300 (seconds) used across agents, tasks, and configuration.
/// </summary>
public static class ExecutionDefaults
{
    /// <summary>
    /// Default maximum execution time in seconds for a single agent task or operation (5 minutes).
    /// </summary>
    public const int DefaultMaxExecutionSeconds = 300;
}
