using Orkeon.Domain.Common;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Crew;
using Orkeon.Domain.Task;
using DomainCrew = Orkeon.Domain.Crew.Crew;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainTask = Orkeon.Domain.Task.CrewTask;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;

namespace Orkeon.Domain.Tests.Fixtures;

/// <summary>
/// Test data builders for Crew domain tests.
/// Provides consistent test data for crew scenarios.
/// </summary>
public static class CrewFixtures
{
    /// <summary>
    /// Creates a basic sequential crew.
    /// </summary>
    public static DomainCrew CreateSequentialCrew()
    {
        return new CrewBuilder()
            .Goal("Execute tasks in sequential order")
            .Sequential()
            .Build();
    }

    /// <summary>
    /// Creates a parallel processing crew.
    /// </summary>
    public static DomainCrew CreateParallelCrew()
    {
        return new CrewBuilder()
            .Goal("Execute tasks in parallel for maximum efficiency")
            .Parallel()
            .Verbose()
            .Build();
    }

    /// <summary>
    /// Creates a hierarchical crew with management layer.
    /// </summary>
    public static DomainCrew CreateHierarchicalCrew()
    {
        return new CrewBuilder()
            .Goal("Execute complex project with delegation")
            .Hierarchical(Agents.CreateProjectManager())
            .Verbose()
            .Planning()
            .Build();
    }

    /// <summary>
    /// Creates a consensual decision-making crew.
    /// </summary>
    public static DomainCrew CreateConsensualCrew()
    {
        return new CrewBuilder()
            .Goal("Make decisions through team consensus")
            .Consensual()
            .Planning()
            .Build();
    }

    /// <summary>
    /// Crews with pre-configured agents.
    /// </summary>
    public static class WithAgents
    {
        public static DomainCrew CreateResearchTeam()
        {
            var leadResearcher = Agents.CreateLeadResearcher();
            var dataAnalyst = Agents.CreateDataAnalyst();
            var writer = Agents.CreateTechnicalWriter();

            return new CrewBuilder()
                .Goal("Execute tasks in sequential order")
                .Sequential()
                .WithAgent(leadResearcher)
                .WithAgent(dataAnalyst)
                .WithAgent(writer)
                .Build();
        }

        public static DomainCrew CreateDevelopmentTeam()
        {
            var architect = Agents.CreateSoftwareArchitect();
            var developer1 = Agents.CreateSeniorDeveloper();
            var developer2 = Agents.CreateSeniorDeveloper();
            var tester = Agents.CreateQAEngineer();

            return new CrewBuilder()
                .Goal("Execute tasks in parallel for maximum efficiency")
                .Parallel()
                .Verbose()
                .WithAgent(architect)
                .WithAgent(developer1)
                .WithAgent(developer2)
                .WithAgent(tester)
                .Build();
        }

        public static DomainCrew CreateHierarchicalTeam()
        {
            var manager = Agents.CreateProjectManager();
            var teamLead = Agents.CreateTechLead();
            var developers = Enumerable.Range(1, 3)
                .Select(_ => Agents.CreateSeniorDeveloper())
                .ToList();

            var builder = new CrewBuilder()
                .Goal("Execute complex project with delegation")
                .Hierarchical(manager)
                .Verbose()
                .Planning()
                .WithAgent(manager)
                .WithAgent(teamLead);

            foreach (var dev in developers)
            {
                builder.WithAgent(dev);
            }

            return builder.Build();
        }
    }

    /// <summary>
    /// Crews with pre-configured tasks.
    /// </summary>
    public static class WithTasks
    {
        public static DomainCrew CreateCrewWithResearchTasks()
        {
            var gatherData = Tasks.CreateDataGatheringTask();
            var analyzeData = Tasks.CreateDataAnalysisTask();
            var writeReport = Tasks.CreateReportWritingTask();

            return new CrewBuilder()
                .Goal("Execute tasks in sequential order")
                .Sequential()
                .WithTask(gatherData)
                .WithTask(analyzeData)
                .WithTask(writeReport)
                .Build();
        }

        public static DomainCrew CreateCrewWithDependentTasks()
        {
            var task1 = Tasks.CreateDataGatheringTask();
            var task2 = Tasks.CreateDataAnalysisTask();
            var task3 = Tasks.CreateReportWritingTask();

            return new CrewBuilder()
                .Goal("Execute tasks in sequential order")
                .Sequential()
                .WithTask(task1)
                .WithTask(task2)
                .WithTask(task3)
                .Build();
        }

        public static DomainCrew CreateCrewWithParallelTasks()
        {
            var tasks = Enumerable.Range(1, 5)
                .Select(i => Tasks.CreateIndependentTask($"Task {i}"))
                .ToList();

            var builder = new CrewBuilder()
                .Goal("Execute tasks in parallel for maximum efficiency")
                .Parallel()
                .Verbose();

            foreach (var task in tasks)
            {
                builder.WithTask(task);
            }

            return builder.Build();
        }
    }

    /// <summary>
    /// Invalid crew configurations for negative testing.
    /// </summary>
    public static class Invalid
    {
        public static Action CreateWithEmptyGoal()
            => () => new CrewBuilder()
                .Goal("")
                .Sequential()
                .Build();

        public static Action CreateWithNullGoal()
            => () => new CrewBuilder()
                .Goal(null!)
                .Sequential()
                .Build();

        public static Action AddSameAgentTwice(DomainCrew crew, AgentId agentId)
            => () =>
            {
                crew.AddAgent(agentId);
                crew.AddAgent(agentId); // Should throw
            };

        public static Action AddSameTaskTwice(DomainCrew crew, TaskId taskId)
            => () =>
            {
                crew.AddTask(taskId);
                crew.AddTask(taskId); // Should throw
            };
    }

    /// <summary>
    /// Helper agents for crew tests.
    /// </summary>
    internal static class Agents
    {
        public static DomainAgent CreateLeadResearcher()
            => new AgentBuilder()
                .Role("Lead Researcher")
                .Goal("Guide research direction")
                .Backstory("PhD with 10 years experience")
                .AllowDelegation()
                .Build();

        public static DomainAgent CreateDataAnalyst()
            => new AgentBuilder()
                .Role(RoleDataAnalyst)
                .Goal("Analyze complex datasets")
                .Backstory("Expert in statistical analysis")
                .Build();

        public static DomainAgent CreateTechnicalWriter()
            => new AgentBuilder()
                .Role("Technical Writer")
                .Goal("Create clear documentation")
                .Backstory("Former developer turned writer")
                .Build();

        public static DomainAgent CreateSoftwareArchitect()
            => new AgentBuilder()
                .Role("Software Architect")
                .Goal("Design scalable systems")
                .Backstory("20 years of architecture experience")
                .AllowDelegation()
                .Build();

        public static DomainAgent CreateSeniorDeveloper()
            => new AgentBuilder()
                .Role(RoleSeniorDeveloper)
                .Goal("Implement robust solutions")
                .Backstory("Full-stack development expertise")
                .Build();

        public static DomainAgent CreateQAEngineer()
            => new AgentBuilder()
                .Role("QA Engineer")
                .Goal("Ensure quality standards")
                .Backstory("Automation and testing expert")
                .Build();

        public static DomainAgent CreateProjectManager()
            => new AgentBuilder()
                .Role("Project Manager")
                .Goal("Deliver project on time")
                .Backstory("PMP certified with agile experience")
                .AllowDelegation()
                .MaxIterations(3)
                .Build();

        public static DomainAgent CreateTechLead()
            => new AgentBuilder()
                .Role("Technical Lead")
                .Goal("Guide technical decisions")
                .Backstory("Senior architect with leadership skills")
                .AllowDelegation()
                .MaxIterations(5)
                .Build();
    }

    /// <summary>
    /// Helper tasks for crew tests.
    /// </summary>
    private static class Tasks
    {
        public static DomainTask CreateDataGatheringTask()
            => new CrewTaskBuilder()
                .Description("Gather market research data")
                .ExpectedOutput("Comprehensive dataset with sources")
                .Build();

        public static DomainTask CreateDataAnalysisTask()
            => new CrewTaskBuilder()
                .Description("Analyze gathered data for insights")
                .ExpectedOutput("Statistical analysis with visualizations")
                .Build();

        public static DomainTask CreateReportWritingTask()
            => new CrewTaskBuilder()
                .Description("Write executive summary report")
                .ExpectedOutput("Professional report with recommendations")
                .Build();

        public static DomainTask CreateIndependentTask(string name)
            => new CrewTaskBuilder()
                .Description($"Execute {name} independently")
                .ExpectedOutput($"Completed output for {name}")
                .Build();
    }
}
