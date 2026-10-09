using System.Runtime.CompilerServices;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainCrew = Orkeon.Domain.Crew.Crew;

namespace Orkeon.Application.Crew.Execution;

/// <summary>
/// The model requests an agent and a crew may make per minute — their <c>maxRpm</c>, CrewAI's
/// <c>max_rpm</c> (GAP-38). Each object that declares a limit has a sliding window of 60 s: an
/// agent's lives with the <see cref="DomainAgent"/> and is shared by its concurrent tasks; a crew's
/// lives with the <see cref="DomainCrew"/> and is shared by all its agents and its manager during
/// its runs. A request waits until each of its windows has room, then counts in each at the same
/// instant; it never takes room in one while it waits for the other.
/// </summary>
/// <remarks>
/// <para>
/// The agent's window is bounded by the smaller of its <c>maxRpm</c> and the host's per-agent cap
/// (<c>RateLimiting:AgentRequestsPerMinute</c>), read at each request: a <c>maxRpm</c> above the cap
/// is held to it. Nothing is created or awaited when nothing is declared.
/// </para>
/// <para>
/// The crew in progress is ambient, like the plan (<see cref="CrewPlanScope"/>): backed by
/// <see cref="AsyncLocal{T}"/>, it follows the run into its parallel waves, the candidates and
/// ballots of a vote, the <c>asyncExecution</c> tasks and a delegated colleague's turn; a crew run
/// from inside a task opens its own, and its requests count in its window, not its caller's.
/// </para>
/// <para>
/// A wait follows the token of its request — Ctrl+C, <c>RunTimeout</c>, <c>/stop</c> — and, cancelled,
/// counts nothing. It never fails by itself. Which of several waiting requests goes first is not
/// guaranteed; each goes in the end.
/// </para>
/// </remarks>
public static class RequestRates
{
    private static readonly ConditionalWeakTable<DomainAgent, RequestRateWindow> s_agentWindows = new();
    private static readonly ConditionalWeakTable<DomainCrew, RequestRateWindow> s_crewWindows = new();
    private static readonly AsyncLocal<CrewRun?> s_run = new();

    /// <summary>The crew whose run is in progress, and the clock its window counts on.</summary>
    private sealed record CrewRun(DomainCrew Crew, TimeProvider Time);

    /// <summary>
    /// Opens the scope of a run of <paramref name="crew"/>: the requests made in it count in the
    /// crew's window. Disposing the handle restores the enclosing scope.
    /// </summary>
    /// <param name="crew">The crew being run.</param>
    /// <param name="time">The clock the crew's window counts on, when the run creates it.</param>
    /// <returns>The handle that restores the enclosing scope.</returns>
    public static IDisposable BeginRun(DomainCrew crew, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(crew);
        ArgumentNullException.ThrowIfNull(time);
        var enclosing = s_run.Value;
        s_run.Value = new CrewRun(crew, time);
        return new Scope(enclosing);
    }

    /// <summary>
    /// Waits until the window of <paramref name="agent"/> and that of the crew in progress have room
    /// for one more model request, then counts it in both. Completes at once — without creating
    /// anything — when neither declares a limit, or both have room.
    /// </summary>
    /// <param name="agent">The agent the request is made for; null for a manager without an agent.</param>
    /// <param name="hostAgentLimit">The host's per-agent cap; null, or zero and less, sets none.</param>
    /// <param name="time">The clock the agent's window counts on, when this request creates it.</param>
    /// <param name="cancellationToken">The request's token: cancelled, the wait ends and counts nothing.</param>
    /// <returns>How long the request waited, and the limit that made it wait.</returns>
    /// <exception cref="OperationCanceledException">The token was cancelled during the wait.</exception>
    public static System.Threading.Tasks.Task<RequestTurn> WaitTurnAsync(
        DomainAgent? agent, int? hostAgentLimit, TimeProvider time, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(time);
        var turn = new Turn(agent, hostAgentLimit is > 0 ? hostAgentLimit : null, s_run.Value);
        if (turn.AgentLimit() is null && turn.CrewLimit() is null)
            return System.Threading.Tasks.Task.FromResult(RequestTurn.Immediate);

        turn.Open(time);
        var first = turn.TryTake();
        return first.Taken ? System.Threading.Tasks.Task.FromResult(RequestTurn.Immediate) : WaitAsync(turn, first, time, cancellationToken);
    }

    private static async System.Threading.Tasks.Task<RequestTurn> WaitAsync(Turn turn, Attempt attempt, TimeProvider time, CancellationToken cancellationToken)
    {
        var started = time.GetTimestamp();
        var cause = attempt.Cause;
        while (!attempt.Taken)
        {
            await System.Threading.Tasks.Task.Delay(attempt.Wait, attempt.Clock!, cancellationToken).ConfigureAwait(false);
            attempt = turn.TryTake();
        }

        return new RequestTurn(time.GetElapsedTime(started), cause);
    }

    /// <summary>One request: who makes it, the run it is part of, and their windows once opened.</summary>
    private sealed class Turn(DomainAgent? agent, int? hostAgentLimit, CrewRun? run)
    {
        private RequestRateWindow? _agentWindow;
        private RequestRateWindow? _crewWindow;

        /// <summary>The agent's limit, read now: the stricter of its <c>maxRpm</c> and the host's cap.</summary>
        public int? AgentLimit()
        {
            if (agent is null)
                return null;

            return (agent.MaxRpm, hostAgentLimit) switch
            {
                ({ } own, { } host) => Math.Min(own, host),
                ({ } own, null) => own,
                (null, var host) => host,
            };
        }

        /// <summary>The crew's limit, read now.</summary>
        public int? CrewLimit() => run?.Crew.MaxRpm;

        public void Open(TimeProvider time)
        {
            if (AgentLimit() is not null)
                _agentWindow = s_agentWindows.GetValue(agent!, _ => new RequestRateWindow(time));
            if (CrewLimit() is not null)
                _crewWindow = s_crewWindows.GetValue(run!.Crew, _ => new RequestRateWindow(run.Time));
        }

        /// <summary>
        /// Counts the request in its windows when each has room, under their locks taken in a fixed
        /// order — the agent's, then the crew's — or says how long to wait and why.
        /// </summary>
        public Attempt TryTake()
        {
            if (_agentWindow is { } agentWindow)
            {
                lock (agentWindow.Gate)
                {
                    if (_crewWindow is not { } crewWindow)
                        return TakeOrWait(agentWindow, AgentLimit(), AgentCause, null, null, null);

                    lock (crewWindow.Gate)
                        return TakeOrWait(agentWindow, AgentLimit(), AgentCause, crewWindow, CrewLimit(), CrewCause);
                }
            }

            var onlyCrew = _crewWindow!;
            lock (onlyCrew.Gate)
                return TakeOrWait(onlyCrew, CrewLimit(), CrewCause, null, null, null);
        }

        private static Attempt TakeOrWait(
            RequestRateWindow first, int? firstLimit, Func<int, string> firstCause,
            RequestRateWindow? second, int? secondLimit, Func<int, string>? secondCause)
        {
            var firstWait = firstLimit is { } a ? first.WaitUnder(a) : TimeSpan.Zero;
            var secondWait = second is not null && secondLimit is { } b ? second.WaitUnder(b) : TimeSpan.Zero;
            if (firstWait == TimeSpan.Zero && secondWait == TimeSpan.Zero)
            {
                first.Record();
                second?.Record();
                return Attempt.TakenNow;
            }

            return firstWait >= secondWait
                ? new Attempt(false, firstWait, first.Time, firstCause(firstLimit!.Value))
                : new Attempt(false, secondWait, second!.Time, secondCause!(secondLimit!.Value));
        }

        private string AgentCause(int limit) =>
            agent!.MaxRpm == limit
                ? $"agent '{agent.Role.Value}' maxRpm {limit}"
                : $"RateLimiting:AgentRequestsPerMinute {limit}";

        private string CrewCause(int limit) =>
            $"crew '{run!.Crew.Name ?? run.Crew.Id.ToString()}' maxRpm {limit}";
    }

    /// <summary>The outcome of one try: counted, or the wait before the next, on which clock, and why.</summary>
    private sealed record Attempt(bool Taken, TimeSpan Wait, TimeProvider? Clock, string? Cause)
    {
        public static Attempt TakenNow { get; } = new(true, TimeSpan.Zero, null, null);
    }

    private sealed class Scope(CrewRun? enclosing) : IDisposable
    {
        public void Dispose() => s_run.Value = enclosing;
    }
}

/// <summary>
/// How a model request got its turn (<see cref="RequestRates.WaitTurnAsync"/>): how long it waited —
/// zero when it went at once — and the limit that made it wait, for the line that says so.
/// </summary>
/// <param name="Waited">How long the request waited.</param>
/// <param name="Limit">The limit that made it wait, in words — <c>agent 'Researcher' maxRpm 2</c>; null when it did not.</param>
public readonly record struct RequestTurn(TimeSpan Waited, string? Limit)
{
    /// <summary>A request that went at once.</summary>
    public static RequestTurn Immediate => default;

    /// <summary>Whether the request waited at all.</summary>
    public bool HasWaited => Waited > TimeSpan.Zero;
}
