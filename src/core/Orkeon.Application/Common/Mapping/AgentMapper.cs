using System.Collections.Immutable;
using Orkeon.Application.Agent.DTOs;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Agent.ValueObjects;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Common;
using Orkeon.Domain.Constants.Agent;
using Orkeon.Application.Constants.Execution;

namespace Orkeon.Application.Common.Mapping;

/// <summary>
/// Simplified mapper for converting between DomainAgent domain entities and DTOs.
/// Uses only the properties that actually exist in the current Domain implementation.
/// </summary>
public static class AgentMapper
{
    /// <summary>
    /// Converts an DomainAgent domain entity to AgentDto.
    /// </summary>
    public static AgentDto ToDto(DomainAgent agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        return new AgentDto
        {
            Id = agent.Id.ToString(),
            Name = agent.Role, // Name defaults to role
            Role = agent.Role,
            Goal = agent.Goal,
            Backstory = agent.Backstory?.Value ?? string.Empty,
            Type = "standard",
            Status = "Active",
            Verbose = agent.Verbose,
            AllowDelegation = agent.AllowDelegation,
            MaxExecutionTime = ExecutionDefaults.DefaultMaxExecutionSeconds,
            Tools = agent.Tools.Select(t => t.Name).ToImmutableList(),
            Llm = null, // LlmConfig not stored on domain entity
            Capabilities = null, // Not implemented in current Domain
            PerformanceMetrics = null,
            CreatedAt = agent.CreatedAt,
            UpdatedAt = agent.UpdatedAt
        };
    }

    /// <summary>
    /// Converts a list of DomainAgent domain entities to AgentDto list.
    /// </summary>
    public static IReadOnlyList<AgentDto> ToDto(IEnumerable<DomainAgent> agents)
    {
        return agents.Select(ToDto).ToList();
    }

    /// <summary>
    /// Creates a simple DomainAgent from CreateAgentRequest.
    /// Uses Agent.Create factory method with proper value objects.
    /// </summary>
    public static DomainAgent CreateFromRequest(CreateAgentRequest request, IBaseTool[] tools)
    {
        ArgumentNullException.ThrowIfNull(request);
        var builder = new AgentBuilder()
            .Role(AgentRole.From(request.Role))
            .Goal(AgentGoal.From(request.Goal))
            .AllowDelegation(request.Settings?.AllowDelegation ?? false)
            .MaxIterations(request.Settings?.MaxIterations ?? AgentDefaults.MaxIterations)
            .MaxRpm(request.Settings?.MaxRPM.HasValue == true ? (int)request.Settings.MaxRPM.Value : AgentDefaults.MaxRequestsPerMinute)
            .Verbose(request.Settings?.Verbose ?? false);

        if (!string.IsNullOrEmpty(request.Backstory))
            builder.Backstory(request.Backstory);

        // Assign tools to the agent via builder
        if (tools != null && tools.Length > 0)
        {
            builder.WithTools(tools.OfType<ITool>());
        }

        return builder.Build();
    }

    /// <summary>
    /// Creates a simple AgentDto for summary views.
    /// </summary>
    public static AgentDto ToSummaryDto(DomainAgent agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        return new AgentDto
        {
            Id = agent.Id.ToString(),
            Name = agent.Role,
            Role = agent.Role,
            Goal = agent.Goal,
            Backstory = agent.Backstory?.Value ?? string.Empty,
            Type = "standard",
            Status = "Active",
            Verbose = agent.Verbose,
            AllowDelegation = agent.AllowDelegation,
            CreatedAt = agent.CreatedAt,
            UpdatedAt = agent.UpdatedAt
        };
    }
}
