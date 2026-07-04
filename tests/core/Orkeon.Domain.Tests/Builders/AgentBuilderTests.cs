using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Common;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.SharedKernel.ValueObjects;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using ProtocolToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;

namespace Orkeon.Domain.Tests.Builders;

public class AgentBuilderTests
{
    private static AgentBuilder MinimalAgent() =>
        new AgentBuilder().Role("Test Role").Goal(TestGoal);

    [Fact]
    public void Build_WithRoleAndGoal_CreatesAgent()
    {
        // Act
        var agent = MinimalAgent().Build();

        // Assert
        Assert.NotNull(agent);
        Assert.Equal("Test Role", agent.Role.Value);
        Assert.Equal(TestGoal, agent.Goal.Value);
    }

    [Fact]
    public void Build_WithoutRole_ThrowsValidation()
    {
        // Arrange
        var builder = new AgentBuilder().Goal(TestGoal);

        // Act & Assert
        var ex = Assert.Throws<BuilderValidationException>(() => builder.Build());
        Assert.Equal("Agent", ex.BuilderName);
        Assert.Contains("Role is required", ex.Message);
    }

    [Fact]
    public void Build_WithoutGoal_ThrowsValidation()
    {
        // Arrange
        var builder = new AgentBuilder().Role("Test Role");

        // Act & Assert
        var ex = Assert.Throws<BuilderValidationException>(() => builder.Build());
        Assert.Equal("Agent", ex.BuilderName);
        Assert.Contains("Goal is required", ex.Message);
    }

    [Fact]
    public void Build_WithAllOptions_SetsAllProperties()
    {
        // Arrange
        var llm = new StubLlmProvider();
        var stepCallback = new StubStepCallback();

        // Act
        var agent = new AgentBuilder()
            .Role(RoleSeniorDeveloper)
            .Goal("Write clean code")
            .Backstory("Experienced developer with 10 years")
            .AllowDelegation()
            .MaxIterations(20)
            .MaxRpm(50)
            .Verbose()
            .MaxExecutionTime(TimeoutStandard)
            .CacheEnabled(false)
            .SystemTemplate("You are a developer")
            .PromptTemplate("Task: {task}")
            .ResponseTemplate("Result: {result}")
            .MaxRetryLimit(3)
            .WithLlm(llm)
            .WithStepCallback(stepCallback)
            .Build();

        // Assert
        Assert.Equal(RoleSeniorDeveloper, agent.Role.Value);
        Assert.Equal("Write clean code", agent.Goal.Value);
        Assert.Equal("Experienced developer with 10 years", agent.Backstory!);
        Assert.True(agent.AllowDelegation);
        Assert.Equal(20, agent.MaxIterations);
        Assert.Equal(50, agent.MaxRpm);
        Assert.True(agent.Verbose);
        Assert.Equal(TimeoutStandard, agent.MaxExecutionTime);
        Assert.False(agent.CacheEnabled);
        Assert.Equal("You are a developer", agent.SystemTemplate);
        Assert.Equal("Task: {task}", agent.PromptTemplate);
        Assert.Equal("Result: {result}", agent.ResponseTemplate);
        Assert.Equal(3, agent.MaxRetryLimit);
        Assert.Same(llm, agent.FunctionCallingLlm);
        Assert.Same(stepCallback, agent.StepCallback);
    }

    [Fact]
    public void Build_WithTools_AddsToolsToAgent()
    {
        // Arrange
        var tool1 = new StubTool("tool_one");
        var tool2 = new StubTool("tool_two");

        // Act
        var agent = MinimalAgent()
            .WithTool(tool1)
            .WithTool(tool2)
            .Build();

        // Assert
        Assert.Equal(2, agent.Tools.Count);
        Assert.Same(tool1, agent.Tools[0]);
        Assert.Same(tool2, agent.Tools[1]);
    }

    [Fact]
    public void Build_WithStringRole_CreatesAgentRole()
    {
        // Act
        var agent = new AgentBuilder()
            .Role(RoleAnalyst)
            .Goal(GoalAnalyzeData)
            .Build();

        // Assert
        Assert.IsType<AgentRole>(agent.Role);
        Assert.Equal(RoleAnalyst, agent.Role.Value);
    }

    [Fact]
    public void Verbose_DefaultTrue_SetsVerbose()
    {
        // Act — .Verbose() without param sets true
        var agent = MinimalAgent().Verbose().Build();

        // Assert
        Assert.True(agent.Verbose);
    }

    [Fact]
    public void Build_WithLlm_SetsFunctionCallingLlm()
    {
        // Arrange
        var llm = new StubLlmProvider();

        // Act
        var agent = MinimalAgent().WithLlm(llm).Build();

        // Assert
        Assert.NotNull(agent.FunctionCallingLlm);
        Assert.Same(llm, agent.FunctionCallingLlm);
    }

    [Fact]
    public void Thinking_DefaultArgs_EnablesThinkingWithoutEffort()
    {
        var agent = MinimalAgent().Thinking().Build();

        Assert.NotNull(agent.LlmConfig);
        Assert.NotNull(agent.LlmConfig!.Thinking);
        Assert.True(agent.LlmConfig.Thinking!.Enabled);
        Assert.Null(agent.LlmConfig.Thinking.Effort);
    }

    [Fact]
    public void Thinking_WithEffort_SetsEnabledAndEffort()
    {
        var agent = MinimalAgent().Thinking(enabled: true, effort: "max").Build();

        Assert.Equal(true, agent.LlmConfig!.Thinking!.Enabled);
        Assert.Equal("max", agent.LlmConfig.Thinking.Effort);
    }

    [Fact]
    public void MaxOutputTokens_SetsMaxTokensOnConfig()
    {
        var agent = MinimalAgent().MaxOutputTokens(256).Build();

        Assert.NotNull(agent.LlmConfig);
        Assert.Equal(256, agent.LlmConfig!.MaxTokens);
    }

    [Fact]
    public void ThinkingAndMaxOutputTokens_Compose_WithoutClobbering()
    {
        var agent = MinimalAgent()
            .Thinking(enabled: false)
            .MaxOutputTokens(512)
            .Build();

        Assert.Equal(false, agent.LlmConfig!.Thinking!.Enabled);
        Assert.Equal(512, agent.LlmConfig.MaxTokens);
    }

    [Fact]
    public void Sugar_MergesIntoExistingWithLlmConfig_PreservingModel()
    {
        var agent = MinimalAgent()
            .WithLlmConfig(LlmConfig.Create("deepseek-v4-pro"))
            .Thinking(enabled: true, effort: "high")
            .MaxOutputTokens(1024)
            .Build();

        Assert.Equal("deepseek-v4-pro", agent.LlmConfig!.Model);
        Assert.Equal("high", agent.LlmConfig.Thinking!.Effort);
        Assert.Equal(1024, agent.LlmConfig.MaxTokens);
    }

    [Fact]
    public void MaxOutputTokens_NonPositive_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MinimalAgent().MaxOutputTokens(0));
    }

    #region Test Doubles

    private sealed class StubTool : ITool
    {
        public string Name { get; }
        public string Description => $"Stub tool {Name}";
        public ToolSchema Schema => new(Name, Description, []);

        public StubTool(string name) => Name = name;

        public System.Threading.Tasks.Task<ToolCallResponse> CallAsync(ProtocolToolCallRequest request, CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.FromResult(new ToolCallResponse(true, "ok", null));

        public System.Threading.Tasks.Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.FromResult(ToolResult.CreateSuccess("ok"));

        public bool ValidateInput(string input) => true;
    }

    private sealed class StubLlmProvider : ILlmProvider
    {
        public string Name => "StubLlm";

        public System.Threading.Tasks.Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.FromResult(new LlmResponse { Content = "stub response" });

        public System.Threading.Tasks.Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.FromResult(new LlmResponse { Content = "stub response" });
    }

    private sealed class StubStepCallback : IStepCallback
    {
        public System.Threading.Tasks.Task OnStepStartAsync(DomainAgent agent, Orkeon.Domain.Task.ICrewTask task, int iteration)
            => System.Threading.Tasks.Task.CompletedTask;

        public System.Threading.Tasks.Task OnStepCompletedAsync(DomainAgent agent, Orkeon.Domain.Task.ICrewTask task, int iteration, Orkeon.Domain.Agent.AgentStep step)
            => System.Threading.Tasks.Task.CompletedTask;

        public System.Threading.Tasks.Task OnStepFailedAsync(DomainAgent agent, Orkeon.Domain.Task.ICrewTask task, int iteration, string error)
            => System.Threading.Tasks.Task.CompletedTask;
    }

    #endregion
}
