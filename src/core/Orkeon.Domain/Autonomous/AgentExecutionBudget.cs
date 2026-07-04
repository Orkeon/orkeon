namespace Orkeon.Domain.Autonomous;

/// <summary>
/// Multi-dimensional execution budget that constrains autonomous agent behaviour.
/// Each agent receives an immutable budget; every action (tool call, delegation,
/// token consumption) decrements the remaining allowance. When any dimension is
/// exhausted a <see cref="BudgetExhaustedException"/> is thrown, forcing the agent
/// to return control to its orchestrator.
/// </summary>
public sealed class AgentExecutionBudget
{
    // ── Limits (immutable after construction) ────────────────────────────
    /// <summary>Maximum tool calls the agent may perform.</summary>
    public int MaxToolCalls { get; init; } = 15;

    /// <summary>Maximum delegation depth (A→B→C = depth 2).</summary>
    public int MaxDelegationDepth { get; init; } = 2;

    /// <summary>Maximum wall-clock time before forced return.</summary>
    public TimeSpan MaxWallTime { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Maximum total tokens (prompt + completion) the agent may consume.</summary>
    public int MaxTokensConsumed { get; init; } = 16_000;

    /// <summary>Maximum sub-agents this agent may spawn.</summary>
    public int MaxSpawnedAgents { get; init; } = 3;

    /// <summary>
    /// Clock used to measure wall-clock time. Injectable for testability (e.g. a
    /// fake/controllable <see cref="System.TimeProvider"/> lets tests exercise the
    /// wall-time budget without real waiting). Defaults to <see cref="System.TimeProvider.System"/>.
    /// </summary>
    public TimeProvider TimeProvider
    {
        get => _timeProvider;
        init
        {
            _timeProvider = value ?? TimeProvider.System;
            _startTimestamp = _timeProvider.GetTimestamp();
        }
    }

    // ── Counters (thread-safe, mutable) ──────────────────────────────────
    private int _toolCalls;
    private int _delegationDepth;
    private int _tokensConsumed;
    private int _spawnedAgents;
    private readonly TimeProvider _timeProvider = TimeProvider.System;
    private readonly long _startTimestamp = TimeProvider.System.GetTimestamp();

    /// <summary>Current number of tool calls executed.</summary>
    public int CurrentToolCalls => _toolCalls;

    /// <summary>Current delegation depth.</summary>
    public int CurrentDelegationDepth => _delegationDepth;

    /// <summary>Current tokens consumed.</summary>
    public int CurrentTokensConsumed => _tokensConsumed;

    /// <summary>Current number of spawned agents.</summary>
    public int CurrentSpawnedAgents => _spawnedAgents;

    /// <summary>Elapsed wall-clock time since budget creation.</summary>
    public TimeSpan Elapsed => _timeProvider.GetElapsedTime(_startTimestamp);

    /// <summary>Whether the budget has been exhausted on any dimension.</summary>
    public bool IsExhausted =>
        _toolCalls >= MaxToolCalls ||
        _delegationDepth > MaxDelegationDepth ||
        _tokensConsumed >= MaxTokensConsumed ||
        _spawnedAgents >= MaxSpawnedAgents ||
        Elapsed >= MaxWallTime;

    // ── Recording actions ────────────────────────────────────────────────

    /// <summary>Records a tool call and throws if the tool-call budget is exhausted.</summary>
    /// <exception cref="BudgetExhaustedException"/>
    public void RecordToolCall()
    {
        AssertWallTime();
        if (Interlocked.Increment(ref _toolCalls) > MaxToolCalls)
            throw new BudgetExhaustedException(
                BudgetDimension.ToolCalls,
                $"Agent exceeded {MaxToolCalls} tool calls.");
    }

    /// <summary>Records a delegation hop and throws if the depth limit is exceeded.</summary>
    /// <exception cref="BudgetExhaustedException"/>
    public void RecordDelegation()
    {
        AssertWallTime();
        if (Interlocked.Increment(ref _delegationDepth) > MaxDelegationDepth)
            throw new BudgetExhaustedException(
                BudgetDimension.DelegationDepth,
                $"Delegation depth {_delegationDepth} exceeds max {MaxDelegationDepth}.");
    }

    /// <summary>Records token consumption and throws if the token budget is exhausted.</summary>
    /// <exception cref="BudgetExhaustedException"/>
    public void RecordTokens(int tokens)
    {
        AssertWallTime();
        if (Interlocked.Add(ref _tokensConsumed, tokens) > MaxTokensConsumed)
            throw new BudgetExhaustedException(
                BudgetDimension.Tokens,
                $"Token budget exhausted ({_tokensConsumed}/{MaxTokensConsumed}).");
    }

    /// <summary>Records a sub-agent spawn and throws if the spawn limit is exceeded.</summary>
    /// <exception cref="BudgetExhaustedException"/>
    public void RecordSpawn()
    {
        AssertWallTime();
        if (Interlocked.Increment(ref _spawnedAgents) > MaxSpawnedAgents)
            throw new BudgetExhaustedException(
                BudgetDimension.SpawnedAgents,
                $"Agent exceeded {MaxSpawnedAgents} spawned sub-agents.");
    }

    /// <summary>
    /// Throws when any dimension is already exhausted, identifying the exhausted
    /// dimension. Unlike the <c>Record*</c> methods this consumes nothing — it is the
    /// pre-flight gate callers use before paying for the next action.
    /// </summary>
    /// <exception cref="BudgetExhaustedException"/>
    public void ThrowIfExhausted()
    {
        AssertWallTime();
        if (_toolCalls >= MaxToolCalls)
            throw new BudgetExhaustedException(
                BudgetDimension.ToolCalls,
                $"Tool-call budget exhausted ({_toolCalls}/{MaxToolCalls}).");
        if (_tokensConsumed >= MaxTokensConsumed)
            throw new BudgetExhaustedException(
                BudgetDimension.Tokens,
                $"Token budget exhausted ({_tokensConsumed}/{MaxTokensConsumed}).");
        if (_delegationDepth > MaxDelegationDepth)
            throw new BudgetExhaustedException(
                BudgetDimension.DelegationDepth,
                $"Delegation depth {_delegationDepth} exceeds max {MaxDelegationDepth}.");
        if (_spawnedAgents >= MaxSpawnedAgents)
            throw new BudgetExhaustedException(
                BudgetDimension.SpawnedAgents,
                $"Agent exceeded {MaxSpawnedAgents} spawned sub-agents.");
    }

    /// <summary>Asserts that the wall-clock time has not been exceeded.</summary>
    /// <exception cref="BudgetExhaustedException"/>
    public void AssertWallTime()
    {
        var elapsed = Elapsed;
        if (elapsed >= MaxWallTime)
            throw new BudgetExhaustedException(
                BudgetDimension.WallTime,
                $"Wall time exceeded ({elapsed:g} >= {MaxWallTime:g}).");
    }

    /// <summary>Returns a snapshot of the current budget state.</summary>
    public BudgetSnapshot ToSnapshot() => new()
    {
        ToolCalls = _toolCalls,
        MaxToolCalls = MaxToolCalls,
        DelegationDepth = _delegationDepth,
        MaxDelegationDepth = MaxDelegationDepth,
        TokensConsumed = _tokensConsumed,
        MaxTokensConsumed = MaxTokensConsumed,
        SpawnedAgents = _spawnedAgents,
        MaxSpawnedAgents = MaxSpawnedAgents,
        Elapsed = Elapsed,
        MaxWallTime = MaxWallTime,
        IsExhausted = IsExhausted
    };

    /// <summary>Creates a child budget for a delegated sub-agent, inheriting remaining allowances with reduced depth.</summary>
    public AgentExecutionBudget CreateChildBudget() => new()
    {
        MaxToolCalls = Math.Max(1, MaxToolCalls - _toolCalls),
        MaxDelegationDepth = Math.Max(0, MaxDelegationDepth - _delegationDepth - 1),
        MaxWallTime = MaxWallTime - Elapsed,
        MaxTokensConsumed = Math.Max(100, MaxTokensConsumed - _tokensConsumed),
        MaxSpawnedAgents = Math.Max(0, MaxSpawnedAgents - _spawnedAgents),
        TimeProvider = _timeProvider
    };

    // ── Presets ──────────────────────────────────────────────────────────

    /// <summary>Conservative budget for production workloads.</summary>
    public static AgentExecutionBudget Strict => new()
    {
        MaxToolCalls = 8,
        MaxDelegationDepth = 1,
        MaxWallTime = TimeSpan.FromMinutes(2),
        MaxTokensConsumed = 8_000,
        MaxSpawnedAgents = 1
    };

    /// <summary>Permissive budget for development and exploration.</summary>
    public static AgentExecutionBudget Permissive => new()
    {
        MaxToolCalls = 50,
        MaxDelegationDepth = 4,
        MaxWallTime = TimeSpan.FromMinutes(15),
        MaxTokensConsumed = 64_000,
        MaxSpawnedAgents = 10
    };

    /// <summary>Default balanced budget.</summary>
    public static AgentExecutionBudget Default => new();
}

/// <summary>Identifies which budget dimension was exhausted.</summary>
public enum BudgetDimension
{
    /// <summary>Tool call limit exceeded.</summary>
    ToolCalls,
    /// <summary>Delegation depth limit exceeded.</summary>
    DelegationDepth,
    /// <summary>Wall-clock time limit exceeded.</summary>
    WallTime,
    /// <summary>Token consumption limit exceeded.</summary>
    Tokens,
    /// <summary>Spawned agent limit exceeded.</summary>
    SpawnedAgents
}

/// <summary>Immutable snapshot of a budget's current state, suitable for logging and telemetry.</summary>
public sealed record BudgetSnapshot
{
    /// <summary>Current tool calls.</summary>
    public required int ToolCalls { get; init; }
    /// <summary>Maximum tool calls allowed.</summary>
    public required int MaxToolCalls { get; init; }
    /// <summary>Current delegation depth.</summary>
    public required int DelegationDepth { get; init; }
    /// <summary>Maximum delegation depth allowed.</summary>
    public required int MaxDelegationDepth { get; init; }
    /// <summary>Current tokens consumed.</summary>
    public required int TokensConsumed { get; init; }
    /// <summary>Maximum tokens allowed.</summary>
    public required int MaxTokensConsumed { get; init; }
    /// <summary>Current spawned agents.</summary>
    public required int SpawnedAgents { get; init; }
    /// <summary>Maximum spawned agents allowed.</summary>
    public required int MaxSpawnedAgents { get; init; }
    /// <summary>Elapsed wall time.</summary>
    public required TimeSpan Elapsed { get; init; }
    /// <summary>Maximum wall time.</summary>
    public required TimeSpan MaxWallTime { get; init; }
    /// <summary>Whether any dimension is exhausted.</summary>
    public required bool IsExhausted { get; init; }
}

/// <summary>
/// Thrown when an autonomous agent has exhausted one dimension of its execution budget.
/// The orchestrator catches this to force a controlled return.
/// </summary>
#pragma warning disable S3925 // BinaryFormatter serialization is obsolete in .NET 10; ISerializable pattern not required
public sealed class BudgetExhaustedException : InvalidOperationException
#pragma warning restore S3925
{
    /// <summary>The dimension that was exhausted.</summary>
    public BudgetDimension Dimension { get; }

    /// <summary>Creates a new <see cref="BudgetExhaustedException"/>.</summary>
    public BudgetExhaustedException(BudgetDimension dimension, string message)
        : base(message)
    {
        Dimension = dimension;
    }

    /// <summary>Initializes a new instance of <see cref="BudgetExhaustedException"/>.</summary>
    public BudgetExhaustedException() { }

    /// <summary>Initializes a new instance of <see cref="BudgetExhaustedException"/>.</summary>
    /// <param name="message">The exception message.</param>
    public BudgetExhaustedException(string message) : base(message) { }

    /// <summary>Initializes a new instance of <see cref="BudgetExhaustedException"/> with an inner exception.</summary>
    /// <param name="message">The exception message.</param>
    /// <param name="innerException">The inner exception.</param>
    public BudgetExhaustedException(string message, Exception innerException) : base(message, innerException) { }
}
