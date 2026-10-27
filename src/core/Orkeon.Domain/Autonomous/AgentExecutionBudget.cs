using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Domain.Autonomous;

/// <summary>
/// Multi-dimensional execution budget that constrains autonomous agent behaviour.
/// Each agent receives an immutable budget; every action (tool call, delegation,
/// token consumption) decrements the remaining allowance. When any dimension is
/// exhausted a <see cref="BudgetExhaustedException"/> is thrown, forcing the agent
/// to return control to its orchestrator.
/// </summary>
[Experimental("ORKEXP002", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public sealed class AgentExecutionBudget
{
    // ── Limits (immutable after construction) ────────────────────────────
    /// <summary>Maximum tool calls the agent may perform.</summary>
    public int MaxToolCalls { get; init; } = 15;

    /// <summary>Maximum delegation depth (A→B→C = depth 2).</summary>
    public int MaxDelegationDepth { get; init; } = 2;

    /// <summary>Maximum wall-clock time before forced return.</summary>
    public TimeSpan MaxWallTime { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Maximum running sum of tokens the agent may consume: the prompt plus the completion of every
    /// call recorded through <see cref="RecordTokens"/> or <see cref="RecordChildTokens"/>. A call that resends its whole context counts
    /// that context again, so a conversation of n calls pays its context about n times.
    /// </summary>
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

    /// <summary>
    /// A budget this one also charges: every tool call, token and spawn recorded here is recorded
    /// there too, its wall clock pauses with this one, and this budget is exhausted as soon as that
    /// one is. Several budgets charging one share its tally, which is thread-safe like this one's,
    /// so runs at once never spend more than it allows. A call this budget refuses is not charged, and
    /// a call the charged one refuses is not counted here either. Null (the default): this budget stands
    /// alone. Delegation depth is not charged. <see cref="CreateChildBudget"/> carries the charge over,
    /// so what a child spends is charged too.
    /// </summary>
    public AgentExecutionBudget? ChargedTo { get; init; }

    // ── Counters (thread-safe, mutable) ──────────────────────────────────
    private int _toolCalls;
    private int _delegationDepth;
    private int _tokensConsumed;
    private int _tokenCalls;
    private int _spawnedAgents;
    private readonly TimeProvider _timeProvider = TimeProvider.System;
    private readonly long _startTimestamp = TimeProvider.System.GetTimestamp();
    private readonly Lock _pauseGate = new();
    private TimeSpan _paused;
    private int _pauses;
    private long _pausedSince;

    /// <summary>Current number of tool calls executed.</summary>
    public int CurrentToolCalls => _toolCalls;

    /// <summary>Current delegation depth.</summary>
    public int CurrentDelegationDepth => _delegationDepth;

    /// <summary>Current tokens consumed.</summary>
    public int CurrentTokensConsumed => _tokensConsumed;

    /// <summary>Number of calls recorded through <see cref="RecordTokens"/>.</summary>
    public int CurrentTokenCalls => _tokenCalls;

    /// <summary>Current number of spawned agents.</summary>
    public int CurrentSpawnedAgents => _spawnedAgents;

    /// <summary>
    /// Elapsed wall-clock time since budget creation, the time spent under
    /// <see cref="PauseWallClock"/> excluded.
    /// </summary>
    public TimeSpan Elapsed
    {
        get
        {
            lock (_pauseGate)
            {
                var paused = _pauses > 0 ? _paused + _timeProvider.GetElapsedTime(_pausedSince) : _paused;
                return _timeProvider.GetElapsedTime(_startTimestamp) - paused;
            }
        }
    }

    /// <summary>
    /// Stops the wall clock until the returned handle is disposed: the time a permission gate
    /// holds a tool call for an operator's answer is not the agent's. Pauses nest; the clock
    /// runs again when the last one ends, and disposing a handle twice resumes once.
    /// </summary>
    public IDisposable PauseWallClock()
    {
        PauseWallClockCore();
        return new WallClockResumption(this);
    }

    private void PauseWallClockCore()
    {
        lock (_pauseGate)
        {
            if (_pauses++ == 0)
                _pausedSince = _timeProvider.GetTimestamp();
        }

        ChargedTo?.PauseWallClockCore();
    }

    private void ResumeWallClock()
    {
        lock (_pauseGate)
        {
            if (--_pauses == 0)
                _paused += _timeProvider.GetElapsedTime(_pausedSince);
        }

        ChargedTo?.ResumeWallClock();
    }

    private sealed class WallClockResumption(AgentExecutionBudget owner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                owner.ResumeWallClock();
        }
    }

    /// <summary>Whether the budget has been exhausted on any dimension.</summary>
    public bool IsExhausted =>
        _toolCalls >= MaxToolCalls ||
        _delegationDepth > MaxDelegationDepth ||
        _tokensConsumed >= MaxTokensConsumed ||
        _spawnedAgents >= MaxSpawnedAgents ||
        Elapsed >= MaxWallTime ||
        ChargedTo?.IsExhausted == true;

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
        ChargeOrRollBack(ref _toolCalls, static charged => charged.RecordToolCall());
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

    /// <summary>
    /// Records the tokens of one call (its prompt plus its completion) here and on every budget this one
    /// charges, then throws if a running sum passes its token budget - the farthest charged budget's
    /// refusal first - or, when none does, if the wall time is spent. The tokens stay recorded whatever
    /// it throws: they were sent and received, and the answer they paid for is kept.
    /// </summary>
    /// <exception cref="BudgetExhaustedException"/>
    public void RecordTokens(int tokens) => RecordTokensOn(ChainOf(this), tokens);

    /// <summary>
    /// Records the tokens a child budget's agent reported (<see cref="CreateChildBudget"/>) on the child,
    /// on this budget and on every budget either charges, once each: a budget the two chains share - the
    /// child carries this one's charge - counts the tokens once. Throws as <see cref="RecordTokens"/>
    /// does, this chain's refusals before the child's; the tokens stay recorded whatever it throws.
    /// </summary>
    /// <exception cref="BudgetExhaustedException"/>
    public void RecordChildTokens(AgentExecutionBudget child, int tokens)
    {
        ArgumentNullException.ThrowIfNull(child);
        var budgets = ChainOf(this);
        foreach (var budget in ChainOf(child))
        {
            if (!budgets.Contains(budget))
                budgets.Add(budget);
        }
        RecordTokensOn(budgets, tokens);
    }

    /// <summary>
    /// <paramref name="budget"/> and every budget it charges, transitively, the farthest charged first.
    /// </summary>
    private static List<AgentExecutionBudget> ChainOf(AgentExecutionBudget budget)
    {
        var chain = new List<AgentExecutionBudget>();
        for (var current = budget; current is not null; current = current.ChargedTo)
            chain.Insert(0, current);
        return chain;
    }

    /// <summary>
    /// Adds <paramref name="tokens"/> and one call to each of <paramref name="budgets"/>, then throws the
    /// first token refusal among them, else the first spent wall time.
    /// </summary>
    private static void RecordTokensOn(List<AgentExecutionBudget> budgets, int tokens)
    {
        BudgetExhaustedException? refusal = null;
        foreach (var budget in budgets)
        {
            var consumed = Interlocked.Add(ref budget._tokensConsumed, tokens);
            var calls = Interlocked.Increment(ref budget._tokenCalls);
            if (consumed > budget.MaxTokensConsumed)
                refusal ??= budget.TokensExhausted(consumed, calls);
        }
        if (refusal is not null)
            throw refusal;
        foreach (var budget in budgets)
            budget.AssertOwnWallTime();
    }

    /// <summary>The refusal of a spent token budget, naming what it counted.</summary>
    private BudgetExhaustedException TokensExhausted(int consumed, int calls) => new(
        BudgetDimension.Tokens,
        $"Token budget exhausted: {consumed} tokens sent and received over {calls} {(calls == 1 ? "call" : "calls")} (limit {MaxTokensConsumed})");

    /// <summary>Records a sub-agent spawn and throws if the spawn limit is exceeded.</summary>
    /// <exception cref="BudgetExhaustedException"/>
    public void RecordSpawn()
    {
        AssertWallTime();
        if (Interlocked.Increment(ref _spawnedAgents) > MaxSpawnedAgents)
            throw new BudgetExhaustedException(
                BudgetDimension.SpawnedAgents,
                $"Agent exceeded {MaxSpawnedAgents} spawned sub-agents.");
        ChargeOrRollBack(ref _spawnedAgents, static charged => charged.RecordSpawn());
    }

    /// <summary>
    /// Records on <see cref="ChargedTo"/> an action this budget has just counted in
    /// <paramref name="counter"/> and allowed. When the charged budget refuses it, the count here is
    /// taken back before the refusal propagates: a refused action is counted on neither tally.
    /// </summary>
    private void ChargeOrRollBack(ref int counter, Action<AgentExecutionBudget> record)
    {
        if (ChargedTo is not { } charged) return;
        try
        {
            record(charged);
        }
        catch (BudgetExhaustedException)
        {
            Interlocked.Decrement(ref counter);
            throw;
        }
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
            throw TokensExhausted(_tokensConsumed, _tokenCalls);
        if (_delegationDepth > MaxDelegationDepth)
            throw new BudgetExhaustedException(
                BudgetDimension.DelegationDepth,
                $"Delegation depth {_delegationDepth} exceeds max {MaxDelegationDepth}.");
        if (_spawnedAgents >= MaxSpawnedAgents)
            throw new BudgetExhaustedException(
                BudgetDimension.SpawnedAgents,
                $"Agent exceeded {MaxSpawnedAgents} spawned sub-agents.");
        ChargedTo?.ThrowIfExhausted();
    }

    /// <summary>
    /// Asserts that the wall-clock time has not been exceeded, on this budget and on the one it
    /// charges (<see cref="ChargedTo"/>).
    /// </summary>
    /// <exception cref="BudgetExhaustedException"/>
    public void AssertWallTime()
    {
        AssertOwnWallTime();
        ChargedTo?.AssertWallTime();
    }

    /// <summary>Throws when this budget's own wall time is spent, whatever the budget it charges says.</summary>
    private void AssertOwnWallTime()
    {
        var elapsed = Elapsed;
        if (elapsed >= MaxWallTime)
            throw new BudgetExhaustedException(
                BudgetDimension.WallTime,
                $"Wall time exceeded ({elapsed:g} >= {MaxWallTime:g}).");
    }

    /// <summary>
    /// Returns a snapshot of the current budget state. A budget that charges another one
    /// (<see cref="ChargedTo"/>) carries that one's snapshot in <see cref="BudgetSnapshot.Charged"/>: it
    /// tells why <see cref="BudgetSnapshot.IsExhausted"/> can be true while every dimension of this one
    /// is under its limit.
    /// </summary>
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
        IsExhausted = IsExhausted,
        Charged = ChargedTo?.ToSnapshot()
    };

    /// <summary>
    /// Creates a child budget for a delegated sub-agent, inheriting remaining allowances with reduced depth.
    /// A budget that charges another one (<see cref="ChargedTo"/>) hands the charge to its child, and each
    /// allowance is also capped by what the charged budget has left.
    /// </summary>
    public AgentExecutionBudget CreateChildBudget() => new()
    {
        MaxToolCalls = Math.Max(1, Remaining(static b => b.MaxToolCalls - b._toolCalls)),
        MaxDelegationDepth = Math.Max(0, MaxDelegationDepth - _delegationDepth - 1),
        MaxWallTime = RemainingWallTime(),
        MaxTokensConsumed = Math.Max(100, Remaining(static b => b.MaxTokensConsumed - b._tokensConsumed)),
        MaxSpawnedAgents = Math.Max(0, Remaining(static b => b.MaxSpawnedAgents - b._spawnedAgents)),
        TimeProvider = _timeProvider,
        ChargedTo = ChargedTo
    };

    /// <summary>The smallest of <paramref name="left"/> over this budget and every budget it charges, transitively.</summary>
    private int Remaining(Func<AgentExecutionBudget, int> left)
    {
        var remaining = int.MaxValue;
        for (var budget = this; budget is not null; budget = budget.ChargedTo)
            remaining = Math.Min(remaining, left(budget));
        return remaining;
    }

    /// <summary>The smallest wall time left over this budget and every budget it charges, transitively.</summary>
    private TimeSpan RemainingWallTime()
    {
        var remaining = TimeSpan.MaxValue;
        for (var budget = this; budget is not null; budget = budget.ChargedTo)
        {
            var left = budget.MaxWallTime - budget.Elapsed;
            if (left < remaining) remaining = left;
        }
        return remaining;
    }

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
[Experimental("ORKEXP002", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
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
[Experimental("ORKEXP002", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
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
    /// <summary>Whether any dimension is exhausted, on this budget or on the one it charges (<see cref="Charged"/>).</summary>
    public required bool IsExhausted { get; init; }
    /// <summary>
    /// The snapshot of the budget this one charges (<see cref="AgentExecutionBudget.ChargedTo"/>), taken
    /// with this one; null for a budget that charges none.
    /// </summary>
    public BudgetSnapshot? Charged { get; init; }
}

/// <summary>
/// Thrown when an autonomous agent has exhausted one dimension of its execution budget.
/// The orchestrator catches this to force a controlled return.
/// </summary>
#pragma warning disable S3925 // BinaryFormatter serialization is obsolete in .NET 10; ISerializable pattern not required
[Experimental("ORKEXP002", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
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
