using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orkeon.Application.Interfaces.AgentCommunication;
using Orkeon.Domain.AgentCommunication;

namespace Orkeon.Host;

/// <summary>
/// The A2A router of <c>orkeon-host</c> (GAP-23): one skill per crew <c>Orkeon:Host:A2A</c>
/// exposes, and a task is a run of that crew — exactly what a chat message is — through
/// <see cref="ICrewRunner"/>: the task's input is the run's need, and the run goes under the
/// crew's mounts, inside its concurrency bound (the one the chat channel counts against too),
/// under the host's deadline, logged with its origin <c>a2a:&lt;task id&gt;</c>.
/// <para>
/// Not the agent router of GAP-10, which runs an agent found by id in a directory filled before
/// the request. The daemon loads a fresh crew for every run and keeps none, so there is no
/// directory to fill: the skills come from the configuration, and the key the card publishes —
/// the crew's declared name — is the key this router compares, exactly.
/// </para>
/// </summary>
internal sealed partial class HostedCrewA2ARouter : IA2ATaskRouter
{
    private readonly ICrewRunner _runner;
    private readonly CrewHostRegistry _registry;
    private readonly ILogger<HostedCrewA2ARouter> _logger;
    private readonly Lazy<IReadOnlyList<HostedCrewOptions>> _exposed;

    /// <summary>Builds the router over the host's runner, its registry and its options.</summary>
    public HostedCrewA2ARouter(
        ICrewRunner runner,
        CrewHostRegistry registry,
        IOptions<OrkeonHostOptions> options,
        ILogger<HostedCrewA2ARouter> logger)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        ArgumentNullException.ThrowIfNull(options);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Resolved on first use: HostA2AService checked the section when the host started, and
        // a host whose section is refused never serves a request.
        var hostOptions = options.Value;
        _exposed = new(() => hostOptions.A2A.ExposedCrews(hostOptions.Crews));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AgentSkill>> GetSkillsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<AgentSkill>>([.. _exposed.Value.Select(ToSkill)]);

    /// <inheritdoc />
    public Task<A2ATaskResponse> RouteTaskAsync(A2ATaskRequest request, IProgress<string>? progress, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return RouteCoreAsync(request, progress, ct);
    }

    private async Task<A2ATaskResponse> RouteCoreAsync(A2ATaskRequest request, IProgress<string>? progress, CancellationToken ct)
    {
        // Exactly the published id: a crew the section does not expose is no skill at all, and a
        // case-changed name is not the id the card gave.
        var crew = _exposed.Value.FirstOrDefault(c => string.Equals(c.Name, request.SkillId, StringComparison.Ordinal));
        if (crew is null)
        {
            LogUnknownSkill(request.Id, request.SkillId);
            return Failed(request,
                $"No skill has the id '{request.SkillId}': this host exposes "
                + $"{string.Join(", ", _exposed.Value.Select(c => $"'{c.Name}'"))} "
                + "(the 'id' of each skill GET /.well-known/agent.json lists).");
        }

        if (string.IsNullOrWhiteSpace(request.Input))
            return Failed(request, "The task input is empty: there is nothing for the crew to do.");

        // A hosted run stops through the registry alone — the runner takes no token, by design —
        // so the token the task runs under (DELETE /a2a/tasks/{id}, the server stopping) is bridged
        // to the run's own stop the moment the run exists.
        var stop = default(CancellationTokenRegistration);
        try
        {
            var result = await _runner.RunAsync(
                crew.Name,
                request.Input,
                $"a2a:{request.Id}",
                // A peer following the task (sendSubscribe) reads the lines a chat thread reads —
                // "Running '<crew>'…", then one per finished task — as they come (GAP-35).
                onProgress: progress is null ? null : progress.Report,
                onStarted: runId =>
                {
                    stop = ct.Register(static state =>
                    {
                        var (registry, id) = ((CrewHostRegistry, string))state!;
                        registry.RequestStop(id);
                    }, (_registry, runId));
                    return Task.CompletedTask;
                },
                variables: request.Metadata).ConfigureAwait(false);

            return Answer(request, result);
        }
        finally
        {
            await stop.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// What the run became, in A2A terms. The message is the one a chat thread gets: the answer,
    /// or why there is none — never the detail the host log keeps (paths, endpoints).
    /// </summary>
    private static A2ATaskResponse Answer(A2ATaskRequest request, HostedRunResult result) => result.Outcome switch
    {
        HostedRunOutcome.Completed => new A2ATaskResponse
        {
            TaskId = request.Id,
            Status = A2ATaskStatus.Completed,
            Output = result.Message,
            Timestamp = DateTime.UtcNow,
        },
        HostedRunOutcome.Cancelled => new A2ATaskResponse
        {
            TaskId = request.Id,
            Status = A2ATaskStatus.Cancelled,
            Error = result.Message,
            Timestamp = DateTime.UtcNow,
        },
        // Failed, and the refusals: the crew at its bound (the peer may retry shortly), the host
        // stopping (the peer may retry once it is back — GAP-35), or a crew gone.
        _ => Failed(request, result.Message),
    };

    private static A2ATaskResponse Failed(A2ATaskRequest request, string error) => new()
    {
        TaskId = request.Id,
        Status = A2ATaskStatus.Failed,
        Error = error,
        Timestamp = DateTime.UtcNow,
    };

    private static AgentSkill ToSkill(HostedCrewOptions crew) => new()
    {
        Id = crew.Name,
        Name = crew.Name,
        Description = string.IsNullOrWhiteSpace(crew.Description)
            ? $"Runs the hosted crew '{crew.Name}' on the task's input."
            : crew.Description,
        Tags = ["crew"],
    };

    [LoggerMessage(Level = LogLevel.Warning, Message = "A2A task {TaskId} refused: no exposed crew has the skill id {SkillId}")]
    private partial void LogUnknownSkill(string taskId, string skillId);
}
