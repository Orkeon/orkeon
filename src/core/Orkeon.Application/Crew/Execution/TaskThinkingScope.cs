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
    private static readonly AsyncLocal<Settled?> Ambient = new();

    /// <summary>Opens the scope of one execution of a task; disposing the handle restores the enclosing one.</summary>
    /// <param name="thinking">The thinking the task's calls run on in place of a config with <c>auto</c>; null for the provider's own.</param>
    /// <returns>The handle that restores the enclosing scope.</returns>
    internal static IDisposable Begin(LlmThinkingConfig? thinking)
    {
        var enclosing = Ambient.Value;
        Ambient.Value = new Settled(thinking);
        return new Scope(enclosing);
    }

    /// <summary>
    /// <paramref name="config"/> with its thinking settled: the scope's in place of a thinking that leaves something to
    /// System One, unchanged otherwise.
    /// </summary>
    /// <param name="config">A call's config, its task's override merged.</param>
    /// <returns>A config whose thinking leaves nothing to System One.</returns>
    internal static LlmConfig Apply(LlmConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (config.Thinking is not { HasAuto: true } thinking)
            return config;
        return config with { Thinking = Ambient.Value is { } settled ? settled.Thinking : thinking.Settle(null, null) };
    }

    /// <summary>The merge of <paramref name="taskOverride"/> over <paramref name="baseConfig"/>, its thinking settled.</summary>
    /// <param name="baseConfig">The agent's config, or the base a loop builds from it.</param>
    /// <param name="taskOverride">The task's <c>llm_override</c>.</param>
    /// <returns>The config the call runs on.</returns>
    internal static LlmConfig Resolve(LlmConfig baseConfig, LlmConfigOverride? taskOverride) =>
        Apply(LlmConfigResolver.Resolve(baseConfig, taskOverride));

    private sealed record Settled(LlmThinkingConfig? Thinking);

    private sealed class Scope(Settled? enclosing) : IDisposable
    {
        public void Dispose() => Ambient.Value = enclosing;
    }
}
