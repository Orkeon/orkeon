using Orkeon.Domain.Constants.Task;
using Orkeon.Domain.Constants.Agent;

namespace Orkeon.Domain.SharedKernel.ValueObjects;

/// <summary>Configuration for execution settings.</summary>
public sealed record ExecutionConfig
{
    /// <summary>Gets the maximum number of concurrent tasks.</summary>
    public int MaxConcurrentTasks { get; init; } = 10;
    /// <summary>Gets the default timeout for task execution.</summary>
    public TimeSpan DefaultTimeout { get; init; } = TaskDefaults.DefaultOperationTimeout;
    /// <summary>Gets the maximum number of retries on failure.</summary>
    public int MaxRetries { get; init; } = AgentDefaults.MaxRetryLimit;
    /// <summary>Gets a value indicating whether debug mode is enabled.</summary>
    public bool EnableDebugMode { get; init; }
    /// <summary>Gets a value indicating whether async execution is enabled.</summary>
    public bool EnableAsyncExecution { get; init; } = true;
    /// <summary>Gets the maximum requests per minute for the crew.</summary>
    public int MaxRPM { get; init; }
    /// <summary>Gets the LLM identifier for the manager agent.</summary>
    public string? ManagerLlm { get; init; }
    /// <summary>Gets additional executor-specific settings.</summary>
    public IReadOnlyDictionary<string, object> ExecutorSettings { get; init; } = new Dictionary<string, object>();

    /// <summary>Creates a validated <see cref="ExecutionConfig"/>.</summary>
    public static ExecutionConfig Create(
        int maxConcurrentTasks = 10,
        TimeSpan? defaultTimeout = null,
        int maxRetries = AgentDefaults.MaxRetryLimit,
        bool enableDebugMode = false,
        bool enableAsyncExecution = true,
        int maxRPM = 0,
        string? managerLlm = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxConcurrentTasks);
        ArgumentOutOfRangeException.ThrowIfNegative(maxRetries);
        ArgumentOutOfRangeException.ThrowIfNegative(maxRPM);
        var timeout = defaultTimeout ?? TaskDefaults.DefaultOperationTimeout;
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(defaultTimeout),
                "DefaultTimeout must be positive.");

        return new ExecutionConfig
        {
            MaxConcurrentTasks = maxConcurrentTasks,
            DefaultTimeout = timeout,
            MaxRetries = maxRetries,
            EnableDebugMode = enableDebugMode,
            EnableAsyncExecution = enableAsyncExecution,
            MaxRPM = maxRPM,
            ManagerLlm = managerLlm
        };
    }
}
