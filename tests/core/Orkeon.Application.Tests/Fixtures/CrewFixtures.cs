using Orkeon.Domain.Common;
using Orkeon.Domain.SharedKernel.ValueObjects;
using DomainCrew = Orkeon.Domain.Crew.Crew;

namespace Orkeon.Application.Tests.Fixtures;

/// <summary>
/// Provides test fixtures for creating domain crews.
/// </summary>
public static class CrewFixtures
{
    public static DomainCrew CreateBasicCrew(
        string? goal = null,
        ProcessType? processType = null)
    {
        return DomainCrew.Create(
            goal: goal ?? "Test crew goal",
            processType: processType ?? ProcessType.Sequential
        );
    }

    public static DomainCrew CreateCrewWithAgents(
        string goal,
        params AgentId[] agentIds)
    {
        var crew = DomainCrew.Create(
            goal: goal,
            processType: ProcessType.Sequential
        );

        foreach (var agentId in agentIds)
        {
            crew.AddAgent(agentId);
        }

        return crew;
    }

    public static DomainCrew CreateCrewWithTasks(
        string goal,
        params TaskId[] taskIds)
    {
        var crew = DomainCrew.Create(
            goal: goal,
            processType: ProcessType.Sequential
        );

        foreach (var taskId in taskIds)
        {
            crew.AddTask(taskId);
        }

        return crew;
    }

    public static DomainCrew CreateCrewWithAgentsAndTasks(
        string goal,
        IEnumerable<AgentId> agentIds,
        IEnumerable<TaskId> taskIds,
        ProcessType? processType = null)
    {
        var crew = DomainCrew.Create(
            goal: goal,
            processType: processType ?? ProcessType.Sequential
        );

        foreach (var agentId in agentIds)
        {
            crew.AddAgent(agentId);
        }

        foreach (var taskId in taskIds)
        {
            crew.AddTask(taskId);
        }

        return crew;
    }

    public static DomainCrew CreateSequentialCrew()
    {
        return DomainCrew.Create(
            goal: "Execute tasks in sequential order",
            processType: ProcessType.Sequential
        );
    }

    public static DomainCrew CreateParallelCrew()
    {
        return DomainCrew.Create(
            goal: "Execute tasks in parallel for efficiency",
            processType: ProcessType.Parallel
        );
    }

    public static DomainCrew CreateHierarchicalCrew()
    {
        return DomainCrew.Create(
            goal: "Execute tasks with hierarchical management",
            processType: ProcessType.Hierarchical
        );
    }

    public static DomainCrew CreateConsensualCrew()
    {
        return DomainCrew.Create(
            goal: "Execute tasks with consensus-based decisions",
            processType: ProcessType.Consensual
        );
    }

    public static DomainCrew CreateResearchCrew()
    {
        return DomainCrew.Create(
            goal: "Conduct comprehensive research and analysis",
            processType: ProcessType.Sequential,
            verbose: true
        );
    }

    public static DomainCrew CreateDevelopmentCrew()
    {
        return DomainCrew.Create(
            goal: "Develop and deploy software solutions",
            processType: ProcessType.Parallel
        );
    }

    public static DomainCrew CreateCrewWithFullConfiguration()
    {
        return DomainCrew.Create(
            goal: "Handle complex multi-stage projects",
            processType: ProcessType.Hierarchical,
            verbose: true,
            planning: true
        );
    }

    public static DomainCrew CreateExecutingCrew()
    {
        var crew = CreateBasicCrew("Crew currently executing");
        // Note: Start/Complete/Fail methods don't exist on Crew domain model
        return crew;
    }

    public static DomainCrew CreateCompletedCrew()
    {
        var crew = CreateBasicCrew("Crew that completed execution");
        // Note: Start/Complete/Fail methods don't exist on Crew domain model
        // crew.Complete("Execution completed successfully");
        return crew;
    }

    public static DomainCrew CreateFailedCrew()
    {
        var crew = CreateBasicCrew("Crew that failed execution");
        // Note: Start/Complete/Fail methods don't exist on Crew domain model
        // crew.Fail("Critical error during execution");
        return crew;
    }

    public static List<DomainCrew?> CreateMultipleCrews()
    {
        return
        [
            CreateSequentialCrew(),
            CreateParallelCrew(),
            CreateHierarchicalCrew(),
            CreateConsensualCrew()
        ];
    }

    public static DomainCrew CreateCrewWithCallback()
    {
        return CreateBasicCrew("Crew with callback configuration");
    }
}
