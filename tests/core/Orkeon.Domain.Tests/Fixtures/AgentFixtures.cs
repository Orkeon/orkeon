using Orkeon.Domain.Agent;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;

namespace Orkeon.Domain.Tests.Fixtures;

/// <summary>
/// Test data builders for Agent domain tests.
/// Follows Object Mother pattern for consistent test data.
/// </summary>
public static class AgentFixtures
{
    /// <summary>
    /// Creates a basic researcher agent with minimal configuration.
    /// </summary>
    public static DomainAgent CreateResearcherAgent()
    {
        return new AgentBuilder()
            .Role("Senior Research Analyst")
            .Goal("Uncover cutting-edge developments in AI and data science")
            .Backstory("You're a seasoned researcher with a knack for uncovering hidden patterns")
            .Build();
    }

    /// <summary>
    /// Creates a manager agent capable of delegation.
    /// </summary>
    public static DomainAgent CreateManagerAgent()
    {
        return new AgentBuilder()
            .Role("Project Manager")
            .Goal("Coordinate team efforts and ensure project success")
            .Backstory("Experienced leader with strong delegation and coordination skills")
            .AllowDelegation()
            .MaxIterations(5)
            .Build();
    }

    /// <summary>
    /// Creates a developer agent with specific tools.
    /// </summary>
    public static DomainAgent CreateDeveloperAgent()
    {
        return new AgentBuilder()
            .Role("Senior Software Engineer")
            .Goal("Design and implement robust software solutions")
            .Backstory("Veteran developer with expertise in distributed systems")
            .MaxIterations(10)
            .Build();
    }

    /// <summary>
    /// Creates an agent with custom memory configuration.
    /// </summary>
    public static DomainAgent CreateAgentWithMemory(int memoryCapacity = 100)
    {
        var agent = new AgentBuilder()
            .Role(RoleDataAnalyst)
            .Goal("Analyze complex datasets and extract insights")
            .Backstory("Expert in statistical analysis and machine learning")
            .Build();

        // Memory would be configured through the aggregate methods
        return agent;
    }

    /// <summary>
    /// Creates agents for team scenarios.
    /// </summary>
    public static class Team
    {
        public static DomainAgent CreateLeadResearcher()
        {
            return new AgentBuilder()
                .Role("Lead Research Scientist")
                .Goal("Guide research direction and validate findings")
                .Backstory("PhD in AI with 15 years of research experience")
                .AllowDelegation()
                .MaxIterations(3)
                .Build();
        }

        public static DomainAgent CreateJuniorResearcher()
        {
            return new AgentBuilder()
                .Role("Junior Research Assistant")
                .Goal("Support senior researchers with data collection")
                .Backstory("Recent graduate eager to learn and contribute")
                .MaxIterations(5)
                .Build();
        }

        public static DomainAgent CreateTechnicalWriter()
        {
            return new AgentBuilder()
                .Role("Technical Documentation Specialist")
                .Goal("Create clear and comprehensive documentation")
                .Backstory("Former developer turned technical writer")
                .Build();
        }
    }

    /// <summary>
    /// Invalid agent configurations for negative testing.
    /// </summary>
    public static class Invalid
    {
        public static Action CreateWithEmptyRole()
            => () => new AgentBuilder()
                .Role("")
                .Goal("Valid goal")
                .Backstory("Valid backstory")
                .Build();

        public static Action CreateWithEmptyGoal()
            => () => new AgentBuilder()
                .Role("Valid Role")
                .Goal("")
                .Backstory("Valid backstory")
                .Build();

        public static Action CreateWithEmptyBackstory()
            => () => new AgentBuilder()
                .Role("Valid Role")
                .Goal("Valid goal")
                .Backstory("")
                .Build();

        public static Action CreateWithNegativeMaxIterations()
            => () => new AgentBuilder()
                .Role("Valid Role")
                .Goal("Valid goal")
                .Backstory("Valid backstory")
                .MaxIterations(-1)
                .Build();
    }

    /// <summary>
    /// Pre-configured agent states for testing state transitions.
    /// </summary>
    public static class States
    {
        public static DomainAgent CreateIdleAgent()
        {
            return CreateResearcherAgent();
        }

        public static DomainAgent CreateBusyAgent()
        {
            var agent = CreateResearcherAgent();
            // Would assign a task through domain methods
            return agent;
        }

        public static DomainAgent CreateAgentWithCompletedTasks()
        {
            var agent = CreateResearcherAgent();
            // Would complete tasks through domain methods
            return agent;
        }
    }
}
