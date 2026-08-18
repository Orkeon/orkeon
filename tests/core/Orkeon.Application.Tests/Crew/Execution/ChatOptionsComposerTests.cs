using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainTask = Orkeon.Domain.Task.CrewTask;
using Microsoft.Extensions.AI;
using Orkeon.Application.Common.DTOs;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Tests.Doubles;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Common;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Application.Tests.Crew.Execution;

/// <summary>
/// SONAR-14 T2: pins the per-call ChatOptions composition — toolbelt resolution
/// (registry + agent-owned + auto-injected human_input), the agent- and task-level LLM
/// override cascade, and the structured-output GBNF grammar attachment.
/// </summary>
public class ChatOptionsComposerTests
{
    private static DomainAgent BuildAgent(LlmConfig? llmConfig = null, params ITool[] tools)
    {
        var builder = new AgentBuilder()
            .Role("Composer Agent")
            .Goal("Exercise option composition");

        foreach (var tool in tools)
            builder = builder.WithTool(tool);

        if (llmConfig is not null)
            builder = builder.WithLlmConfig(llmConfig);

        return builder.Build();
    }

    private static DomainTask BuildTask() =>
        DomainTask.Create(
            TaskDescription.From("Compose options"),
            ExpectedOutput.From("Options"));

    private static ChatOptionsComposer BuildComposer(
        IEnumerable<Orkeon.Domain.Tools.IBaseTool>? registeredTools = null,
        FakeFileSystemService? fileSystem = null) =>
        new(new SpyExecutionLogger(), registeredTools, fileSystem ?? new FakeFileSystemService());

    // ── Toolbelt resolution ───────────────────────────────────────────────

    [Fact]
    public async System.Threading.Tasks.Task ResolvesRegistryTools_AndPublishesTheirSchemas()
    {
        var tool = new SpyTool("registry_tool");
        var agent = BuildAgent(null, tool);
        var composer = BuildComposer(registeredTools: [tool, new SpyTool("unrelated_tool")]);

        var (options, availableTools) = await composer.BuildChatOptionsAsync(agent, BuildTask(), TestContext.Current.CancellationToken);

        Assert.Equal("registry_tool", Assert.Single(availableTools).Name);
        Assert.NotNull(options.Tools);
        Assert.Single(options.Tools!);
        Assert.Equal(ChatToolMode.Auto, options.ToolMode);

        var schemas = Assert.IsType<IReadOnlyList<ToolSchema>>(
            options.AdditionalProperties!["orkeon:tool_schemas"], exactMatch: false);
        Assert.Equal("registry_tool", Assert.Single(schemas).Name);
        Assert.Equal(ToolCallMode.Auto, options.AdditionalProperties!["orkeon:tool_mode"]);
    }

    [Fact]
    public async System.Threading.Tasks.Task IncludesAgentOwnedTools_MissingFromTheRegistry()
    {
        var delegation = new SpyTool("delegate_work");
        var agent = BuildAgent(null, delegation);
        var composer = BuildComposer(registeredTools: []);

        var (_, availableTools) = await composer.BuildChatOptionsAsync(agent, BuildTask(), TestContext.Current.CancellationToken);

        Assert.Equal("delegate_work", Assert.Single(availableTools).Name);
    }

    [Fact]
    public async System.Threading.Tasks.Task LeavesToolsNull_ForAToollessAgent()
    {
        var composer = BuildComposer();

        var (options, availableTools) = await composer.BuildChatOptionsAsync(BuildAgent(), BuildTask(), TestContext.Current.CancellationToken);

        Assert.Empty(availableTools);
        Assert.Null(options.Tools);
    }

    [Fact]
    public async System.Threading.Tasks.Task TheComposedAIFunction_InvokesTheUnderlyingTool()
    {
        var tool = new SpyTool("callable_tool", result: "invoked!");
        var agent = BuildAgent(null, tool);
        var composer = BuildComposer(registeredTools: [tool]);

        var (options, _) = await composer.BuildChatOptionsAsync(agent, BuildTask(), TestContext.Current.CancellationToken);

        var function = Assert.IsType<AIFunction>(Assert.Single(options.Tools!), exactMatch: false);
        var result = await function.InvokeAsync(
            new AIFunctionArguments { ["input"] = "hello" }, TestContext.Current.CancellationToken);

        Assert.Equal("invoked!", result?.ToString());
        var call = Assert.Single(tool.Calls);
        Assert.Equal("hello", call.Parameters["input"]);
    }

    // ── human_input auto-injection ────────────────────────────────────────

    [Fact]
    public async System.Threading.Tasks.Task InjectsHumanInput_WhenTheTaskAsksForIt_AndTheToolIsRegistered()
    {
        var humanInput = new SpyTool("human_input");
        var agent = BuildAgent();
        var composer = BuildComposer(registeredTools: [humanInput]);
        var task = new Orkeon.Domain.Task.CrewTaskBuilder()
            .Description("Interactive task").ExpectedOutput("Answer").HumanInput().Build();

        var (_, availableTools) = await composer.BuildChatOptionsAsync(agent, task, TestContext.Current.CancellationToken);

        Assert.Equal("human_input", Assert.Single(availableTools).Name);
    }

    [Fact]
    public async System.Threading.Tasks.Task HumanInputFlagIsANoOp_WhenNoProviderIsRegistered()
    {
        var composer = BuildComposer(registeredTools: []);
        var task = new Orkeon.Domain.Task.CrewTaskBuilder()
            .Description("Interactive task").ExpectedOutput("Answer").HumanInput().Build();

        var (_, availableTools) = await composer.BuildChatOptionsAsync(BuildAgent(), task, TestContext.Current.CancellationToken);

        Assert.Empty(availableTools);
    }

    [Fact]
    public async System.Threading.Tasks.Task DoesNotDuplicateHumanInput_WhenTheAgentAlreadyCarriesIt()
    {
        var humanInput = new SpyTool("human_input");
        var agent = BuildAgent(null, humanInput);
        var composer = BuildComposer(registeredTools: [humanInput]);
        var task = new Orkeon.Domain.Task.CrewTaskBuilder()
            .Description("Interactive task").ExpectedOutput("Answer").HumanInput().Build();

        var (_, availableTools) = await composer.BuildChatOptionsAsync(agent, task, TestContext.Current.CancellationToken);

        Assert.Single(availableTools);
    }

    // ── Agent-level LLM overrides ─────────────────────────────────────────

    [Fact]
    public async System.Threading.Tasks.Task AppliesExplicitAgentOverrides_AndLeavesDefaultsAlone()
    {
        var config = LlmConfig.Create("planner-model") with
        {
            Temperature = 0.2,
            MaxTokens = 512,
            TopP = 0.9,
        };
        var agent = BuildAgent(config);
        var composer = BuildComposer();

        var (options, _) = await composer.BuildChatOptionsAsync(agent, BuildTask(), TestContext.Current.CancellationToken);

        Assert.Equal("planner-model", options.ModelId);
        Assert.Equal(0.2f, options.Temperature);
        Assert.Equal(512, options.MaxOutputTokens);
        Assert.Equal(0.9f, options.TopP);
    }

    [Fact]
    public async System.Threading.Tasks.Task DoesNotForwardAnything_WhenTheAgentHasNoLlmConfig()
    {
        var composer = BuildComposer();

        var (options, _) = await composer.BuildChatOptionsAsync(BuildAgent(), BuildTask(), TestContext.Current.CancellationToken);

        Assert.Null(options.ModelId);
        Assert.Null(options.Temperature);
        Assert.Null(options.MaxOutputTokens);
        Assert.Null(options.TopP);
    }

    [Fact]
    public async System.Threading.Tasks.Task ForwardsThinkingAndResponseFormat_FromTheAgentConfig()
    {
        var config = LlmConfig.Create("m") with
        {
            Thinking = new LlmThinkingConfig { Enabled = true },
            ResponseFormat = LlmResponseFormat.JsonObject(),
        };
        var agent = BuildAgent(config);
        var composer = BuildComposer();

        var (options, _) = await composer.BuildChatOptionsAsync(agent, BuildTask(), TestContext.Current.CancellationToken);

        Assert.NotNull(options.AdditionalProperties);
        Assert.Equal(config.Thinking, options.AdditionalProperties![LlmChatOptionsKeys.Thinking]);
        Assert.Equal(LlmResponseFormat.JsonObject(), options.AdditionalProperties![LlmChatOptionsKeys.ResponseFormat]);
    }

    // ── Task-level overrides win ──────────────────────────────────────────

    [Fact]
    public async System.Threading.Tasks.Task TaskOverridesWin_OverAgentOverrides()
    {
        var agent = BuildAgent(LlmConfig.Create("m") with { Temperature = 0.2 });
        var composer = BuildComposer();
        var task = new Orkeon.Domain.Task.CrewTaskBuilder()
            .Description("Override me").ExpectedOutput("x")
            .WithLlmOverride(new LlmConfigOverride
            {
                Temperature = 0.9,
                MaxTokens = 128,
                TopP = 0.5,
                ResponseFormat = LlmResponseFormat.JsonObject(),
            })
            .Build();

        var (options, _) = await composer.BuildChatOptionsAsync(agent, task, TestContext.Current.CancellationToken);

        Assert.Equal(0.9f, options.Temperature);
        Assert.Equal(128, options.MaxOutputTokens);
        Assert.Equal(0.5f, options.TopP);
        Assert.Equal(LlmResponseFormat.JsonObject(), options.AdditionalProperties![LlmChatOptionsKeys.ResponseFormat]);
    }

    // ── Structured-output grammar attachment ──────────────────────────────

    [Fact]
    public async System.Threading.Tasks.Task AttachesAGbnfGrammar_ForAnInlineSchema()
    {
        var composer = BuildComposer();
        var task = BuildTask();
        task.SetDeliverable(new TaskDeliverable
        {
            Path = "/output/result.json",
            Source = DeliverableSource.StructuredOutput,
            SchemaInline = """{"type":"object","properties":{"name":{"type":"string"}}}""",
        });

        var (options, _) = await composer.BuildChatOptionsAsync(BuildAgent(), task, TestContext.Current.CancellationToken);

        var grammar = Assert.IsType<string>(options.AdditionalProperties!["orkeon:grammar_gbnf"]);
        Assert.Contains("root", grammar, StringComparison.Ordinal);
    }

    [Fact]
    public async System.Threading.Tasks.Task ReadsTheSchemaFromTheVfs_WhenOnlyAPathIsDeclared()
    {
        var fileSystem = new FakeFileSystemService()
            .AddFile("/schemas/out.json", """{"type":"object"}""");
        var composer = BuildComposer(fileSystem: fileSystem);
        var task = BuildTask();
        task.SetDeliverable(new TaskDeliverable
        {
            Path = "/output/result.json",
            Source = DeliverableSource.StructuredOutput,
            SchemaPath = "/schemas/out.json",
        });

        var (options, _) = await composer.BuildChatOptionsAsync(BuildAgent(), task, TestContext.Current.CancellationToken);

        Assert.True(options.AdditionalProperties!.ContainsKey("orkeon:grammar_gbnf"));
    }

    [Fact]
    public async System.Threading.Tasks.Task SwallowsAnInvalidSchema_AndStillReturnsOptions()
    {
        var composer = BuildComposer();
        var task = BuildTask();
        task.SetDeliverable(new TaskDeliverable
        {
            Path = "/output/result.json",
            Source = DeliverableSource.StructuredOutput,
            SchemaInline = "this is not json",
        });

        var (options, _) = await composer.BuildChatOptionsAsync(BuildAgent(), task, TestContext.Current.CancellationToken);

        Assert.True(options.AdditionalProperties is null
            || !options.AdditionalProperties.ContainsKey("orkeon:grammar_gbnf"));
    }

    [Fact]
    public async System.Threading.Tasks.Task IgnoresDeliverables_WhoseSourceIsNotStructuredOutput()
    {
        var composer = BuildComposer();
        var task = BuildTask();
        task.SetDeliverable(new TaskDeliverable
        {
            Path = "/output/report.md",
            Source = DeliverableSource.FinalMessage,
        });

        var (options, _) = await composer.BuildChatOptionsAsync(BuildAgent(), task, TestContext.Current.CancellationToken);

        Assert.True(options.AdditionalProperties is null
            || !options.AdditionalProperties.ContainsKey("orkeon:grammar_gbnf"));
    }
}
