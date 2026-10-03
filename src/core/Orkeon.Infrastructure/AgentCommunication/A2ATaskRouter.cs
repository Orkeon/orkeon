using Orkeon.Application.Context;
using Orkeon.Application.Interfaces.AgentCommunication;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Agent;
using Orkeon.Domain.AgentCommunication;
using Orkeon.Domain.Common;
using Orkeon.Domain.Task;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Infrastructure.Stubs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Infrastructure.AgentCommunication;

/// <summary>
/// Runs an incoming A2A task with the local agent it names (GAP-10). The request's
/// <c>skillId</c> must equal, exactly, a skill <c>id</c> of the agent card — the agent's id:
/// <see cref="GetSkillsAsync"/> lists one skill per available agent, and the card publishes
/// what it lists (<c>GET /.well-known/agent.json</c>). The task is an
/// ad hoc <see cref="CrewTask"/> whose description is the request's <c>input</c>; the agent runs
/// it through <see cref="IAgentExecutionService"/>, and the response carries what it produced:
/// <c>Completed</c> with its output, <c>Failed</c> with its error, <c>Cancelled</c> when the
/// request's token fires. Nothing answers <c>Completed</c> without the agent having run.
/// <para>
/// R4.6 / ANT-001: this router is a singleton, so it never captures the scoped
/// <see cref="IAgentRepository"/> or <see cref="IAgentExecutionService"/>. It opens a DI scope
/// per request — the card's listing, each routed task — via <see cref="IServiceScopeFactory"/>
/// and resolves them inside it. The repository hydrates from the shared
/// <c>IAgentRegistrationStore</c> singleton, so agents registered by other scopes (e.g. the
/// execution pipeline) are visible here.
/// </para>
/// </summary>
[Experimental("ORKEXP001", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public partial class A2ATaskRouter : IA2ATaskRouter
{
    private const string AdHocExpectedOutput = "A complete answer to the request, as text.";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger _logger;

    /// <summary>Initializes a new instance of <see cref="A2ATaskRouter"/>.</summary>
    /// <param name="scopeFactory">Factory used to open one DI scope per request — the card's listing, each routed task (the scoped <see cref="IAgentRepository"/> and <see cref="IAgentExecutionService"/> are resolved inside it).</param>
    /// <param name="logger">Optional logger.</param>
    public A2ATaskRouter(
        IServiceScopeFactory scopeFactory,
        ILogger<A2ATaskRouter>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        _scopeFactory = scopeFactory;
        _logger = logger ?? NullLogger<A2ATaskRouter>.Instance;
    }

    /// <inheritdoc />
    /// <remarks>
    /// One skill per available agent: its id (<c>id</c>), its role (<c>name</c>, and as a
    /// lowercase tag), its goal (<c>description</c>), plain text in and out.
    /// </remarks>
    public async Task<IReadOnlyList<AgentSkill>> GetSkillsAsync(CancellationToken ct = default)
    {
        // ANT-001: the repository is scoped; a singleton reads it in a scope of its own.
        var scope = _scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var agents = await scope.ServiceProvider.GetRequiredService<IAgentRepository>()
                .GetAvailableAgentsAsync(ct).ConfigureAwait(false);
            return [.. agents.Select(ToSkill)];
        }
    }

    private static AgentSkill ToSkill(Orkeon.Domain.Agent.Agent agent) => new()
    {
        Id = agent.Id.ToString(),
        Name = agent.Role.Value,
        Description = agent.Goal.Value,
#pragma warning disable CA1308 // lowercase is the required wire/storage form, not a comparison normalization
        Tags = [agent.Role.Value.ToLowerInvariant()],
#pragma warning restore CA1308
    };

    /// <inheritdoc />
    public Task<A2ATaskResponse> RouteTaskAsync(
        A2ATaskRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return RouteTaskAsyncCore(request, ct);
    }

    private async Task<A2ATaskResponse> RouteTaskAsyncCore(
        A2ATaskRequest request, CancellationToken ct)
    {
        LogRoutingA2ATask(request.Id, request.SkillId);

        try
        {
            // ANT-001: resolve the scoped services in a dedicated scope per request —
            // a singleton must never hold on to a scoped service (captive dependency).
            var scope = _scopeFactory.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                var agentRepository = scope.ServiceProvider.GetRequiredService<IAgentRepository>();
                var agents = await agentRepository.GetAvailableAgentsAsync(ct).ConfigureAwait(false);

                // Exact match on the id the agent card publishes as the skill id — never on the
                // role, never on a substring ("writer" must not select "Ghostwriter").
                var agent = agents.FirstOrDefault(a =>
                    string.Equals(a.Id.ToString(), request.SkillId, StringComparison.Ordinal));
                if (agent is null)
                {
                    LogNoAgentFoundForSkill(request.SkillId, agents.Count);
                    return Failed(request,
                        $"No agent publishes the skill id '{request.SkillId}': use the 'id' of a skill " +
                        "listed by GET /.well-known/agent.json.");
                }

                if (string.IsNullOrWhiteSpace(request.Input))
                    return Failed(request, "The task input is empty: there is nothing for the agent to do.");

                var executionService = scope.ServiceProvider.GetService<IAgentExecutionService>();
                if (executionService is null or NullAgentExecutionService)
                {
                    return Failed(request,
                        "No agent execution service is registered, so the agent cannot run: call " +
                        "AddOrkeonApplication() in the host that serves A2A.");
                }

                LogMatchedA2ATask(request.Id, agent.Role.Value);
                return await ExecuteAsync(executionService, agent, request, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return Cancelled(request);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogErrorRoutingA2ATask(ex, request.Id);
            return Failed(request, ex.Message);
        }
    }

    private static async Task<A2ATaskResponse> ExecuteAsync(
        IAgentExecutionService executionService,
        Orkeon.Domain.Agent.Agent agent,
        A2ATaskRequest request,
        CancellationToken ct)
    {
        var task = CrewTask.Create(
            TaskDescription.From(request.Input),
            ExpectedOutput.From(AdHocExpectedOutput));

        var variables = request.Metadata is { } metadata
            ? new Dictionary<string, string>(metadata)
            : [];

        var context = new SimpleExecutionContext(
            CrewId.Create(), variables, NullMemoryScope.Instance, [], ct);

        var result = await executionService.ExecuteTaskAsync(agent, task, context, ct).ConfigureAwait(false);

        if (result.Success)
        {
            return new A2ATaskResponse
            {
                TaskId = request.Id,
                Status = A2ATaskStatus.Completed,
                Output = result.Output,
                Timestamp = DateTime.UtcNow
            };
        }

        if (ct.IsCancellationRequested || result.ExitReason == AgentExitReason.Cancelled)
            return Cancelled(request);

        return Failed(request, result.Error ?? result.LastError ?? "The agent execution failed without an error message.");
    }

    private static A2ATaskResponse Failed(A2ATaskRequest request, string error) => new()
    {
        TaskId = request.Id,
        Status = A2ATaskStatus.Failed,
        Error = error,
        Timestamp = DateTime.UtcNow
    };

    private static A2ATaskResponse Cancelled(A2ATaskRequest request) => new()
    {
        TaskId = request.Id,
        Status = A2ATaskStatus.Cancelled,
        Error = "The task was cancelled before the agent finished.",
        Timestamp = DateTime.UtcNow
    };

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Routing A2A task {TaskId} for skill {SkillId}")]
    private partial void LogRoutingA2ATask(string taskId, string skillId);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "No agent publishes skill id {SkillId} ({AgentCount} available agents)")]
    private partial void LogNoAgentFoundForSkill(string skillId, int agentCount);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Matched A2A task {TaskId} to agent {AgentRole}")]
    private partial void LogMatchedA2ATask(string taskId, string agentRole);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Error, Message = "Error routing A2A task {TaskId}")]
    private partial void LogErrorRoutingA2ATask(Exception ex, string taskId);
}
