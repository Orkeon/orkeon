using System.Collections.Immutable;
using Orkeon.Cli.Scripting.Loading;
using Orkeon.Cli.Scripting.Registry;
using Orkeon.Cli.Scripting.Runtime;

namespace Orkeon.Cli.Scripting.Tests.Doubles;

/// <summary>
/// Hand-written counting double for the deferred <see cref="ScriptCommandRegistry"/> load
/// callback (R10.3 / ANT-002). <see cref="Calls"/> records how many times the load pipeline
/// ran; <see cref="FailuresBeforeSuccess"/> makes the first N attempts throw, to exercise
/// the error-surfacing and retry paths.
/// </summary>
public sealed class FakeScriptCommandLoadSource
{
    private int _calls;

    /// <summary>Number of <see cref="LoadAsync"/> invocations so far.</summary>
    public int Calls => Volatile.Read(ref _calls);

    /// <summary>Number of leading <see cref="LoadAsync"/> calls that throw before the source succeeds.</summary>
    public int FailuresBeforeSuccess { get; init; }

    /// <summary>Counting load callback, shaped like <c>ScriptCommandLoader.LoadAndRegisterAsync</c>.</summary>
    public async Task<(ScriptCommandRegistry Registry, CommandLoadResult Summary)> LoadAsync(CancellationToken ct)
    {
        var call = Interlocked.Increment(ref _calls);

        // Genuinely asynchronous, like the real discovery → transpile → evaluate pipeline.
        await Task.Yield();
        ct.ThrowIfCancellationRequested();

        if (call <= FailuresBeforeSuccess)
            throw new InvalidOperationException($"Simulated script-load failure #{call}.");

        return (new ScriptCommandRegistry(Array.Empty<ScriptCommand>()),
                new CommandLoadResult(0, 0, 0, ImmutableArray<string>.Empty));
    }
}
