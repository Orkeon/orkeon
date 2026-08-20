using System.Text.Json;
using Orkeon.Domain.Common;
using Orkeon.Domain.Tools;
using Protocol = Orkeon.Domain.Tools.Protocol;
using Orkeon.Scripting.Cli.Commands.Run;
using Orkeon.Scripting.Cli.Events;

namespace Orkeon.Scripting.Cli.Tests.Run;

/// <summary>A tool that answers however the test asks it to.</summary>
internal sealed class ScriptedTool : IBaseTool
{
    public ScriptedTool(string name) => Name = name;

    public string Name { get; }
    public string Description => "scripted";
    public Protocol.ToolSchema Schema => new(Name, "scripted", []);
    public bool Succeeds { get; set; } = true;
    public Exception? Throws { get; set; }

    public Task<Protocol.ToolCallResponse> CallAsync(Protocol.ToolCallRequest request, CancellationToken cancellationToken = default)
    {
        if (Throws is not null)
            throw Throws;

        return Task.FromResult(new Protocol.ToolCallResponse(Succeeds, "done", Succeeds ? null : "nope"));
    }

    public Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default)
    {
        if (Throws is not null)
            throw Throws;

        return Task.FromResult(Succeeds ? ToolResult.CreateSuccess("done") : ToolResult.CreateError("nope"));
    }

    public bool ValidateInput(string input) => true;
}

/// <summary>
/// BUS-03: a tool's calls become events. Instrumenting at the tool rather than in each agent
/// loop is what makes the coverage complete, and the decorator has one hard constraint the
/// tests exist to hold.
/// </summary>
public class ObservedToolTests
{
    private static (ObservedTool Tool, StringWriter Output) Observe(string name = "file_read")
    {
        var output = new StringWriter();
        return (new ObservedTool(new ScriptedTool(name), new OrkeonEventWriter(output)), output);
    }

    private static IReadOnlyList<JsonElement> Emitted(StringWriter output) =>
        [.. output.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => JsonDocument.Parse(line).RootElement.Clone())];

    [Fact]
    public void The_decorator_is_an_ITool_or_the_agents_would_get_no_tools_at_all()
    {
        // CrewFactory assigns with `tool is ITool` and AgentMapper with OfType<ITool>(). A
        // decorator implementing only IBaseTool would be filtered out silently, and a crew
        // would run with an empty toolbox while every test still passed.
        var (tool, _) = Observe();

        Assert.IsType<ITool>(tool, exactMatch: false);
        Assert.IsType<IBaseTool>(tool, exactMatch: false);
    }

    [Fact]
    public void The_decorator_forwards_the_tool_s_own_identity()
    {
        var (tool, _) = Observe("web_scrape");

        Assert.Equal("web_scrape", tool.Name);
        Assert.Equal("scripted", tool.Description);
        Assert.True(tool.ValidateInput("anything"));
    }

    [Fact]
    public async Task A_call_becomes_a_correlated_pair()
    {
        var (tool, output) = Observe();

        await tool.CallAsync(
            new Protocol.ToolCallRequest("file_read", new Dictionary<string, object?> { ["path"] = "/a.txt" }),
            TestContext.Current.CancellationToken);

        var events = Emitted(output);
        Assert.Equal(2, events.Count);

        Assert.Equal("tool.called", events[0].GetProperty("kind").GetString());
        Assert.Equal("file_read", events[0].GetProperty("toolName").GetString());
        Assert.Equal("path", events[0].GetProperty("argsSummary").GetString());

        Assert.Equal("tool.returned", events[1].GetProperty("kind").GetString());
        Assert.True(events[1].GetProperty("success").GetBoolean());

        // The pair is correlated, so a client can nest a call inside whatever it was part of.
        Assert.Equal(
            events[0].GetProperty("correlationId").GetString(),
            events[1].GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task A_tool_that_throws_still_reports_that_it_returned()
    {
        // Otherwise a watcher shows a step running forever, which is the worst of both: the
        // run is over and the screen says it is not.
        var inner = new ScriptedTool("file_read") { Throws = new InvalidOperationException("boom") };
        var output = new StringWriter();
        var tool = new ObservedTool(inner, new OrkeonEventWriter(output));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => tool.ExecuteAsync("x", TestContext.Current.CancellationToken));

        var events = Emitted(output);
        Assert.Equal(2, events.Count);
        Assert.Equal("tool.returned", events[1].GetProperty("kind").GetString());
        Assert.False(events[1].GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task A_failed_call_is_reported_as_failed_without_throwing()
    {
        var inner = new ScriptedTool("file_read") { Succeeds = false };
        var output = new StringWriter();
        var tool = new ObservedTool(inner, new OrkeonEventWriter(output));

        await tool.ExecuteAsync("x", TestContext.Current.CancellationToken);

        Assert.False(Emitted(output)[1].GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task Delegation_and_spawn_are_named_rather_than_buried_among_tool_calls()
    {
        // They are tool calls underneath, but what they mean is not "a tool ran": one is an
        // agent handing work over, the other a team growing. Those are the two moments that
        // make an autonomous run hard to follow.
        var (delegating, delegateOutput) = Observe(ObservedTool.DelegateToolName);
        await delegating.CallAsync(
            new Protocol.ToolCallRequest(ObservedTool.DelegateToolName, new Dictionary<string, object?>
            {
                ["coworker"] = "analyst",
                ["task"] = "check the figures",
            }),
            TestContext.Current.CancellationToken);

        var delegation = Emitted(delegateOutput)[0];
        Assert.Equal("delegation.started", delegation.GetProperty("kind").GetString());
        Assert.Equal("analyst", delegation.GetProperty("toAgentId").GetString());
        Assert.Equal("check the figures", delegation.GetProperty("taskId").GetString());

        var (spawning, spawnOutput) = Observe(ObservedTool.SpawnToolName);
        await spawning.CallAsync(
            new Protocol.ToolCallRequest(ObservedTool.SpawnToolName, new Dictionary<string, object?>
            {
                ["role"] = "auditor",
                ["goal"] = "double-check the totals",
            }),
            TestContext.Current.CancellationToken);

        var spawn = Emitted(spawnOutput)[0];
        Assert.Equal("agent.spawned", spawn.GetProperty("kind").GetString());
        Assert.Equal("auditor", spawn.GetProperty("role").GetString());
        Assert.Equal("double-check the totals", spawn.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task The_arguments_are_summarized_not_shipped()
    {
        // A tool call can carry a whole file. The stream is read by a UI, not an archive.
        var (tool, output) = Observe();
        var arguments = new Dictionary<string, object?>();
        for (var index = 0; index < 9; index++)
            arguments[$"key{index}"] = new string('x', 5000);

        await tool.CallAsync(new Protocol.ToolCallRequest("file_read", arguments), TestContext.Current.CancellationToken);

        var summary = Emitted(output)[0].GetProperty("argsSummary").GetString()!;
        Assert.EndsWith("…", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("xxxxx", summary, StringComparison.Ordinal);
    }
}
