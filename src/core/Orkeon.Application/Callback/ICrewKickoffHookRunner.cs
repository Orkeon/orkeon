using DomainCrew = Orkeon.Domain.Crew.Crew;

namespace Orkeon.Application.Callback;

/// <summary>
/// Executes the <see cref="BeforeKickoffHook"/> and <see cref="AfterKickoffHook"/>
/// instances registered in DI, in priority order.
/// <para>
/// Opt-in subsystem (R4.9 — MORT-004): not registered by <c>AddOrkeonApplication()</c>.
/// Activate with <c>AddOrkeonKickoffHooks()</c> and invoke the runner around the crew
/// kickoff call. See <c>docs/reference/opt-in-subsystems.md</c>.
/// </para>
/// </summary>
public interface ICrewKickoffHookRunner
{
    /// <summary>
    /// Runs all registered <see cref="BeforeKickoffHook"/> instances against
    /// <paramref name="crew"/>, ordered by ascending <see cref="BeforeKickoffHook.Priority"/>.
    /// Hooks with <see cref="BeforeKickoffHook.ContinueOnError"/> set swallow (and log)
    /// their exceptions; other hooks rethrow and abort the run.
    /// </summary>
    /// <param name="crew">The crew about to be kicked off.</param>
    /// <param name="ct">Cancellation token checked between hooks.</param>
    System.Threading.Tasks.Task RunBeforeKickoffAsync(DomainCrew crew, CancellationToken ct = default);

    /// <summary>
    /// Runs all registered <see cref="AfterKickoffHook"/> instances against
    /// <paramref name="crew"/> and the kickoff <paramref name="result"/>, ordered by
    /// ascending <see cref="AfterKickoffHook.Priority"/>. Hooks with
    /// <see cref="AfterKickoffHook.ContinueOnError"/> set swallow (and log) their
    /// exceptions; other hooks rethrow and abort the run.
    /// </summary>
    /// <param name="crew">The crew that was kicked off.</param>
    /// <param name="result">The kickoff result (may be null).</param>
    /// <param name="ct">Cancellation token checked between hooks.</param>
    System.Threading.Tasks.Task RunAfterKickoffAsync(DomainCrew crew, object? result, CancellationToken ct = default);
}
