using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.Agent;
using Orkeon.Infrastructure.Agent;

namespace Orkeon.Infrastructure.Tests.Services;

/// <summary>
/// Regression coverage for Experiment 07 friction #1: OpenAI-compatible providers
/// (DeepSeek, OpenAI, Groq, ...) enforce <c>^[a-zA-Z0-9_-]+$</c> on <c>function.name</c>.
/// Any delegation tool that lands on the wire with spaces in its name triggers HTTP 400.
/// </summary>
public partial class DelegationToolNamesValidityTests
{
    [GeneratedRegex("^[a-zA-Z0-9_-]+$")]
    private static partial Regex OpenAiFunctionNamePattern();

    [Fact]
    public void DelegationToolNames_ShouldMatchOpenAiFunctionNamePattern_WhenAllowDelegationIsTrue()
    {
        // Arrange — supervisor with delegation enabled
        var supervisor = new AgentBuilder()
            .Role("Supervisor")
            .Goal("Orchestrate the crew")
            .AllowDelegation()
            .Build();

        var provider = new AgentDelegationToolsProvider(
            new TestAgentCommunicationService(),
            new TestAgentExecutionService(),
            NullLogger<AgentDelegationToolsProvider>.Instance);

        // Act — materialize the tool list the LLM provider will receive
        provider.AddDelegationToolsToAgent(supervisor);

        // Assert — every tool name must satisfy the OpenAI-compatible regex
        Assert.NotEmpty(supervisor.Tools);
        foreach (var tool in supervisor.Tools)
        {
            Assert.True(
                OpenAiFunctionNamePattern().IsMatch(tool.Name),
                $"Tool name '{tool.Name}' does not match ^[a-zA-Z0-9_-]+$ — would be rejected by DeepSeek/OpenAI as function.name.");
        }
    }

    [Fact]
    public void DelegationToolNames_ShouldExposeCoworkerSuffixedSnakeCase()
    {
        // Arrange
        var supervisor = new AgentBuilder()
            .Role("Supervisor")
            .Goal("Orchestrate")
            .AllowDelegation()
            .Build();

        var provider = new AgentDelegationToolsProvider(
            new TestAgentCommunicationService(),
            new TestAgentExecutionService(),
            NullLogger<AgentDelegationToolsProvider>.Instance);

        // Act
        provider.AddDelegationToolsToAgent(supervisor);

        // Assert — the names match the values documented in the experiment 07 fix prompt
        var names = supervisor.Tools.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("delegate_work_to_coworker", names);
        Assert.Contains("ask_question_to_coworker", names);
    }
}
