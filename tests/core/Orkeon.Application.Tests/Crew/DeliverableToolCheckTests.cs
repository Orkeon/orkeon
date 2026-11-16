using Orkeon.Application.Crew;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Application.Tests.Doubles;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Application.Tests.Crew;

/// <summary>
/// #392: a task whose <c>tool_call</c> deliverable its agent cannot write (no <c>file_write</c>
/// among the agent's tools and the task's own) is reported at load and by <c>--validate</c>.
/// </summary>
public sealed class DeliverableToolCheckTests
{
    private const string NotesPath = "/output/notes.md";

    private sealed class StubProvider(bool runsOwnTools) : ILlmProvider
    {
        public string Name => "stub";

        public LlmProviderCapabilities Capabilities { get; } =
            LlmProviderCapabilities.Unknown with { RunsOwnTools = runsOwnTools };

        public System.Threading.Tasks.Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default) =>
            System.Threading.Tasks.Task.FromResult(new LlmResponse { Content = "stub" });

        public System.Threading.Tasks.Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default) =>
            System.Threading.Tasks.Task.FromResult(new LlmResponse { Content = "stub" });
    }

    private static IBaseTool Tool(string name) =>
        new StubResponseTool(name, new ToolCallResponse(true, "ok", null));

    private static DomainAgent Agent(params string[] tools)
    {
        var builder = new AgentBuilder()
            .Role(AgentRole.From("Reader"))
            .Goal(AgentGoal.From("Read the sources"));
        if (tools.Length > 0)
            builder.WithTools(tools.Select(Tool));
        return builder.Build();
    }

    private static CrewTask Task(
        DomainAgent? agent, TaskDeliverable? deliverable, string[]? taskTools = null, string? key = "take_notes")
    {
        var builder = new CrewTaskBuilder()
            .Description(TaskDescription.From("Read the sources and take notes."))
            .ExpectedOutput("The notes.")
            .WithKey(key)
            .WithTools((taskTools ?? []).Select(Tool));
        if (agent is not null)
            builder.AssignTo(agent);
        var task = builder.Build();
        task.SetDeliverable(deliverable);
        return task;
    }

    private static TaskDeliverable Deliverable(DeliverableSource source) => new()
    {
        Path = NotesPath,
        Source = source,
        SchemaInline = source == DeliverableSource.StructuredOutput ? "{\"type\":\"object\"}" : null,
    };

    [Fact]
    public void ShouldReportTheTask_WhenItsToolCallDeliverableAgentHasNoFileWrite()
    {
        var agent = Agent("file_read", "grep");
        var task = Task(agent, Deliverable(DeliverableSource.ToolCall));

        var finding = Assert.Single(DeliverableToolCheck.Check([task], [agent]));

        Assert.Equal("take_notes", finding.Task);
        Assert.Equal("Reader", finding.Agent);
        Assert.Equal(NotesPath, finding.Path);
        Assert.Equal(
            "Task 'take_notes' declares the deliverable '/output/notes.md' (source tool_call), but its agent 'Reader' "
            + "has no file_write tool: the file will not be written. Give the agent file_write, or set "
            + "deliverable.source: final_message.",
            finding.Message);
    }

    [Fact]
    public void ShouldNameTheTaskByItsDescription_WhenItHasNoKey()
    {
        var agent = Agent();
        var task = Task(agent, Deliverable(DeliverableSource.ToolCall), key: null);

        var finding = Assert.Single(DeliverableToolCheck.Check([task], [agent]));

        Assert.Equal("Read the sources and take notes.", finding.Task);
    }

    [Fact]
    public void ShouldReportNothing_WhenTheAgentHasFileWrite()
    {
        var agent = Agent("file_read", "file_write");
        var task = Task(agent, Deliverable(DeliverableSource.ToolCall));

        Assert.Empty(DeliverableToolCheck.Check([task], [agent]));
    }

    [Fact]
    public void ShouldReportNothing_WhenTheTaskItselfGrantsFileWrite()
    {
        var agent = Agent("file_read");
        var task = Task(agent, Deliverable(DeliverableSource.ToolCall), taskTools: ["file_write"]);

        Assert.Empty(DeliverableToolCheck.Check([task], [agent]));
    }

    [Theory]
    [InlineData(DeliverableSource.FinalMessage)]
    [InlineData(DeliverableSource.StructuredOutput)]
    [InlineData(DeliverableSource.None)]
    public void ShouldReportNothing_WhenTheFrameworkWritesTheDeliverable(DeliverableSource source)
    {
        var agent = Agent("file_read");
        var task = Task(agent, Deliverable(source));

        Assert.Empty(DeliverableToolCheck.Check([task], [agent]));
    }

    [Fact]
    public void ShouldReportNothing_WhenTheTaskHasNoDeliverable()
    {
        var agent = Agent("file_read");
        var task = Task(agent, deliverable: null);

        Assert.Empty(DeliverableToolCheck.Check([task], [agent]));
    }

    [Fact]
    public void ShouldReportNothing_WhenNoAgentIsAssigned()
    {
        var agent = Agent("file_read");
        var task = Task(agent: null, Deliverable(DeliverableSource.ToolCall));

        Assert.Empty(DeliverableToolCheck.Check([task], [agent]));
    }

    [Fact]
    public void ShouldReportNothing_WhenTheAgentProviderRunsItsOwnTools()
    {
        var agent = new AgentBuilder()
            .Role(AgentRole.From("Bridge"))
            .Goal(AgentGoal.From("Work on its own provider"))
            .WithLlm(new StubProvider(runsOwnTools: true))
            .Build();
        var task = Task(agent, Deliverable(DeliverableSource.ToolCall));

        Assert.Empty(DeliverableToolCheck.Check([task], [agent]));
    }

    [Fact]
    public void ShouldReport_WhenTheAgentProviderRunsOrkeonTools()
    {
        var agent = new AgentBuilder()
            .Role(AgentRole.From("Reader"))
            .Goal(AgentGoal.From("Work on its own provider"))
            .WithLlm(new StubProvider(runsOwnTools: false))
            .Build();
        var task = Task(agent, Deliverable(DeliverableSource.ToolCall));

        Assert.Single(DeliverableToolCheck.Check([task], [agent]));
    }
}
