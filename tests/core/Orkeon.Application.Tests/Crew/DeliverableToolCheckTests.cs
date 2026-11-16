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

    private static TaskDeliverable Deliverable(DeliverableSource source, string path = NotesPath) => new()
    {
        Path = path,
        Source = source,
        SchemaInline = source == DeliverableSource.StructuredOutput ? "{\"type\":\"object\"}" : null,
    };

    [Fact]
    public void ShouldReportTheTask_WhenItsToolCallDeliverableAgentHasNoFileWrite()
    {
        var agent = Agent("file_read", "grep");
        var task = Task(agent, Deliverable(DeliverableSource.ToolCall));

        var finding = Assert.Single(DeliverableToolCheck.Check([task], [agent], ProcessType.Sequential, managerAgentId: null));

        Assert.Equal("take_notes", finding.Task);
        Assert.Equal(["Reader"], finding.Agents);
        Assert.Equal(NotesPath, finding.Path);
        Assert.Equal(
            "Task 'take_notes' declares the deliverable '/output/notes.md' (source tool_call), but its agent 'Reader' "
            + "has no tool that writes it (file_write): the file may not be written. Give the agent file_write, or set "
            + "deliverable.source: final_message.",
            finding.Message);
    }

    [Fact]
    public void ShouldNameTheTaskByItsDescription_WhenItHasNoKey()
    {
        var agent = Agent();
        var task = Task(agent, Deliverable(DeliverableSource.ToolCall), key: null);

        var finding = Assert.Single(DeliverableToolCheck.Check([task], [agent], ProcessType.Sequential, managerAgentId: null));

        Assert.Equal("Read the sources and take notes.", finding.Task);
    }

    [Fact]
    public void ShouldReportNothing_WhenTheAgentHasFileWrite()
    {
        var agent = Agent("file_read", "file_write");
        var task = Task(agent, Deliverable(DeliverableSource.ToolCall));

        Assert.Empty(DeliverableToolCheck.Check([task], [agent], ProcessType.Sequential, managerAgentId: null));
    }

    [Fact]
    public void ShouldReportNothing_WhenTheTaskItselfGrantsFileWrite()
    {
        var agent = Agent("file_read");
        var task = Task(agent, Deliverable(DeliverableSource.ToolCall), taskTools: ["file_write"]);

        Assert.Empty(DeliverableToolCheck.Check([task], [agent], ProcessType.Sequential, managerAgentId: null));
    }

    [Theory]
    [InlineData(DeliverableSource.FinalMessage)]
    [InlineData(DeliverableSource.StructuredOutput)]
    [InlineData(DeliverableSource.None)]
    public void ShouldReportNothing_WhenTheFrameworkWritesTheDeliverable(DeliverableSource source)
    {
        var agent = Agent("file_read");
        var task = Task(agent, Deliverable(source));

        Assert.Empty(DeliverableToolCheck.Check([task], [agent], ProcessType.Sequential, managerAgentId: null));
    }

    [Fact]
    public void ShouldReportNothing_WhenTheTaskHasNoDeliverable()
    {
        var agent = Agent("file_read");
        var task = Task(agent, deliverable: null);

        Assert.Empty(DeliverableToolCheck.Check([task], [agent], ProcessType.Sequential, managerAgentId: null));
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

        Assert.Empty(DeliverableToolCheck.Check([task], [agent], ProcessType.Sequential, managerAgentId: null));
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

        Assert.Single(DeliverableToolCheck.Check([task], [agent], ProcessType.Sequential, managerAgentId: null));
    }

    // D20 (VF-1, VF-4): a built-in tool other than file_write that writes the deliverable's format.
    [Theory]
    [InlineData("docx_writer", "/output/report.docx")]
    [InlineData("xlsx_writer", "/output/figures.xlsx")]
    [InlineData("image_generation", "/output/cover.png")]
    [InlineData("firefox", "/output/page.png")]
    public void ShouldReportNothing_WhenABuiltInToolWritesTheDeliverableFormat(string tool, string path)
    {
        var agent = Agent("file_read", tool);
        var task = Task(agent, Deliverable(DeliverableSource.ToolCall, path));

        Assert.Empty(DeliverableToolCheck.Check([task], [agent], ProcessType.Sequential, managerAgentId: null));
    }

    [Fact]
    public void ShouldReportAndNameTheFormatWriter_WhenTheAgentWritesAnotherFormatOnly()
    {
        var agent = Agent("file_read", "xlsx_writer");
        var task = Task(agent, Deliverable(DeliverableSource.ToolCall, "/output/report.docx"));

        var finding = Assert.Single(DeliverableToolCheck.Check([task], [agent], ProcessType.Sequential, managerAgentId: null));

        Assert.Contains("has no tool that writes it (file_write, docx_writer)", finding.Message, StringComparison.Ordinal);
        Assert.Contains("Give the agent docx_writer", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldReportNothing_WhenTheAgentHoldsAToolOrkeonDoesNotShip()
    {
        var foreign = StubForeignTool.Create("render_report");
        Assert.False(foreign.GetType().Assembly.GetName().Name!.StartsWith("Orkeon.", StringComparison.Ordinal));
        var agent = new AgentBuilder()
            .Role(AgentRole.From("Reader"))
            .Goal(AgentGoal.From("Read the sources"))
            .WithTools(Tool("file_read"), foreign)
            .Build();
        var task = Task(agent, Deliverable(DeliverableSource.ToolCall));

        Assert.Empty(DeliverableToolCheck.Check([task], [agent], ProcessType.Sequential, managerAgentId: null));
    }

    [Fact]
    public void ShouldReportNothing_WhenTheTaskGrantsAToolOrkeonDoesNotShip()
    {
        var agent = Agent("file_read");
        var task = new CrewTaskBuilder()
            .Description(TaskDescription.From("Read the sources and take notes."))
            .ExpectedOutput("The notes.")
            .WithKey("take_notes")
            .WithTools([StubForeignTool.Create("render_report")])
            .AssignTo(agent)
            .Build();
        task.SetDeliverable(Deliverable(DeliverableSource.ToolCall));

        Assert.Empty(DeliverableToolCheck.Check([task], [agent], ProcessType.Sequential, managerAgentId: null));
    }

    // D21 (VF-5): a task with no agent runs on the crew's agents in turn (round-robin), not on a manager's pick.
    [Theory]
    [InlineData("Sequential")]
    [InlineData("Parallel")]
    [InlineData("Graph")]
    public void ShouldReport_WhenATaskWithNoAgentRunsOnACrewOfOneAgentThatCannotWrite(string process)
    {
        var agent = Agent("file_read");
        var task = Task(agent: null, Deliverable(DeliverableSource.ToolCall));

        var finding = Assert.Single(DeliverableToolCheck.Check([task], [agent], ProcessType.From(process), managerAgentId: null));

        Assert.Equal(["Reader"], finding.Agents);
    }

    [Fact]
    public void ShouldReportNothing_WhenATaskWithNoAgentMayRunOnAnAgentThatWrites()
    {
        var reader = Agent("file_read");
        var writer = Named("Writer", "file_write");
        var task = Task(agent: null, Deliverable(DeliverableSource.ToolCall));

        Assert.Empty(DeliverableToolCheck.Check([task], [reader, writer], ProcessType.Sequential, managerAgentId: null));
    }

    [Fact]
    public void ShouldCheckOnlyTheDeclaredAgent_WhenTheProcessBindsTheTaskToIt()
    {
        var reader = Agent("file_read");
        var writer = Named("Writer", "file_write");
        var task = Task(reader, Deliverable(DeliverableSource.ToolCall));

        var finding = Assert.Single(DeliverableToolCheck.Check([task], [reader, writer], ProcessType.Sequential, managerAgentId: null));

        Assert.Equal(["Reader"], finding.Agents);
    }

    // D21 (VF-6): in the coordinated modes the manager or the vote picks the agent, whatever the task declares.
    [Theory]
    [InlineData("Hierarchical")]
    [InlineData("Consensual")]
    [InlineData("Autonomous")]
    public void ShouldReportNothing_WhenTheDeclaredAgentCannotWriteButAnotherWorkerCan(string process)
    {
        var reader = Agent("file_read");
        var writer = Named("Writer", "file_write");
        var task = Task(reader, Deliverable(DeliverableSource.ToolCall));

        Assert.Empty(DeliverableToolCheck.Check([task], [reader, writer], ProcessType.From(process), managerAgentId: null));
    }

    [Theory]
    [InlineData("Hierarchical")]
    [InlineData("Consensual")]
    [InlineData("Autonomous")]
    public void ShouldReportEveryWorker_WhenNoWorkerOfACoordinatedCrewCanWrite(string process)
    {
        var reader = Agent("file_read");
        var analyst = Named("Analyst", "grep");
        var task = Task(reader, Deliverable(DeliverableSource.ToolCall));

        var finding = Assert.Single(DeliverableToolCheck.Check([task], [reader, analyst], ProcessType.From(process), managerAgentId: null));

        Assert.Equal(["Reader", "Analyst"], finding.Agents);
        Assert.Equal(
            "Task 'take_notes' declares the deliverable '/output/notes.md' (source tool_call), but none of the agents "
            + "that may run it ('Reader', 'Analyst') has a tool that writes it (file_write): the file may not be "
            + "written. Give file_write to the agent meant to write it, or set deliverable.source: final_message.",
            finding.Message);
    }

    [Theory]
    [InlineData("Hierarchical")]
    [InlineData("Consensual")]
    public void ShouldLeaveTheManagerOut_WhenItIsTheOnlyAgentThatCanWrite(string process)
    {
        var reader = Agent("file_read");
        var manager = Named("Manager", "file_write");
        var task = Task(reader, Deliverable(DeliverableSource.ToolCall));

        var finding = Assert.Single(DeliverableToolCheck.Check([task], [reader, manager], ProcessType.From(process), manager.Id));

        Assert.Equal(["Reader"], finding.Agents);
    }

    private static DomainAgent Named(string role, params string[] tools) => new AgentBuilder()
        .Role(AgentRole.From(role))
        .Goal(AgentGoal.From("Work"))
        .WithTools(tools.Select(Tool))
        .Build();
}
