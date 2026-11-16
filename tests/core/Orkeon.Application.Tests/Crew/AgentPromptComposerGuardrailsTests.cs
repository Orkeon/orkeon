using Orkeon.Application.Context;
using Orkeon.Application.Crew.Execution;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Common;
using Orkeon.Domain.Task;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Application.Tests.Crew;

/// <summary>
/// Verifies that task-level guardrails (P2-O-04) reach the system prompt via
/// <see cref="AgentPromptComposer.BuildSystemPrompt"/>, alongside — and after — the agent's own
/// guardrails, without changing agent-level rendering.
/// </summary>
public class AgentPromptComposerGuardrailsTests
{
    [Fact]
    public void BuildSystemPrompt_ShouldInjectTaskGuardrails_IntoSystemPrompt()
    {
        var agent = new AgentBuilder().Role("Analyst").Goal("Analyze").Build();
        var task = new CrewTaskBuilder()
            .Description("Do work")
            .ExpectedOutput("Result")
            .WithGuardrails(new GuardrailsConfig
            {
                Header = "TASK RULES:",
                Rules = ["cite every source"]
            })
            .Build();

        var prompt = Prompt(agent, task);

        Assert.Contains("TASK RULES:", prompt, StringComparison.Ordinal);
        Assert.Contains("1. cite every source", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildSystemPrompt_ShouldLeaveAgentGuardrailsUnchanged_WhenTaskHasNone()
    {
        var agent = new AgentBuilder()
            .Role("Analyst").Goal("Analyze")
            .WithGuardrails(new GuardrailsConfig { Header = "AGENT RULES:", Rules = ["stay factual"] })
            .Build();
        var task = new CrewTaskBuilder().Description("Do work").ExpectedOutput("Result").Build();

        var prompt = Prompt(agent, task);

        // Agent guardrails still render; no phantom task section appears.
        Assert.Contains("AGENT RULES:", prompt, StringComparison.Ordinal);
        Assert.Contains("1. stay factual", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("TASK RULES:", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildSystemPrompt_ShouldApplyBothLevels_AgentRulesBeforeTaskRules()
    {
        var agent = new AgentBuilder()
            .Role("Analyst").Goal("Analyze")
            .WithGuardrails(new GuardrailsConfig { Header = "AGENT RULES:", Rules = ["agent-rule-alpha"] })
            .Build();
        var task = new CrewTaskBuilder()
            .Description("Do work").ExpectedOutput("Result")
            .WithGuardrails(new GuardrailsConfig { Header = "TASK RULES:", Rules = ["task-rule-beta"] })
            .Build();

        var prompt = Prompt(agent, task);

        // Both apply; the agent section precedes the task section.
        Assert.Contains("agent-rule-alpha", prompt, StringComparison.Ordinal);
        Assert.Contains("task-rule-beta", prompt, StringComparison.Ordinal);
        Assert.True(
            prompt.IndexOf("AGENT RULES:", StringComparison.Ordinal)
                < prompt.IndexOf("TASK RULES:", StringComparison.Ordinal),
            "Agent guardrails must render before task guardrails.");
    }

    [Fact]
    public void BuildSystemPrompt_ShouldGateTaskToolRules_ByExecutingAgentTools()
    {
        var agent = new AgentBuilder()
            .Role("Analyst").Goal("Analyze")
            .WithTool(new StubTool("file_write"))
            .Build();
        var task = new CrewTaskBuilder()
            .Description("Do work").ExpectedOutput("Result")
            .WithGuardrails(new GuardrailsConfig
            {
                Header = "TASK RULES:",
                ToolRules = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["file_write"] = ["never overwrite"],   // agent HAS this tool → rendered
                    ["web_search"] = ["cite domains"]        // agent LACKS this tool → skipped
                }
            })
            .Build();

        var prompt = Prompt(agent, task);

        Assert.Contains("[file_write] never overwrite", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("web_search", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildSystemPrompt_ShouldListTaskTools_AndApplyTheirToolRules()
    {
        // GAP-07: a task's own tools join the agent's for this task — the prompt lists them and
        // the tool rules keyed on them apply.
        var agent = new AgentBuilder().Role("Analyst").Goal("Analyze").WithTool(new StubTool("file_read")).Build();
        var task = new CrewTaskBuilder()
            .Description("Do work").ExpectedOutput("Result")
            .WithTool(new StubTool("file_write"))
            .WithGuardrails(new GuardrailsConfig
            {
                Header = "TASK RULES:",
                ToolRules = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["file_write"] = ["never overwrite"],
                }
            })
            .Build();

        var prompt = Prompt(agent, task);

        Assert.Contains("- **file_read**", prompt, StringComparison.Ordinal);
        Assert.Contains("- **file_write**", prompt, StringComparison.Ordinal);
        Assert.Contains("[file_write] never overwrite", prompt, StringComparison.Ordinal);
    }

    private static string Prompt(DomainAgent agent, CrewTask task)
        => AgentPromptComposer.BuildSystemPrompt(agent, task, TaskToolbelt.Compose(agent, task), supportsNativeToolCalling: true,
            new Orkeon.Tests.Shared.FileSystem.FakeFileSystemService());

    /// <summary>Minimal IBaseTool stub — only Name matters for tool-rule gating.</summary>
    private sealed class StubTool : IBaseTool
    {
        public string Name { get; }
        public string Description => "stub";
        public ToolSchema Schema { get; }

        public StubTool(string name)
        {
            Name = name;
            Schema = new ToolSchema(name, "stub", new Dictionary<string, ParameterSchema>());
        }

        public System.Threading.Tasks.Task<ToolCallResponse> CallAsync(
            Orkeon.Domain.Tools.Protocol.ToolCallRequest request, CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.FromResult(new ToolCallResponse(true, "stub result", null));

        public System.Threading.Tasks.Task<ToolResult> ExecuteAsync(
            string input, CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.FromResult(ToolResult.CreateSuccess("stub result"));

        public bool ValidateInput(string input) => true;
    }
}

/// <summary>
/// A ToolCall deliverable is written by the agent itself, so the prompt must name the
/// path — without it the agent guesses (the forge trial burned three denied file_write
/// calls before finding /output). Framework-persisted sources stay out of the prompt.
/// </summary>
public class AgentPromptComposerDeliverableTests
{
    private static SimpleExecutionContext Context() => new(
        CrewId.From(Guid.NewGuid()),
        new Dictionary<string, string>(),
        NullMemoryScope.Instance,
        [],
        CancellationToken.None);

    private static CrewTask Task(Orkeon.Domain.Task.ValueObjects.TaskDeliverable? deliverable)
    {
        var task = new CrewTaskBuilder()
            .Description("Write the summary")
            .ExpectedOutput("A markdown document")
            .Build();
        task.SetDeliverable(deliverable);
        return task;
    }

    [Fact]
    public void BuildUserPrompt_ShouldNameThePath_WhenTheAgentWritesTheDeliverable()
    {
        var task = Task(new Orkeon.Domain.Task.ValueObjects.TaskDeliverable
        {
            Path = "/output/synthese_nouveautes.md",
            Source = Orkeon.Domain.Task.ValueObjects.DeliverableSource.ToolCall,
        });

        var prompt = AgentPromptComposer.BuildUserPrompt(
            task, Context(), toolbelt: [new Orkeon.Tests.Shared.Doubles.StubBaseTool("file_read"), new Orkeon.Tests.Shared.Doubles.StubBaseTool("file_write")]);

        Assert.Contains("/output/synthese_nouveautes.md", prompt, StringComparison.Ordinal);
        Assert.Contains("file_write", prompt, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("BR", "CH-392")]
    [InlineData(false)]
    [InlineData(true)]
    public void BuildUserPrompt_ShouldNameThePathWithoutATool_WhenTheAgentLacksFileWrite(bool withOtherTools)
    {
        var task = Task(new Orkeon.Domain.Task.ValueObjects.TaskDeliverable
        {
            Path = "/output/notes-lecture.md",
            Source = Orkeon.Domain.Task.ValueObjects.DeliverableSource.ToolCall,
        });
        IReadOnlyList<IBaseTool>? toolbelt = withOtherTools
            ? [new Orkeon.Tests.Shared.Doubles.StubBaseTool("file_read"), new Orkeon.Tests.Shared.Doubles.StubBaseTool("docx_writer")]
            : null;

        var prompt = AgentPromptComposer.BuildUserPrompt(task, Context(), toolbelt: toolbelt);

        Assert.DoesNotContain("file_write", prompt, StringComparison.Ordinal);
        Assert.Contains("Deliverable: write the final result to '/output/notes-lecture.md'.", prompt, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-392")]
    public void BuildUserPrompt_ShouldNameTheDeliverableOfATruncatedPreviousOutput_WhenItsFileIsKnown()
    {
        var longContent = new string('x', 9000);
        var context = Context() with
        {
            PreviousOutputs =
            [
                new Orkeon.Application.Execution.TaskOutput("read", null, longContent, DateTime.UtcNow, true, TimeSpan.Zero)
                {
                    DeliverablePath = "/output/notes.md",
                },
            ],
        };

        var prompt = AgentPromptComposer.BuildUserPrompt(Task(null), context, toolbelt: [new Orkeon.Tests.Shared.Doubles.StubBaseTool("file_read")]);

        var cut = prompt.IndexOf("... [truncated]", StringComparison.Ordinal);
        var pointer = prompt.IndexOf("[full result in '/output/notes.md': read it with file_read]", StringComparison.Ordinal);
        Assert.True(cut >= 0 && pointer > cut, prompt);
    }

    [Fact]
    [Trait("BR", "CH-392")]
    public void BuildUserPrompt_ShouldNotNameAPath_WhenThePreviousOutputIsCutWithoutAKnownFile()
    {
        var context = Context() with
        {
            PreviousOutputs = [new Orkeon.Application.Execution.TaskOutput("read", null, new string('x', 9000), DateTime.UtcNow, true, TimeSpan.Zero)],
        };

        var prompt = AgentPromptComposer.BuildUserPrompt(Task(null), context);

        Assert.Contains("... [truncated]", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("full result in", prompt, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-392")]
    public void BuildUserPrompt_ShouldNotNameAPath_WhenThePreviousOutputFitsWhole()
    {
        var context = Context() with
        {
            PreviousOutputs =
            [
                new Orkeon.Application.Execution.TaskOutput("read", null, "short notes", DateTime.UtcNow, true, TimeSpan.Zero)
                {
                    DeliverablePath = "/output/notes.md",
                },
            ],
        };

        var prompt = AgentPromptComposer.BuildUserPrompt(Task(null), context);

        Assert.Contains("short notes", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("/output/notes.md", prompt, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-392")]
    public void BuildUserPrompt_ShouldNameTheDeliverableOfAPreviousOutputLeftOut_WhenItsFileIsKnown()
    {
        var context = Context() with
        {
            PreviousOutputs =
            [
                new Orkeon.Application.Execution.TaskOutput("first", null, new string('x', 8000), DateTime.UtcNow, true, TimeSpan.Zero),
                new Orkeon.Application.Execution.TaskOutput("second", null, "left out", DateTime.UtcNow, true, TimeSpan.Zero)
                {
                    DeliverablePath = "/output/second.md",
                },
            ],
        };

        var prompt = AgentPromptComposer.BuildUserPrompt(Task(null), context, toolbelt: [new Orkeon.Tests.Shared.Doubles.StubBaseTool("file_read")]);

        Assert.DoesNotContain("left out", prompt, StringComparison.Ordinal);
        Assert.Contains("[result of task second in '/output/second.md': read it with file_read]", prompt, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-392")]
    public void BuildUserPrompt_ShouldSayTheFrameworkWritesTheAnswer_WhenTheDeliverableIsTheFinalMessage()
    {
        var task = Task(new Orkeon.Domain.Task.ValueObjects.TaskDeliverable
        {
            Path = "/output/report.md",
            Source = Orkeon.Domain.Task.ValueObjects.DeliverableSource.FinalMessage,
        });

        var prompt = AgentPromptComposer.BuildUserPrompt(
            task, Context(), toolbelt: [new Orkeon.Tests.Shared.Doubles.StubBaseTool("file_write")]);

        Assert.Contains(
            "Deliverable: the framework writes your final answer as-is to '/output/report.md': give the complete content, "
            + "not a summary, and do not write the file yourself.",
            prompt,
            StringComparison.Ordinal);
    }

    [Fact]
    public void BuildUserPrompt_ShouldStaySilent_WhenTheDeliverableIsTheStructuredOutput()
    {
        var task = Task(new Orkeon.Domain.Task.ValueObjects.TaskDeliverable
        {
            Path = "/output/report.json",
            SchemaInline = "{\"type\":\"object\"}",
            Source = Orkeon.Domain.Task.ValueObjects.DeliverableSource.StructuredOutput,
        });

        var prompt = AgentPromptComposer.BuildUserPrompt(task, Context());

        Assert.DoesNotContain("Deliverable:", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("/output/report.json", prompt, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("BR", "CH-392")]
    [InlineData(false)]
    [InlineData(true)]
    public void BuildUserPrompt_ShouldNotPointAtAFile_WhenTheReaderLacksFileRead(bool leftOut)
    {
        var first = new Orkeon.Application.Execution.TaskOutput("first", null, new string('x', leftOut ? 8000 : 9000), DateTime.UtcNow, true, TimeSpan.Zero)
        {
            DeliverablePath = "/output/first.md",
        };
        var second = new Orkeon.Application.Execution.TaskOutput("second", null, "left out", DateTime.UtcNow, true, TimeSpan.Zero)
        {
            DeliverablePath = "/output/second.md",
        };
        var context = Context() with { PreviousOutputs = leftOut ? [first, second] : [first] };

        var prompt = AgentPromptComposer.BuildUserPrompt(
            Task(null), context, toolbelt: [new Orkeon.Tests.Shared.Doubles.StubBaseTool("file_write")]);

        Assert.DoesNotContain("file_read", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("/output/first.md", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("/output/second.md", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildUserPrompt_ShouldStaySilent_WhenThereIsNoDeliverable()
    {
        var prompt = AgentPromptComposer.BuildUserPrompt(Task(null), Context());

        Assert.DoesNotContain("Deliverable:", prompt, StringComparison.Ordinal);
    }
}
