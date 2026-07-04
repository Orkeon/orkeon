using System.Collections.Immutable;
using DomainCrew = Orkeon.Domain.Crew.Crew;
using Orkeon.Application.Crew.DTOs;
using Orkeon.Application.Agent.DTOs;
using Orkeon.Application.Task.DTOs;

namespace Orkeon.Application.Common.Mapping;

/// <summary>
/// Comprehensive mapper for converting between Crew domain entities and DTOs.
/// </summary>
public static class CrewMapper
{
    /// <summary>
    /// Converts a Crew domain entity to CrewDto.
    /// </summary>
    public static CrewDto ToDto(DomainCrew crew)
    {
        ArgumentNullException.ThrowIfNull(crew);

        return new CrewDto
        {
            Id = crew.Id?.Value.ToString() ?? string.Empty,
            Name = crew.Goal?.ToString() ?? string.Empty,
            Description = crew.Goal ?? string.Empty,
            ProcessType = MapProcessTypeToString(crew.ProcessType),
            Status = MapCrewStatusToString(crew.Status),
            Verbosity = crew.Verbose ? "verbose" : "normal",
            Agents = crew.Agents.Select(id => new AgentDto
            {
                Id = id.Value.ToString(),
                Name = string.Empty,
                Role = string.Empty,
                Goal = string.Empty,
                Backstory = string.Empty,
                Type = "standard",
                Status = "Active"
            }).ToImmutableList(),
            Tasks = crew.Tasks.Select(id => new TaskDto
            {
                Id = id.Value.ToString(),
                Name = string.Empty,
                Description = string.Empty,
                ExpectedOutput = string.Empty,
                Status = "Pending",
                Priority = "Normal"
            }).ToImmutableList(),
            CreatedAt = crew.CreatedAt
        };
    }

    /// <summary>
    /// Converts a list of Crew domain entities to CrewDto list.
    /// </summary>
    public static IReadOnlyList<CrewDto> ToDto(IEnumerable<DomainCrew> crews)
    {
        if (crews == null)
            return [];

        return crews.Select(ToDto).ToList();
    }

    /// <summary>
    /// Creates a Crew domain entity from CreateCrewRequest.
    /// </summary>
    public static DomainCrew FromCreateRequest(Crew.DTOs.CreateCrewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var crew = DomainCrew.Create(
            !string.IsNullOrWhiteSpace(request.Description) ? request.Description : "Default goal",
            ToProcessTypeDomain(request.Process),
            request.Verbose,
            request.Planning
        );

        return crew;
    }

    /// <summary>
    /// Updates an existing Crew domain entity from UpdateCrewRequest.
    /// </summary>
    public static DomainCrew UpdateFromRequest(DomainCrew crew, Crew.DTOs.UpdateCrewRequest request)
    {
        ArgumentNullException.ThrowIfNull(crew);
        ArgumentNullException.ThrowIfNull(request);

        if (!string.IsNullOrWhiteSpace(request.Description))
        {
            crew.UpdateGoal(request.Description);
        }

        return crew;
    }

    /// <summary>
    /// Creates a summary CrewDto for list views.
    /// </summary>
    public static CrewDto ToSummaryDto(DomainCrew crew)
    {
        ArgumentNullException.ThrowIfNull(crew);

        return new CrewDto
        {
            Id = crew.Id?.Value.ToString() ?? string.Empty,
            Name = crew.Goal?.ToString() ?? string.Empty,
            Description = crew.Goal ?? string.Empty,
            ProcessType = MapProcessTypeToString(crew.ProcessType),
            Status = MapCrewStatusToString(crew.Status),
            Verbosity = crew.Verbose ? "verbose" : "normal",
            CreatedAt = crew.CreatedAt
        };
    }

    /// <summary>
    /// Converts CrewInput DTO to domain CrewInput.
    /// </summary>
    public static Orkeon.Domain.Crew.CrewInput ToCrewInput(Orkeon.Application.Interfaces.Services.CrewInput input)
    {
        if (input == null)
            return new Orkeon.Domain.Crew.CrewInput("Default context", []);

        return new Orkeon.Domain.Crew.CrewInput(
            input.InitialContext ?? "Default context",
            new Dictionary<string, object>(input.Variables ?? new Dictionary<string, object>())
        );
    }

    /// <summary>
    /// Converts domain CrewOutput to CrewOutput DTO.
    /// </summary>
    public static Orkeon.Application.Interfaces.Services.CrewOutput ToCrewOutputDto(Orkeon.Domain.Crew.CrewOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);

        var taskOutputs = output.TaskOutputs?.Select(taskOutput =>
            new Application.Execution.TaskOutput(
                taskOutput.TaskId?.ToString() ?? string.Empty,
                string.Empty,
                taskOutput.RawOutput ?? string.Empty,
                DateTime.UtcNow,
                true,
                TimeSpan.Zero,
                null
            )).ToList() ?? [];

        return new Application.Interfaces.Services.CrewOutput(
            output.Output,
            taskOutputs,
            TimeSpan.Zero,
            ExtractTokenUsage(output)
        );
    }

    /// <summary>
    /// Rebuilds the token usage from the telemetry carried in the domain crew metadata
    /// (canonical keys on <see cref="Domain.Crew.ValueObjects.CrewMetadata"/>). Returns
    /// null when nothing was measured — never a fabricated zero (R10.8 / MAT-004).
    /// </summary>
    private static Application.Interfaces.Services.TokenUsage? ExtractTokenUsage(
        Orkeon.Domain.Crew.CrewOutput output)
    {
        var metadata = output.Metadata;
        if (!metadata.Contains(Domain.Crew.ValueObjects.CrewMetadata.TotalTokensKey))
            return null;

        var promptTokens = metadata.Contains(Domain.Crew.ValueObjects.CrewMetadata.PromptTokensKey)
            ? metadata.Get<int>(Domain.Crew.ValueObjects.CrewMetadata.PromptTokensKey)
            : 0;
        var completionTokens = metadata.Contains(Domain.Crew.ValueObjects.CrewMetadata.CompletionTokensKey)
            ? metadata.Get<int>(Domain.Crew.ValueObjects.CrewMetadata.CompletionTokensKey)
            : 0;

        return new Application.Interfaces.Services.TokenUsage(
            promptTokens,
            completionTokens,
            metadata.Get<int>(Domain.Crew.ValueObjects.CrewMetadata.TotalTokensKey));
    }

    private static string MapProcessTypeToString(Domain.SharedKernel.ValueObjects.ProcessType processType)
    {
        return processType.Value switch
        {
            "Sequential" => "Sequential",
            "Parallel" => "Parallel",
            "Hierarchical" => "Hierarchical",
            "Consensual" => "Consensual",
            _ => "Sequential"
        };
    }

    /// <summary>
    /// Converts DTO ProcessType enum to domain ProcessType.
    /// </summary>
    private static Domain.SharedKernel.ValueObjects.ProcessType ToProcessTypeDomain(ProcessType processType)
    {
        return processType switch
        {
            ProcessType.Sequential => Domain.SharedKernel.ValueObjects.ProcessType.Sequential,
            ProcessType.Parallel => Domain.SharedKernel.ValueObjects.ProcessType.Parallel,
            ProcessType.Hierarchical => Domain.SharedKernel.ValueObjects.ProcessType.Hierarchical,
            ProcessType.Consensual => Domain.SharedKernel.ValueObjects.ProcessType.Consensual,
            _ => Domain.SharedKernel.ValueObjects.ProcessType.Sequential
        };
    }

    private static string MapCrewStatusToString(Domain.Crew.ValueObjects.CrewStatus crewStatus)
    {
        return crewStatus.Value switch
        {
            "Created" => "Idle",
            "Idle" => "Idle",
            "Initializing" => "Executing",
            "Executing" => "Executing",
            "Paused" => "Paused",
            "Completed" => "Completed",
            "Failed" => "Failed",
            "Cancelled" => "Cancelled",
            "Error" => "Failed",
            _ => "Idle"
        };
    }
}
