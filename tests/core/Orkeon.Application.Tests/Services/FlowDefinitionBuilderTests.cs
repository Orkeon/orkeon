using Orkeon.Application.Flow;
using Orkeon.Domain.Flows;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;
using static Orkeon.Tests.Shared.Constants.TestToolConstants;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;

namespace Orkeon.Application.Tests.Services;

/// <summary>
/// Tests for FlowDefinitionBuilder covering build, validation, and step configuration.
/// </summary>
public class FlowDefinitionBuilderTests
{
    #region Build Sequential Flow

    [Fact]
    public void ShouldBuildSequentialFlow_WhenConfiguredWithSteps()
    {
        // Arrange & Act
        var flow = new FlowDefinitionBuilder()
            .WithName("Sequential Flow")
            .WithDescription("A sequential test flow")
            .AsSequential()
            .AddStep("step1", "action", b => b.WithParameter("param", "value1"))
            .AddStep("step2", "action", b => b.DependsOn("step1"))
            .Build();

        // Assert
        Assert.Equal("Sequential Flow", flow.Name);
        Assert.Equal("A sequential test flow", flow.Description);
        Assert.Equal(FlowType.Sequential, flow.Type);
        Assert.Equal(2, flow.Steps.Count);
        Assert.Equal("step1", flow.Steps[0].Name);
        Assert.Equal("step2", flow.Steps[1].Name);
    }

    #endregion

    #region Build Parallel Flow

    [Fact]
    public void ShouldBuildParallelFlow_WhenConfiguredAsParallel()
    {
        // Arrange & Act
        var flow = new FlowDefinitionBuilder()
            .WithName("Parallel Flow")
            .AsParallel()
            .AddStep("fetch-data", "http")
            .AddStep("fetch-config", "http")
            .AddStep("process", "compute", b => b.DependsOn("fetch-data", "fetch-config"))
            .Build();

        // Assert
        Assert.Equal(FlowType.Parallel, flow.Type);
        Assert.Equal(3, flow.Steps.Count);
        // The "process" step should have 2 dependencies
        var processStep = flow.Steps.First(s => s.Name == "process");
        Assert.Equal(2, processStep.Dependencies.Count);
    }

    #endregion

    #region Build Conditional Flow

    [Fact]
    public void ShouldBuildConditionalFlow_WhenConfiguredAsConditional()
    {
        // Arrange & Act
        var flow = new FlowDefinitionBuilder()
            .WithName("Conditional Flow")
            .AsConditional()
            .AddStep("evaluate", "condition")
            .AddStep("branch-a", "action", b => b.DependsOn("evaluate"))
            .AddStep("branch-b", "action", b => b.DependsOn("evaluate"))
            .Build();

        // Assert
        Assert.Equal(FlowType.Conditional, flow.Type);
        Assert.Equal(3, flow.Steps.Count);
    }

    #endregion

    #region Build Loop Flow

    [Fact]
    public void ShouldBuildLoopFlow_WhenConfiguredAsLoop()
    {
        // Arrange & Act
        var flow = new FlowDefinitionBuilder()
            .WithName("Loop Flow")
            .AsLoop()
            .AddStep("iterate", "loop-body")
            .AddStep("check-condition", "condition")
            .Build();

        // Assert
        Assert.Equal(FlowType.Loop, flow.Type);
        Assert.Equal(2, flow.Steps.Count);
    }

    #endregion

    #region Validation: Missing Name

    [Fact]
    public void ShouldThrowInvalidOperation_WhenBuildingWithMissingName()
    {
        // Arrange
        var builder = new FlowDefinitionBuilder()
            .AddStep("step1", "action");

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => builder.Build());
        Assert.Contains("name", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Validation: No Steps

    [Fact]
    public void ShouldThrowInvalidOperation_WhenBuildingWithNoSteps()
    {
        // Arrange
        var builder = new FlowDefinitionBuilder()
            .WithName("Empty Flow");

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => builder.Build());
        Assert.Contains("step", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Validate: Circular Dependency

    [Fact]
    public void ShouldDetectCircularDependency_WhenValidating()
    {
        // Arrange: Create steps that can form a cycle through resolved IDs
        // step-a depends on step-c, step-b depends on step-a, step-c depends on step-b
        var flow = new FlowDefinitionBuilder()
            .WithName("Circular Flow")
            .AddStep("step-a", "action", b => b.DependsOn("step-c"))
            .AddStep("step-b", "action", b => b.DependsOn("step-a"))
            .AddStep("step-c", "action", b => b.DependsOn("step-b"))
            .Build();

        // Act
        var isValid = flow.Validate(out var errors);

        // Assert
        Assert.False(isValid);
        Assert.Contains(errors, e => e.Contains("Circular dependency", StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    #region Validate: Duplicate Step ID

    [Fact]
    public void ShouldPassValidation_WhenNoDuplicateStepIds()
    {
        // Arrange
        var flow = new FlowDefinitionBuilder()
            .WithName("Valid Flow")
            .AddStep("unique-step-1", "action")
            .AddStep("unique-step-2", "action")
            .Build();

        // Act
        var isValid = flow.Validate(out var errors);

        // Assert
        Assert.True(isValid);
        Assert.Empty(errors);
    }

    #endregion

    #region ResolveStepDependencies By Name

    [Fact]
    public void ShouldResolveStepDependencies_WhenReferencedByName()
    {
        // Arrange & Act
        var flow = new FlowDefinitionBuilder()
            .WithName("Dependency Flow")
            .AddStep("producer", "generate")
            .AddStep("consumer", "process", b => b.DependsOn("producer"))
            .Build();

        // Assert
        var consumerStep = flow.Steps.First(s => s.Name == "consumer");
        var producerStep = flow.Steps.First(s => s.Name == "producer");
        Assert.Single(consumerStep.Dependencies);
        Assert.Equal(producerStep.Id, consumerStep.Dependencies[0]);
    }

    #endregion

    #region AddCrewStep

    [Fact]
    public void ShouldAddCrewStep_WhenConfigured()
    {
        // Arrange & Act
        var flow = new FlowDefinitionBuilder()
            .WithName("Crew Flow")
            .AddCrewStep("research-crew", "research-config.yaml", b =>
                b.WithParameter("max_agents", 5))
            .Build();

        // Assert
        Assert.Single(flow.Steps);
        var step = flow.Steps[0];
        Assert.Equal("research-crew", step.Name);
        Assert.Equal("crew", step.Type);
        Assert.Equal("research-config.yaml", step.Parameters.Get<string>("crew_config"));
    }

    #endregion

    #region AddLlmStep

    [Fact]
    public void ShouldAddLlmStep_WhenConfigured()
    {
        // Arrange & Act
        var flow = new FlowDefinitionBuilder()
            .WithName("LLM Flow")
            .AddLlmStep("summarize", "Summarize the following: {input}", b =>
                b.WithParameter("model", ModelGpt4))
            .Build();

        // Assert
        Assert.Single(flow.Steps);
        var step = flow.Steps[0];
        Assert.Equal("summarize", step.Name);
        Assert.Equal("llm", step.Type);
        Assert.Equal("Summarize the following: {input}", step.Parameters.Get<string>("prompt_template"));
    }

    #endregion

    #region AddToolStep

    [Fact]
    public void ShouldAddToolStep_WhenConfigured()
    {
        // Arrange & Act
        var flow = new FlowDefinitionBuilder()
            .WithName("Tool Flow")
            .AddToolStep("web-search", ToolSearch, b =>
                b.WithParameter(ParamQuery, "AI trends 2026"))
            .Build();

        // Assert
        Assert.Single(flow.Steps);
        var step = flow.Steps[0];
        Assert.Equal("web-search", step.Name);
        Assert.Equal("tool", step.Type);
        Assert.Equal(ToolSearch, step.Parameters.Get<string>("tool_name"));
    }

    #endregion

    #region WithTimeout and WithMaxRetries

    [Fact]
    public void ShouldSetTimeoutAndMaxRetries_WhenConfigured()
    {
        // Arrange & Act
        var timeout = TimeoutExtended;
        var flow = new FlowDefinitionBuilder()
            .WithName("Configured Flow")
            .WithTimeout(timeout)
            .WithMaxRetries(5)
            .AddStep("step1", "action")
            .Build();

        // Assert
        Assert.Equal(timeout, flow.Configuration.Timeout);
        Assert.Equal(5, flow.Configuration.MaxRetries);
    }

    [Fact]
    public void ShouldUseDefaultMaxRetries_WhenNotConfigured()
    {
        // Arrange & Act
        var flow = new FlowDefinitionBuilder()
            .WithName("Default Flow")
            .AddStep("step1", "action")
            .Build();

        // Assert
        Assert.Equal(3, flow.Configuration.MaxRetries);
        Assert.Null(flow.Configuration.Timeout);
    }

    #endregion

    #region Step-Level Configuration

    [Fact]
    public void ShouldConfigureStepTimeout_WhenSet()
    {
        // Arrange & Act
        var stepTimeout = TimeoutQuick;
        var flow = new FlowDefinitionBuilder()
            .WithName("Step Timeout Flow")
            .AddStep("timed-step", "action", b =>
                b.WithTimeout(stepTimeout).WithMaxRetries(2))
            .Build();

        // Assert
        var step = flow.Steps[0];
        Assert.Equal(stepTimeout, step.Timeout);
        Assert.Equal(2, step.MaxRetries);
    }

    #endregion

    #region WithId and WithDescription

    [Fact]
    public void ShouldSetDescription_WhenProvided()
    {
        // Arrange & Act
        var flow = new FlowDefinitionBuilder()
            .WithName("Described Flow")
            .WithDescription("This flow does amazing things")
            .AddStep("step1", "action")
            .Build();

        // Assert
        Assert.Equal("This flow does amazing things", flow.Description);
    }

    [Fact]
    public void ShouldThrow_WhenNullNameProvided()
    {
        // Arrange
        var builder = new FlowDefinitionBuilder();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => builder.WithName(null!));
    }

    [Fact]
    public void ShouldThrow_WhenNullDescriptionProvided()
    {
        // Arrange
        var builder = new FlowDefinitionBuilder();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => builder.WithDescription(null!));
    }

    #endregion

    #region Complex Scenario

    [Fact]
    public void ShouldBuildComplexFlow_WhenMultipleStepTypesUsed()
    {
        // Arrange & Act
        var flow = new FlowDefinitionBuilder()
            .WithName("Research Pipeline")
            .WithDescription("End-to-end research flow")
            .AsSequential()
            .WithTimeout(TimeSpan.FromHours(1))
            .WithMaxRetries(2)
            .AddToolStep("search", "WebSearch", b =>
                b.WithParameter(ParamQuery, "AI research"))
            .AddLlmStep("analyze", "Analyze: {search_output}", b =>
                b.DependsOn("search"))
            .AddCrewStep("review", "review-crew.yaml", b =>
                b.DependsOn("analyze")
                 .WithTimeout(TimeoutLong))
            .AddStep("publish", "output", b =>
                b.DependsOn("review")
                 .WithParameter("format", "pdf"))
            .Build();

        // Assert
        Assert.Equal("Research Pipeline", flow.Name);
        Assert.Equal(4, flow.Steps.Count);
        Assert.True(flow.Validate(out var errors));
        Assert.Empty(errors);

        // Verify dependency chain
        var analyzeStep = flow.Steps.First(s => s.Name == "analyze");
        var searchStep = flow.Steps.First(s => s.Name == "search");
        Assert.Single(analyzeStep.Dependencies);
        Assert.Equal(searchStep.Id, analyzeStep.Dependencies[0]);
    }

    #endregion
}
