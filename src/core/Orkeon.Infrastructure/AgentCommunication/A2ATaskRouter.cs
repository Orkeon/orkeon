using Orkeon.Application.Interfaces.AgentCommunication;
using Orkeon.Domain.Agent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Orkeon.Infrastructure.AgentCommunication;

/// <summary>
/// Routes incoming A2A task requests to matching local agents based on skill matching.
/// Matches the requested skill ID/name against agent roles.
/// <para>
/// R4.6 / ANT-001: this router is a singleton, so it never captures the scoped
/// <see cref="IAgentRepository"/>. Instead it opens a DI scope per routed task via
/// <see cref="IServiceScopeFactory"/> and resolves the repository inside that scope.
/// The repository hydrates from the shared <c>IAgentRegistrationStore</c> singleton,
/// so agents registered by other scopes (e.g. the execution pipeline) are visible here.
/// </para>
/// </summary>
public partial class A2ATaskRouter : IA2ATaskRouter
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger _logger;

    /// <summary>Initializes a new instance of <see cref="A2ATaskRouter"/>.</summary>
    /// <param name="scopeFactory">Factory used to open one DI scope per routed task (the scoped <see cref="IAgentRepository"/> is resolved inside it).</param>
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
            // ANT-001: resolve the scoped repository in a dedicated scope per request —
            // a singleton must never hold on to a scoped service (captive dependency).
            // Get all available agents and match by role (skill mapping).
            IReadOnlyList<Orkeon.Domain.Agent.Agent> agents;
            var scope = _scopeFactory.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                var agentRepository = scope.ServiceProvider.GetRequiredService<IAgentRepository>();
                agents = await agentRepository.GetAvailableAgentsAsync(ct).ConfigureAwait(false);
            }

            var matchingAgent = agents.FirstOrDefault(a =>
                string.Equals(a.Role.Value, request.SkillId, StringComparison.OrdinalIgnoreCase) ||
                a.Role.Value.Contains(request.SkillId, StringComparison.OrdinalIgnoreCase));

            if (matchingAgent == null)
            {
                LogNoAgentFoundForSkill(request.SkillId, string.Join(", ", agents.Select(a => a.Role.Value)));

                return new A2ATaskResponse
                {
                    TaskId = request.Id,
                    Status = A2ATaskStatus.Failed,
                    Error = $"No agent found with skill matching '{request.SkillId}'",
                    Timestamp = DateTime.UtcNow
                };
            }

            LogMatchedA2ATask(request.Id, matchingAgent.Role.Value);

            // Execute the task by returning a response indicating the match was found.
            // In a full implementation, this would invoke the agent's execution pipeline.
            return new A2ATaskResponse
            {
                TaskId = request.Id,
                Status = A2ATaskStatus.Completed,
                Output = $"Task routed to agent '{matchingAgent.Role.Value}' with input: {request.Input}",
                Timestamp = DateTime.UtcNow
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogErrorRoutingA2ATask(ex, request.Id);

            return new A2ATaskResponse
            {
                TaskId = request.Id,
                Status = A2ATaskStatus.Failed,
                Error = ex.Message,
                Timestamp = DateTime.UtcNow
            };
        }
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Routing A2A task {TaskId} for skill {SkillId}")]
    private partial void LogRoutingA2ATask(string taskId, string skillId);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "No agent found for skill {SkillId}. Available roles: {Roles}")]
    private partial void LogNoAgentFoundForSkill(string skillId, string roles);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Matched A2A task {TaskId} to agent {AgentRole}")]
    private partial void LogMatchedA2ATask(string taskId, string agentRole);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Error, Message = "Error routing A2A task {TaskId}")]
    private partial void LogErrorRoutingA2ATask(Exception ex, string taskId);
}
