using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Crew;
using Orkeon.Infrastructure.Crew;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Context;
using Orkeon.Domain.Autonomous;
using Orkeon.Domain.Common;
using Orkeon.Domain.Task;
using Orkeon.Infrastructure.Crew.Strategies;
using IProcessStrategy = Orkeon.Domain.Crew.IProcessStrategy;
using IAgentRepository = Orkeon.Domain.Agent.IAgentRepository;
using ITaskRepository = Orkeon.Domain.Task.ITaskRepository;
using DomainCrew = Orkeon.Domain.Crew.Crew;
using DomainCrewOutput = Orkeon.Domain.Crew.CrewOutput;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using ApplicationTaskOutput = Orkeon.Application.Execution.TaskOutput;
using DomainTaskOutput = Orkeon.Domain.Task.ValueObjects.TaskOutput;

namespace Orkeon.Infrastructure.Consensus;

/// <summary>
/// Consensual process strategy where agents independently execute each task, then vote on
/// the answers: each agent ranks the other agents' successful answers, anonymised under
/// labels (GAP-04). If consensus is not reached, optional discussion rounds allow agents to
/// reconsider with context from others' results.
/// </summary>
/// <remarks>
/// <para>
/// Also implements <see cref="IProcessStrategy"/> so that
/// <c>ProcessStrategyFactory</c> routes <c>ProcessType.Consensual</c> like every other
/// process type (R3.3): <see cref="ExecuteSequentialAsync"/> maps to the consensual
/// voting pipeline; the other modes have dedicated strategies.
/// </para>
/// <para>
/// The crew's memory receives the retained answer and nothing else of the vote (GAP-20): the
/// candidates and the ballots run without storing their result, and the strategy stores the
/// answer it retained once, under the agent that wrote it.
/// </para>
/// </remarks>
public sealed partial class ConsensualProcessStrategy : IProcessStrategy
{
    private readonly IVotingStrategy _votingStrategy;
    private readonly IBallotCollector _ballots;
    private readonly IAgentExecutionService _executionService;
    private readonly ITaskRepository _taskRepository;
    private readonly IAgentRepository _agentRepository;
    private readonly IMemoryScope _memoryScope;
    private readonly IMemoryCoordinator _memoryCoordinator;
    private readonly ILogger<ConsensualProcessStrategy> _logger;
    private readonly ConsensualProcessOptions _options;
    private readonly CrewHookDispatcher _hooks;
    private readonly TaskLifecycle _lifecycle;

    /// <summary>
    /// The role a consensual task reports: it has no single author, the vote is the agent.
    /// </summary>
    private const string ConsensusRole = "consensus";

    /// <summary>Initializes a new instance of <see cref="ConsensualProcessStrategy"/>.</summary>
    /// <param name="votingStrategy">The voting strategy.</param>
    /// <param name="ballotCollector">Collects each voter's ballot on the anonymised answers (GAP-04).</param>
    /// <param name="dependencies">The collaborators shared by every crew strategy.</param>
    /// <param name="memoryCoordinator">
    /// Stores the answer the vote retained in the crew's memory — the candidates and the ballots
    /// are run without storing anything (GAP-20).
    /// </param>
    /// <param name="logger">The logger.</param>
    /// <param name="options">The consensual process options.</param>
    /// <param name="hook">Optional crew execution hook. May be null (BUS-03).</param>
    public ConsensualProcessStrategy(
        IVotingStrategy votingStrategy,
        IBallotCollector ballotCollector,
        CrewStrategyDependencies dependencies,
        IMemoryCoordinator memoryCoordinator,
        ILogger<ConsensualProcessStrategy> logger,
        IOptions<ConsensualProcessOptions> options,
        ICrewExecutionHook? hook = null)
    {
        ArgumentNullException.ThrowIfNull(votingStrategy);
        _votingStrategy = votingStrategy;
        ArgumentNullException.ThrowIfNull(ballotCollector);
        _ballots = ballotCollector;
        ArgumentNullException.ThrowIfNull(dependencies);
        _executionService = dependencies.ExecutionService;
        _taskRepository = dependencies.TaskRepository;
        _agentRepository = dependencies.AgentRepository;
        _memoryScope = dependencies.MemoryScope;
        ArgumentNullException.ThrowIfNull(memoryCoordinator);
        _memoryCoordinator = memoryCoordinator;
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _hooks = new CrewHookDispatcher(hook, logger);
        _lifecycle = dependencies.LifecycleFor(logger);
    }

    /// <summary>
    /// Executes crew tasks using a consensual process. Each task is executed independently by
    /// all agents; each agent then ranks the other agents' anonymised answers
    /// (<see cref="IBallotCollector"/>) and the ballots are tallied to reach consensus on the
    /// best output. <see cref="ExecuteSequentialAsync"/> is the <see cref="IProcessStrategy"/>
    /// entry point and delegates here.
    /// </summary>
    /// <param name="crew">The crew to execute.</param>
    /// <param name="inputVariables">The crew's input variables, passed to every agent execution and ballot.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The crew output after consensus is reached.</returns>
    public Task<DomainCrewOutput> ExecuteConsensualAsync(
        DomainCrew crew,
        IReadOnlyDictionary<string, string>? inputVariables = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(crew);
        return ExecuteConsensualCoreAsync(crew, inputVariables ?? new Dictionary<string, string>(), ct);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Runs the consensual voting pipeline over the crew's tasks. The input variables reach
    /// every agent execution and every ballot (GAP-04).
    /// </remarks>
    public Task<DomainCrewOutput> ExecuteSequentialAsync(
        DomainCrew crew,
        IReadOnlyDictionary<string, string>? inputVariables = null,
        CancellationToken cancellationToken = default)
        => ExecuteConsensualAsync(crew, inputVariables, cancellationToken);

    /// <inheritdoc />
    public Task<DomainCrewOutput> ExecuteHierarchicalAsync(
        DomainCrew crew,
        AgentId? managerAgentId,
        IReadOnlyDictionary<string, string>? inputVariables = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Use HierarchicalProcessStrategy for hierarchical orchestration.");

    /// <inheritdoc />
    public Task<DomainCrewOutput> ExecuteParallelAsync(
        DomainCrew crew,
        IReadOnlyDictionary<string, string>? inputVariables = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Use ParallelProcessStrategy for parallel orchestration.");

    /// <inheritdoc />
    public Task<DomainCrewOutput> ExecuteAutonomousAsync(
        DomainCrew crew,
        AgentExecutionBudget budget,
        IReadOnlyDictionary<string, string>? inputVariables = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Use AutonomousProcessStrategy for autonomous orchestration.");

    private async Task<DomainCrewOutput> ExecuteConsensualCoreAsync(
        DomainCrew crew,
        IReadOnlyDictionary<string, string> inputVariables,
        CancellationToken ct)
    {
        LogStartingConsensualExecutionForCrew(crew.Id);

        var startTime = DateTime.UtcNow;
        var domainResults = new List<DomainTaskOutput>();
        var taskSnapshots = new List<TaskExecutionSnapshot>();
        var applicationOutputs = new List<ApplicationTaskOutput>();

        // Token telemetry propagation (R10.8) — same metadata channel as Sequential.
        // Consensus burns tokens with EVERY agent in EVERY voting round (plus fallback
        // re-execution), so the real cost is the sum of all executions, not just the
        // winning result.
        var tokenTally = new TokenUsageTally();
        var outcome = new CrewRunOutcome(_lifecycle, ct);

        // The terminal event goes out on EVERY exit — setup included: "consensus not
        // reached" was the only failure this mode reported, and a throwing round, a Ctrl+C
        // or an agent-less crew escaped silently.
        try
        {
        // ManagerDecision needs an arbiter: without one the crew is refused before any agent
        // runs, not after it paid for every round (GAP-04).
        var arbiter = await ResolveArbiterAsync(crew, ct).ConfigureAwait(false);

        // Load all agents. A declared manager arbitrates; it neither answers nor votes.
        var agents = new List<DomainAgent>();
        foreach (var agentId in crew.Agents)
        {
            if (agentId == crew.ManagerAgentId)
                continue;
            var agent = await _agentRepository.GetByIdAsync(agentId, ct).ConfigureAwait(false);
            if (agent != null) agents.Add(agent);
        }

        if (agents.Count == 0)
            throw new InvalidOperationException("No agents available for consensual execution");

        // Execute each task with consensus, in the declared order sorted on the tasks' dependencies
        // (STUDIO-12 C2) — never a plan's (GAP-31).
        var taskIds = await CrewTaskSequencer.ResolveAsync(
            crew, _taskRepository, _logger, ct).ConfigureAwait(false);

        foreach (var taskId in taskIds)
        {
            ct.ThrowIfCancellationRequested();

            var task = await _taskRepository.GetByIdAsync(taskId, ct).ConfigureAwait(false);
            if (task == null)
            {
                LogTaskNotFoundSkipping(taskId);
                continue;
            }

            // A task depending on one that did not succeed is skipped, as in Sequential
            // (GAP-03): no agent runs it, no vote is held.
            if (outcome.BlockingDependency(task) is { } blockedBy)
            {
                var skipReason = await outcome.RecordSkipAsync(task, ConsensusRole, blockedBy).ConfigureAwait(false);
                LogTaskSkippedAfterDependency(task.Id, blockedBy);
                var (skippedDomain, skippedApp) = CrewRunOutcome.SkippedOutputs(task.Id, ConsensusRole, blockedBy);
                domainResults.Add(skippedDomain);
                applicationOutputs.Add(skippedApp);
                var skipped = CrewRunOutcome.SkippedSnapshot(task.Id, ConsensusRole, skipReason);
                taskSnapshots.Add(skipped);
                await _hooks.TaskCompletedAsync(skipped, ct).ConfigureAwait(false);
                continue;
            }

            LogStartingConsensualExecutionForTask(taskId);

            // Consensus has no single author: the vote is the agent, on the start as on the end.
            await _hooks.TaskStartedAsync(
                CrewHookDispatcher.Started(task.Id.Value.ToString(), ConsensusRole), ct)
                .ConfigureAwait(false);
            // The task starts once, not once per candidate; every agent answers it, so each starts
            // it too (GAP-21).
            await outcome.RecordStartWithoutAgentAsync(task, agents).ConfigureAwait(false);

            // The loop is sequential, so the tally's delta around one task IS what the
            // whole vote cost — every agent, every round — not just the winning
            // execution the task result carries (review, RC2-FEAT-06 lot 7).
            var tokensBefore = tokenTally.TotalTokens;
            var cacheHitBefore = tokenTally.CacheHitTokens;
            var cacheMissBefore = tokenTally.CacheMissTokens;

            // Null when the Fail fallback was triggered: no consensus, so no result. That is a
            // failed task like any other — its dependants are skipped and the crew fails — not
            // an end of the crew on the spot (GAP-03).
            var vote = new VoteContext(crew, task, agents, arbiter, applicationOutputs, inputVariables, tokenTally);
            var retained = await ExecuteTaskWithConsensusAsync(vote, ct).ConfigureAwait(false);
            var taskResult = retained is null
                ? new TaskResult(
                    false, $"[NO CONSENSUS] {ConsensusNotReached(task.Id)}", null, [], TimeSpan.Zero,
                    Error: ConsensusNotReached(task.Id))
                : await RememberAsync(vote, retained, ct).ConfigureAwait(false);

            // Build application output for context propagation
            var appOutput = new ApplicationTaskOutput(
                TaskId: task.Id.Value.ToString(),
                AgentId: ConsensusRole,
                Content: CrewRunOutcome.RawOutputOf(taskResult),
                CompletedAt: DateTime.UtcNow,
                Success: taskResult.Success,
                ExecutionTime: taskResult.ExecutionTime,
                ToolsUsed: taskResult.ToolsUsed);
            applicationOutputs.Add(appOutput);

            // Build domain output
            var domainOutput = DomainTaskOutput.Create(
                rawOutput: CrewRunOutcome.RawOutputOf(taskResult),
                format: "text",
                formattedOutput: null,
                taskId: task.Id,
                success: taskResult.Success,
                executionTime: taskResult.ExecutionTime,
                structuredOutput: taskResult.StructuredOutput);
            domainResults.Add(domainOutput);

            // Each agent ends the task with its own last answer — or its error —, then the task
            // ends: completed under the author of the retained answer (GAP-21).
            foreach (var agent in agents)
            {
                if (vote.LastAnswers.TryGetValue(agent.Id.ToString(), out var answer))
                    await outcome.RecordAnswerAsync(task, agent, answer).ConfigureAwait(false);
            }

            // The retained result is the task's result: when it failed, the task failed,
            // whatever the vote said (GAP-03).
            if (taskResult.Success)
                await outcome.RecordSuccessAsync(task, domainOutput, retained?.Author).ConfigureAwait(false);
            else
                await outcome.RecordFailureAsync(task, ConsensusRole, taskResult.Error ?? taskResult.LastError).ConfigureAwait(false);

            LogTaskCompletedViaConsensusSuccess(taskId, taskResult.Success);

            var snapshot = new TaskExecutionSnapshot
            {
                TaskId = task.Id.Value.ToString(),
                // Consensus has no single author: the vote is the agent.
                AgentRole = ConsensusRole,
                Success = taskResult.Success,
                Duration = taskResult.ExecutionTime,
                CompletedAt = DateTimeOffset.UtcNow,
                ToolCallCount = taskResult.ToolsUsed?.Count ?? 0,
                TokensUsed = tokenTally.TotalTokens - tokensBefore,
                CacheHitTokens = tokenTally.CacheHitTokens - cacheHitBefore,
                CacheMissTokens = tokenTally.CacheMissTokens - cacheMissBefore,
            };
            taskSnapshots.Add(snapshot);
            await _hooks.TaskCompletedAsync(snapshot, ct).ConfigureAwait(false);
        }
        }
        catch (OperationCanceledException ex)
        {
            await outcome.RecordInterruptionAsync(ex).ConfigureAwait(false);
            await _hooks.CrewFailedAsync(
                CrewHookDispatcher.Snapshot(
                    crew.Id.ToString(), startTime, taskSnapshots, CrewHookStatus.Canceled, "Execution was cancelled."),
                null, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            await outcome.RecordInterruptionAsync(ex).ConfigureAwait(false);
            await _hooks.CrewFailedAsync(
                CrewHookDispatcher.Snapshot(
                    crew.Id.ToString(), startTime, taskSnapshots, CrewHookStatus.Failed, ex.Message),
                ex, CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        var totalExecutionTime = DateTime.UtcNow - startTime;
        var finalOutput = domainResults.LastOrDefault()?.Output ?? string.Empty;

        if (outcome.HasFailures)
            LogConsensualExecutionFailedForCrew(crew.Id, outcome.Failures.Count, outcome.Reason);
        else
            LogConsensualExecutionCompletedForCrew(crew.Id, totalExecutionTime);

        return await outcome.CompleteAsync(
            _hooks, crew.Id.ToString(), startTime, taskSnapshots, domainResults, totalExecutionTime,
            tokenTally.WriteTo(Orkeon.Domain.Crew.ValueObjects.CrewMetadata.CreateBuilder()).Build(),
            finalOutput).ConfigureAwait(false);
    }

    /// <summary>The cause a task reports when the <c>Fail</c> fallback found no consensus.</summary>
    private string ConsensusNotReached(TaskId taskId) =>
        $"Consensus could not be reached for task {taskId} after {_options.MaxVotingRounds} voting round(s)";

    /// <summary>What one task's vote works with, from the first round to the fallback.</summary>
    private sealed record VoteContext(
        DomainCrew Crew,
        CrewTask Task,
        List<DomainAgent> Agents,
        DomainAgent? Arbiter,
        List<ApplicationTaskOutput> PreviousOutputs,
        IReadOnlyDictionary<string, string> InputVariables,
        TokenUsageTally TokenTally)
    {
        /// <summary>A fresh context under the crew's id: its input variables, the outputs so far.</summary>
        public SimpleExecutionContext ExecutionContext(IMemoryScope memory, CancellationToken ct) =>
            new(Crew.Id, new Dictionary<string, string>(InputVariables), memory, PreviousOutputs, ct);

        /// <summary>
        /// The context of a candidate answer or of a ballot: neither is the task's result, so
        /// neither goes to the crew's memory (GAP-20) — the retained answer does, once. A candidate
        /// answers the task and recalls the crew's memories; a ballot does not
        /// (<c>AgentBallotCollector</c>, GAP-30).
        /// </summary>
        public SimpleExecutionContext VotingContext(IMemoryScope memory, CancellationToken ct) =>
            ExecutionContext(memory, ct) with { StoreResultInMemory = false };

        /// <summary>
        /// Each agent's answer in the last round held, keyed by agent id: what the agent ends the
        /// task with (GAP-21). Empty until a round is held.
        /// </summary>
        public Dictionary<string, TaskResult> LastAnswers { get; set; } = [];
    }

    /// <summary>
    /// What a task's vote kept: the result, and the agent that wrote it — none when the result is
    /// a failure no agent's answer stands for.
    /// </summary>
    private sealed record RetainedAnswer(TaskResult Result, DomainAgent? Author)
    {
        /// <summary>The answer of the agent keyed <paramref name="agentKey"/>, retained.</summary>
        public static RetainedAnswer Of(VoteContext vote, string agentKey, TaskResult result) =>
            new(result, vote.Agents.FirstOrDefault(a => a.Id.ToString() == agentKey));

        /// <summary>A failed result: no answer retained.</summary>
        public static RetainedAnswer Failed(TaskResult result) => new(result, Author: null);
    }

    /// <summary>
    /// One counted round: the executions, the anonymised candidates in the order the voters saw
    /// them, and the tally of the ballots.
    /// </summary>
    private sealed record CountedRound(
        Dictionary<string, TaskResult> Results,
        List<string> CandidateOrder,
        Dictionary<string, string> LabelByKey,
        Dictionary<string, string> KeyByLabel,
        VoteResult Tally);

    /// <summary>
    /// The arbiter of <see cref="ConsensusFallback.ManagerDecision"/>: the crew's manager agent.
    /// Null for the other fallbacks. A crew without a manager is refused before any agent runs.
    /// </summary>
    private async Task<DomainAgent?> ResolveArbiterAsync(DomainCrew crew, CancellationToken ct)
    {
        if (_options.FallbackStrategy != ConsensusFallback.ManagerDecision)
            return null;

        if (crew.ManagerAgentId is null)
            throw new InvalidOperationException(
                $"Orkeon:Consensus:FallbackStrategy is ManagerDecision, but crew {crew.Id} declares no manager agent " +
                "to decide: declare one (managerAgent in YAML, CrewBuilder.WithManager in C#) or choose the " +
                "AcceptBestScore or Fail fallback.");

        return await _agentRepository.GetByIdAsync(crew.ManagerAgentId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"The manager agent {crew.ManagerAgentId} of crew {crew.Id} was not found; ManagerDecision has no arbiter.");
    }

    private async Task<RetainedAnswer?> ExecuteTaskWithConsensusAsync(VoteContext vote, CancellationToken ct)
    {
        var task = vote.Task;
        var maxRounds = _options.MaxVotingRounds;
        Dictionary<string, TaskResult>? previousResults = null;
        CountedRound? lastRound = null;

        for (int round = 1; round <= maxRounds; round++)
        {
            LogTaskStartingVotingRound(task.Id, round, maxRounds);

            // Execute task with all agents in parallel
            var agentResults = await ExecuteWithAllAgentsAsync(vote, previousResults, ct).ConfigureAwait(false);
            vote.LastAnswers = agentResults;

            // Record the cost of the whole round: every agent execution consumed tokens,
            // whichever result ends up winning the vote.
            foreach (var agentResult in agentResults.Values)
                vote.TokenTally.Record(agentResult);

            // Only an execution that succeeded is a candidate (GAP-04): a failed one can never win.
            var candidates = vote.Agents
                .Select(a => a.Id.ToString())
                .Where(key => agentResults.TryGetValue(key, out var r) && r.Success)
                .ToList();

            // Every execution failed: there is nothing to vote on, and the task fails with the
            // agents' own error — another round would only pay for the same failure again.
            if (candidates.Count == 0)
                return RetainedAnswer.Failed(agentResults.Values.First(r => !r.Success));

            // A lone successful answer has no rival to be weighed against: it is retained
            // without a ballot.
            if (candidates.Count == 1)
                return RetainedAnswer.Of(vote, candidates[0], agentResults[candidates[0]]);

            lastRound = await HoldBallotAsync(vote, round, agentResults, candidates, ct).ConfigureAwait(false);
            var voteResult = lastRound.Tally;

            LogTaskRoundResultConsensusreachedWinningchoice(task.Id, round, voteResult.ConsensusReached, voteResult.WinningChoice ?? "(none)", voteResult.AgreementScore);

            if (voteResult.ConsensusReached && voteResult.WinningChoice != null
                && agentResults.TryGetValue(voteResult.WinningChoice, out var winningResult))
            {
                return RetainedAnswer.Of(vote, voteResult.WinningChoice, winningResult);
            }

            // No consensus, prepare for next round
            if (round < maxRounds && _options.EnableDiscussion)
            {
                LogTaskNoConsensusInRound(task.Id, round);
                previousResults = agentResults;
            }
        }

        // No round was held (MaxVotingRounds below 1): no consensus.
        if (lastRound is null)
            return null;

        // Max rounds exhausted, apply fallback
        return await ApplyFallbackAsync(vote, lastRound, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// One round of peer ballots (GAP-04): the successful answers get labels in an order
    /// shuffled per task and round, every agent ranks the answers of the others, and the
    /// ballots become votes for the configured <see cref="IVotingStrategy"/>.
    /// </summary>
    private async Task<CountedRound> HoldBallotAsync(
        VoteContext vote,
        int round,
        Dictionary<string, TaskResult> results,
        List<string> candidates,
        CancellationToken ct)
    {
        var order = ShuffledOrder(candidates, vote.Task.Id, round);
        var labelByKey = order.Select((key, i) => (key, label: Label(i))).ToDictionary(p => p.key, p => p.label);
        var keyByLabel = labelByKey.ToDictionary(p => p.Value, p => p.Key);

        // Voters are read in the shuffled order too, so nothing in the count follows the
        // order the agents were declared in.
        var voters = vote.Agents
            .OrderBy(a => order.IndexOf(a.Id.ToString()) is var i and >= 0 ? i : int.MaxValue)
            .ToList();

        var ballots = await System.Threading.Tasks.Task.WhenAll(voters.Select(async voter =>
        {
            var voterKey = voter.Id.ToString();
            // An agent never ranks its own answer.
            var offered = order.Where(key => key != voterKey)
                .Select(key => new BallotCandidate { Label = labelByKey[key], Output = results[key].Output })
                .ToImmutableList();
            var ballot = await CollectBallotAsync(vote, voter, offered, ct).ConfigureAwait(false);
            return (voter, ballot);
        })).ConfigureAwait(false);

        var ranked = _options.VotingOptions.ConsensusType == ConsensusType.BordaCount;
        var votes = new List<Vote>();
        foreach (var (voter, ballot) in ballots)
        {
            vote.TokenTally.Record(ballot.Execution);
            var voterKey = voter.Id.ToString();
            // Only the labels offered to this voter count: whatever a collector returns, a
            // voter's own answer never receives its vote.
            var ranking = ballot.Ranking
                .Select(label => keyByLabel.GetValueOrDefault(label))
                .OfType<string>()
                .Where(key => key != voterKey)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            votes.Add(new Vote
            {
                VoterId = voterKey,
                VoterRole = voter.Role.ToString(),
                // Majority, SuperMajority, Unanimity and WeightedConsensus count the first
                // choice; Borda counts the whole ranking. An abstention is an empty choice.
                Choice = ranking.Count == 0 ? string.Empty : ranked ? string.Join(",", ranking) : ranking[0],
                OwnChoice = labelByKey.ContainsKey(voterKey) ? voterKey : null,
                Confidence = ballot.Confidence,
                Weight = 1.0f,
                Justification = ballot.Justification,
                Timestamp = DateTime.UtcNow
            });
        }

        var abstentions = votes.Count(v => v.Choice.Length == 0);
        LogTaskBallotsCounted(vote.Task.Id, round, votes.Count - abstentions, abstentions, order.Count);

        var tally = await _votingStrategy.TallyVotesAsync(votes, _options.VotingOptions, ct).ConfigureAwait(false);
        return new CountedRound(results, order, labelByKey, keyByLabel, tally);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Per-ballot fault barrier: a ballot that cannot be collected counts as an abstention instead of failing the task (GAP-04).")]
    private async Task<Ballot> CollectBallotAsync(
        VoteContext vote, DomainAgent voter, ImmutableList<BallotCandidate> offered, CancellationToken ct)
    {
        // An agent whose own answer is the only candidate has nobody else to rank.
        if (offered.IsEmpty)
            return Ballot.Abstention("no other candidate to rank");

        try
        {
            return await _ballots.CollectAsync(new BallotRequest
            {
                Voter = voter,
                Task = vote.Task,
                Candidates = offered,
                Context = vote.VotingContext(_memoryScope, ct),
            }, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogBallotCollectionFailed(ex, voter.Role.ToString());
            return Ballot.Abstention($"the ballot could not be collected: {ex.Message}");
        }
    }

    /// <summary>
    /// A deterministic shuffle of the candidates, seeded by the task and the round: the same
    /// run gives the same labels, and no agent is <c>A</c> because it was declared first.
    /// </summary>
    private static List<string> ShuffledOrder(List<string> keys, TaskId taskId, int round)
    {
        var bytes = taskId.ToGuid().ToByteArray();
        var state = BitConverter.ToUInt64(bytes, 0) ^ BitConverter.ToUInt64(bytes, 8) ^ ((ulong)round * 0x9E3779B97F4A7C15UL);
        var order = keys.ToList();
        for (var i = order.Count - 1; i > 0; i--)
        {
            // SplitMix64 step: enough spread for a fair order, no Random instance to seed.
            state += 0x9E3779B97F4A7C15UL;
            var z = state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            z ^= z >> 31;
            var j = (int)(z % (ulong)(i + 1));
            (order[i], order[j]) = (order[j], order[i]);
        }

        return order;
    }

    /// <summary>Candidate labels: <c>A</c> to <c>Z</c>, then <c>C27</c>, <c>C28</c>, ….</summary>
    private static string Label(int index) =>
        index < 26
            ? ((char)('A' + index)).ToString()
            : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"C{index + 1}");

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Per-agent fault barrier: a single agent's task failure is converted into a failed TaskResult so the remaining agents' votes are still tallied.")]
    private async Task<Dictionary<string, TaskResult>> ExecuteWithAllAgentsAsync(
        VoteContext vote,
        Dictionary<string, TaskResult>? discussionContext,
        CancellationToken ct)
    {
        var results = new Dictionary<string, TaskResult>();
        var executionTasks = new List<(string agentKey, System.Threading.Tasks.Task<TaskResult> task)>();

        foreach (var agent in vote.Agents)
        {
            var agentKey = agent.Id.ToString();

            // The crew's input variables reach every execution; the discussion context of the
            // previous round is added over them (GAP-04).
            var context = vote.VotingContext(_memoryScope, ct);
            if (discussionContext != null)
            {
                // Add other agents' previous results as discussion context
                var otherResults = discussionContext
                    .Where(kvp => kvp.Key != agentKey && kvp.Value.Success)
                    .Select(kvp => $"Agent {kvp.Key}: {kvp.Value.Output}")
                    .ToList();

                if (otherResults.Count > 0)
                {
                    context.Variables["discussion_context"] = string.Join("\n---\n", otherResults);
                }
            }

            var capturedAgent = agent;
            executionTasks.Add((agentKey, System.Threading.Tasks.Task.Run(async () =>
            {
                return await _executionService.ExecuteTaskAsync(
                    capturedAgent, vote.Task, context, ct).ConfigureAwait(false);
            }, ct)));
        }

        // Wait for all agents to complete
        await System.Threading.Tasks.Task.WhenAll(executionTasks.Select(t => t.task)).ConfigureAwait(false);

        foreach (var (agentKey, executionTask) in executionTasks)
        {
            try
            {
                results[agentKey] = await executionTask.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogAgentFailedToExecuteTask(ex, agentKey);
                results[agentKey] = new TaskResult(
                    false, string.Empty, null,
                    [],
                    TimeSpan.Zero,
                    ex.Message);
            }
        }

        return results;
    }

    private async Task<RetainedAnswer?> ApplyFallbackAsync(VoteContext vote, CountedRound lastRound, CancellationToken ct)
    {
        LogTaskMaxVotingRoundsExhausted(vote.Task.Id, _options.FallbackStrategy);

        return _options.FallbackStrategy switch
        {
            ConsensusFallback.AcceptBestScore => AcceptBestScore(vote, lastRound),
            ConsensusFallback.ManagerDecision => await AskTheManagerAsync(vote, lastRound, ct).ConfigureAwait(false),
            _ => null
        };
    }

    /// <summary>
    /// The candidate the last count put first, without running anything again (GAP-04). The
    /// task fails when no ballot of that round named a candidate.
    /// </summary>
    private RetainedAnswer AcceptBestScore(VoteContext vote, CountedRound lastRound)
    {
        if (lastRound.Tally.WinningChoice is { } best && lastRound.Results.TryGetValue(best, out var result) && result.Success)
            return RetainedAnswer.Of(vote, best, result);

        var reason = $"{ConsensusNotReached(vote.Task.Id)}, and no ballot of the last round named a candidate to accept";
        return RetainedAnswer.Failed(new TaskResult(false, string.Empty, null, [], TimeSpan.Zero, Error: reason));
    }

    /// <summary>
    /// <see cref="ConsensusFallback.ManagerDecision"/>: the crew's manager ranks every candidate
    /// of the last round, anonymised like the peers saw them, and its first choice is retained.
    /// A manager that abstains decides nothing, and the task fails.
    /// </summary>
    private async Task<RetainedAnswer> AskTheManagerAsync(VoteContext vote, CountedRound lastRound, CancellationToken ct)
    {
        var arbiter = vote.Arbiter
            ?? throw new InvalidOperationException("ManagerDecision reached the fallback without an arbiter.");

        var offered = lastRound.CandidateOrder
            .Select(key => new BallotCandidate
            {
                Label = lastRound.LabelByKey[key],
                Output = lastRound.Results[key].Output,
            })
            .ToImmutableList();

        var ballot = await CollectBallotAsync(vote, arbiter, offered, ct).ConfigureAwait(false);
        vote.TokenTally.Record(ballot.Execution);

        if (ballot.Ranking.FirstOrDefault(lastRound.KeyByLabel.ContainsKey) is { } choice
            && lastRound.Results.TryGetValue(lastRound.KeyByLabel[choice], out var chosen))
        {
            LogTaskDecidedByManager(vote.Task.Id, arbiter.Role.Value, choice);
            return RetainedAnswer.Of(vote, lastRound.KeyByLabel[choice], chosen);
        }

        var reason = $"{ConsensusNotReached(vote.Task.Id)}, and the manager agent {arbiter.Role} chose none of the "
            + $"{offered.Count} answers: {ballot.Justification ?? "it abstained"}";
        return RetainedAnswer.Failed(new TaskResult(false, string.Empty, null, [], TimeSpan.Zero, Error: reason));
    }

    /// <summary>
    /// The retained answer, and nothing else of the vote, goes to the crew's memory — once, under
    /// the agent that wrote it (GAP-20): the candidates and the ballots ran with
    /// <see cref="SimpleExecutionContext.StoreResultInMemory"/> off. A crew without memory stores
    /// nothing, and a store that fails is a warning of the coordinator: the retained answer stays
    /// the task's result, as in every other mode (GAP-30).
    /// </summary>
    private async Task<TaskResult> RememberAsync(VoteContext vote, RetainedAnswer retained, CancellationToken ct)
    {
        var result = retained.Result;
        if (retained.Author is not null && result.Success && !string.IsNullOrEmpty(result.Output))
        {
            await _memoryCoordinator.StoreTaskResultAsync(
                retained.Author, vote.Task, result.Output, vote.ExecutionContext(_memoryScope, ct), ct).ConfigureAwait(false);
        }

        return result;
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Task {TaskId}: no consensus, the manager agent {Manager} chose candidate {Label}")]
    private partial void LogTaskDecidedByManager(TaskId taskId, string manager, string label);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Starting consensual execution for crew {CrewId}")]
    private partial void LogStartingConsensualExecutionForCrew(CrewId crewId);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Task {TaskId} not found, skipping")]
    private partial void LogTaskNotFoundSkipping(TaskId taskId);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Starting consensual execution for task {TaskId}")]
    private partial void LogStartingConsensualExecutionForTask(TaskId taskId);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Task {TaskId} completed via consensus, success: {Success}")]
    private partial void LogTaskCompletedViaConsensusSuccess(TaskId taskId, bool success);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Error, Message = "Consensual execution of crew {CrewId} failed: {FailedTasks} task(s) did not succeed. {Reason}")]
    private partial void LogConsensualExecutionFailedForCrew(CrewId crewId, int failedTasks, string reason);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Task {TaskId} skipped: it depends on task {DependencyId}, which did not succeed")]
    private partial void LogTaskSkippedAfterDependency(TaskId taskId, TaskId dependencyId);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Consensual execution completed for crew {CrewId} in {Duration}")]
    private partial void LogConsensualExecutionCompletedForCrew(CrewId crewId, TimeSpan duration);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Task {TaskId}: Starting voting round {Round}/{MaxRounds}")]
    private partial void LogTaskStartingVotingRound(TaskId taskId, int round, int maxRounds);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Task {TaskId}: Round {Round} result - ConsensusReached: {Consensus}, WinningChoice: {Winner}, AgreementScore: {Score:F1}%")]
    private partial void LogTaskRoundResultConsensusreachedWinningchoice(TaskId taskId, int round, bool consensus, string winner, double score);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Task {TaskId}: No consensus in round {Round}, starting discussion round")]
    private partial void LogTaskNoConsensusInRound(TaskId taskId, int round);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Agent {AgentId} failed to execute task")]
    private partial void LogAgentFailedToExecuteTask(Exception ex, string agentId);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Task {TaskId}: Max voting rounds exhausted, applying fallback: {Fallback}")]
    private partial void LogTaskMaxVotingRoundsExhausted(TaskId taskId, ConsensusFallback fallback);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Ballot of agent {Voter} could not be collected and counts as an abstention")]
    private partial void LogBallotCollectionFailed(Exception ex, string voter);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Task {TaskId}: round {Round} ballots - {Expressed} expressed, {Abstentions} abstention(s) over {Candidates} candidate(s)")]
    private partial void LogTaskBallotsCounted(TaskId taskId, int round, int expressed, int abstentions, int candidates);

}
