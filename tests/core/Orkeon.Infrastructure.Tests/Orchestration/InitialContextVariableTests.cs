using Orkeon.Application.Interfaces.Services;
using Orkeon.Infrastructure.Orchestration;

namespace Orkeon.Infrastructure.Tests.Orchestration;

/// <summary>
/// The initial context of a kickoff reaches the agents as the <c>initial_context</c> prompt
/// variable -- it used to be mapped into the domain input and read by nothing.
/// </summary>
public sealed class InitialContextVariableTests
{
    [Fact]
    public void The_initial_context_joins_the_prompt_variables()
    {
        var variables = SequentialCrewOrchestrator.PromptVariables(CrewInput.Empty("Triage today's issues"));

        Assert.Equal("Triage today's issues", variables[SequentialCrewOrchestrator.InitialContextVariable]);
    }

    [Fact]
    public void A_blank_initial_context_adds_nothing_and_a_caller_supplied_variable_wins()
    {
        var blank = SequentialCrewOrchestrator.PromptVariables(CrewInput.Empty("  "));
        var supplied = SequentialCrewOrchestrator.PromptVariables(CrewInput.WithStringVariables(
            "from the context", new Dictionary<string, string> { ["initial_context"] = "from the caller", ["topic"] = "x" }));

        Assert.False(blank.ContainsKey("initial_context"));
        Assert.Equal("from the caller", supplied["initial_context"]);
        Assert.Equal("x", supplied["topic"]);
    }
}
