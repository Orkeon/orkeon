using Orkeon.Domain.Constants.Task;
using Orkeon.Domain.Constants.Agent;

namespace Orkeon.Domain.SharedKernel.ValueObjects;

/// <summary>Configuration for execution settings.</summary>
public sealed record ExecutionConfig
{
    /// <summary>Gets the default timeout for task execution.</summary>
    public TimeSpan DefaultTimeout { get; init; } = TaskDefaults.DefaultOperationTimeout;
    /// <summary>Gets the maximum number of retries on failure.</summary>
    public int MaxRetries { get; init; } = AgentDefaults.MaxRetryLimit;
    /// <summary>Gets a value indicating whether debug mode is enabled.</summary>
    public bool EnableDebugMode { get; init; }
    /// <summary>Gets the maximum requests per minute for the crew.</summary>
    public int MaxRPM { get; init; }
    /// <summary>Gets additional executor-specific settings.</summary>
    public IReadOnlyDictionary<string, object> ExecutorSettings { get; init; } = new Dictionary<string, object>();

    /// <summary>Creates a validated <see cref="ExecutionConfig"/>.</summary>
    public static ExecutionConfig Create(
        TimeSpan? defaultTimeout = null,
        int maxRetries = AgentDefaults.MaxRetryLimit,
        bool enableDebugMode = false,
        int maxRPM = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxRetries);
        ArgumentOutOfRangeException.ThrowIfNegative(maxRPM);
        var timeout = defaultTimeout ?? TaskDefaults.DefaultOperationTimeout;
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(defaultTimeout),
                "DefaultTimeout must be positive.");

        return new ExecutionConfig
        {
            DefaultTimeout = timeout,
            MaxRetries = maxRetries,
            EnableDebugMode = enableDebugMode,
            MaxRPM = maxRPM,
        };
    }
}
