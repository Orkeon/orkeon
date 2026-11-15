using Orkeon.Application.Context;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Common;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Infrastructure.Tools;

namespace Orkeon.Infrastructure.Tests.Tools;

public sealed class DelegateWorkToolCapFlagTests
{
    private static async System.Threading.Tasks.Task<Dictionary<string, object?>> DelegateAsync(bool capped)
    {
        using var scope = new MockMemoryScope();
        var parent = new SimpleExecutionContext(CrewId.Create(), new Dictionary<string, string>(), scope, []);
        var coworker = new Orkeon.Domain.Agent.AgentBuilder().Role("Writer").Goal("Write").Build();
        var executions = new MockAgentExecutionService();
        executions.SetExecuteResult(
            new TaskResult(true, "partial", null, [], TimeSpan.Zero) { EndedOnIterationCap = capped });
        using var tool = new DelegateWorkTool(
            new MockAgentCommunicationService(), AgentId.Create(), _ => coworker.Id, executions, _ => coworker, () => parent);
        var request = new ToolCallRequest(
            ToolName: "delegate_work_to_coworker",
            Parameters: new Dictionary<string, object?>
            {
                ["task"] = "Write the intro",
                ["context"] = "",
                ["coworker_role"] = "Writer",
                ["wait_for_result"] = true,
            });

        var response = await tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(response.Success, response.Error);
        return Assert.IsType<Dictionary<string, object?>>(response.Result);
    }

    [Fact]
    [Trait("BR", "CH-39")]
    public async System.Threading.Tasks.Task ShouldReportTheCap_WhenADelegatedCoworkerEndsOnItsIterationCap()
    {
        var result = await DelegateAsync(capped: true);

        Assert.Equal(true, result["ended_on_iteration_cap"]);
        Assert.Contains("iteration cap", result["message"]?.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-39")]
    public async System.Threading.Tasks.Task ShouldNotReportTheCap_WhenADelegatedCoworkerIsNotCapped()
    {
        var result = await DelegateAsync(capped: false);

        Assert.Equal(false, result["ended_on_iteration_cap"]);
        Assert.DoesNotContain("iteration cap", result["message"]?.ToString(), StringComparison.Ordinal);
    }
}
