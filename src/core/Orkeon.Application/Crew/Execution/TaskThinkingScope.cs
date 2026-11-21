using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Application.Crew.Execution;

/// <summary>
/// The thinking the calls of a task run on when its config leaves the effort to System One (#439): settled once, at
/// the task's start, by the orchestrator, and read by each place that resolves a call's config - the chat options
/// composer, the native and legacy loops, the correction round -, which are shared singletons and cannot hold it.
/// </summary>
/// <remarks>
/// Ambient, like <see cref="Interfaces.Ports.LlmUsageScope"/>: backed by <see cref="AsyncLocal{T}"/>, opened around one execution of a
/// task, it follows that execution's calls and no other task's. The literal <c>auto</c> never reaches a provider:
/// outside a scope (a loop driven on its own), the marker is dropped and the effort the config would have had without
/// it stands, its own switch kept.
/// </remarks>
internal static class TaskThinkingScope
{
    private static readonly AsyncLocal<ITaskThinking?> Ambient = new();

    /// <summary>Opens the scope of one execution of a task; disposing the handle restores the enclosing one.</summary>
    /// <param name="thinking">The thinking the task's calls run on in place of a config with <c>auto</c>; null for the provider's own.</param>
    /// <returns>The handle that restores the enclosing scope.</returns>
    internal static IDisposable Begin(LlmThinkingConfig? thinking) => Begin(new Fixed(thinking));

    /// <summary>
    /// Opens the scope of one execution of a task on a thinking that may be raised while it runs; disposing the handle
    /// restores the enclosing one.
    /// </summary>
    /// <param name="thinking">The task's thinking, read at each call.</param>
    /// <returns>The handle that restores the enclosing scope.</returns>
    internal static IDisposable Begin(ITaskThinking thinking)
    {
        ArgumentNullException.ThrowIfNull(thinking);
        var enclosing = Ambient.Value;
        Ambient.Value = thinking;
        return new Scope(enclosing);
    }

    /// <summary>
    /// Tells the scope's task that a round of its output failed its guardrail and the agent is asked again (#439): the
    /// thinking goes one step up for the next round, inside the allowed levels. Nothing outside a scope.
    /// </summary>
    internal static void RaiseAfterFailedRound() => Ambient.Value?.RaiseAfterFailedRound();

    /// <summary>
    /// <paramref name="config"/> with its thinking settled: the scope's in place of a thinking that leaves something to
    /// System One, unchanged otherwise.
    /// </summary>
    /// <param name="config">A call's config, its task's override merged.</param>
    /// <returns>A config whose thinking leaves nothing to System One.</returns>
    internal static LlmConfig Apply(LlmConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (config.Thinking is not { } thinking || thinking is { HasAuto: false, AutoEffort: null })
            return config;
        // An effort: auto under enabled: false leaves nothing to System One: thinking stays off, the marker dropped.
        if (!thinking.HasAuto)
            return config with { Thinking = thinking.Settle(null, null) };
        return config with { Thinking = Ambient.Value is { } settled ? settled.Current : thinking.Settle(null, null) };
    }

    /// <summary>The merge of <paramref name="taskOverride"/> over <paramref name="baseConfig"/>, its thinking settled.</summary>
    /// <param name="baseConfig">The agent's config, or the base a loop builds from it.</param>
    /// <param name="taskOverride">The task's <c>llm_override</c>.</param>
    /// <returns>The config the call runs on.</returns>
    internal static LlmConfig Resolve(LlmConfig baseConfig, LlmConfigOverride? taskOverride) =>
        Apply(LlmConfigResolver.Resolve(baseConfig, taskOverride));

    private sealed record Fixed(LlmThinkingConfig? Thinking) : ITaskThinking
    {
        public LlmThinkingConfig? Current => Thinking;

        public void RaiseAfterFailedRound()
        {
            // A fixed thinking is never raised.
        }
    }

    private sealed class Scope(ITaskThinking? enclosing) : IDisposable
    {
        public void Dispose() => Ambient.Value = enclosing;
    }
}

/// <summary>The thinking a task's calls run on while it leaves something to System One (#439), raised as it fails.</summary>
internal interface ITaskThinking
{
    /// <summary>Gets the thinking the next call runs on; null for the provider's own.</summary>
    LlmThinkingConfig? Current { get; }

    /// <summary>A round of the task's output failed its guardrail: the next one runs a step higher, when one is left.</summary>
    void RaiseAfterFailedRound();
}
