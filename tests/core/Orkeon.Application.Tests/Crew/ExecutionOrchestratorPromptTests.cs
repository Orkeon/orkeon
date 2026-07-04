using System.Text;
using Orkeon.Application.Crew;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Common;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Application.Tests.Crew;

/// <summary>
/// Tests verifying that the system prompt correctly includes or excludes
/// text-based tool call instructions depending on native tool calling support.
/// </summary>
public class ExecutionOrchestratorPromptTests
{
    /// <summary>
    /// When native tool calling is supported, the prompt should NOT contain
    /// [TOOL_CALL] instructions or the "How to Call Tools" section.
    /// </summary>
    [Fact]
    public void AppendToolsSection_WithNativeToolCalling_ExcludesToolCallInstructions()
    {
        // Arrange
        var agent = CreateAgentWithTool();
        var prompt = new StringBuilder();

        // Act
        ExecutionOrchestrator.AppendToolsSection(prompt, agent, supportsNativeToolCalling: true);

        // Assert
        var result = prompt.ToString();
        Assert.DoesNotContain("[TOOL_CALL]", result);
        Assert.DoesNotContain("[/TOOL_CALL]", result);
        Assert.DoesNotContain("## How to Call Tools", result);
    }

    /// <summary>
    /// When native tool calling is NOT supported, the prompt SHOULD contain
    /// [TOOL_CALL] instructions and the "How to Call Tools" section.
    /// </summary>
    [Fact]
    public void AppendToolsSection_WithoutNativeToolCalling_IncludesToolCallInstructions()
    {
        // Arrange
        var agent = CreateAgentWithTool();
        var prompt = new StringBuilder();

        // Act
        ExecutionOrchestrator.AppendToolsSection(prompt, agent, supportsNativeToolCalling: false);

        // Assert
        var result = prompt.ToString();
        Assert.Contains("[TOOL_CALL]", result);
        Assert.Contains("[/TOOL_CALL]", result);
        Assert.Contains("## How to Call Tools", result);
        Assert.Contains("tool => \"tool_name\"", result);
    }

    /// <summary>
    /// In both cases (native and text-based), the "Available Tools" section
    /// with tool names and descriptions must always be present.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AppendToolsSection_AlwaysIncludesAvailableTools(bool supportsNativeToolCalling)
    {
        // Arrange
        var agent = CreateAgentWithTool();
        var prompt = new StringBuilder();

        // Act
        ExecutionOrchestrator.AppendToolsSection(prompt, agent, supportsNativeToolCalling);

        // Assert
        var result = prompt.ToString();
        Assert.Contains("## Available Tools", result);
        Assert.Contains("test_tool", result);
        Assert.Contains("A test tool for unit testing", result);
    }

    /// <summary>
    /// When the agent has no tools, nothing should be appended regardless
    /// of the native tool calling flag.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AppendToolsSection_NoTools_AppendsNothing(bool supportsNativeToolCalling)
    {
        // Arrange
        var agent = CreateAgentWithoutTools();
        var prompt = new StringBuilder();

        // Act
        ExecutionOrchestrator.AppendToolsSection(prompt, agent, supportsNativeToolCalling);

        // Assert
        Assert.Equal(string.Empty, prompt.ToString());
    }

    /// <summary>
    /// When native tool calling is supported, the prompt should still list
    /// tool parameters (Required/Optional) in the Available Tools section.
    /// </summary>
    [Fact]
    public void AppendToolsSection_WithNativeToolCalling_IncludesToolParameters()
    {
        // Arrange
        var agent = CreateAgentWithToolAndParameters();
        var prompt = new StringBuilder();

        // Act
        ExecutionOrchestrator.AppendToolsSection(prompt, agent, supportsNativeToolCalling: true);

        // Assert
        var result = prompt.ToString();
        Assert.Contains("## Available Tools", result);
        Assert.Contains("Required: path", result);
        Assert.Contains("Optional: encoding", result);
        Assert.DoesNotContain("[TOOL_CALL]", result);
    }

    /// <summary>
    /// AppendToolParameters: tool with only required params emits Required line but no Optional line.
    /// </summary>
    [Fact]
    public void AppendToolsSection_ToolWithRequiredParamsOnly_EmitsRequiredLineOnly()
    {
        var parameters = new Dictionary<string, ParameterSchema>
        {
            ["query"] = new("string", "The search query", Required: true)
        };
        var schema = new ToolSchema("search", "Searches the web", parameters);
        var tool = new StubTool("search", "Searches the web", schema);
        var agent = new AgentBuilder().Role("R").Goal("G").WithTool(tool).Build();
        var prompt = new StringBuilder();

        ExecutionOrchestrator.AppendToolsSection(prompt, agent, supportsNativeToolCalling: true);

        var result = prompt.ToString();
        Assert.Contains("Required: query", result);
        Assert.DoesNotContain("Optional:", result);
    }

    /// <summary>
    /// AppendToolParameters: tool with optional param that has a default value renders "(default: X)" suffix.
    /// </summary>
    [Fact]
    public void AppendToolsSection_ToolWithOptionalParamAndDefault_RendersDefaultSuffix()
    {
        var parameters = new Dictionary<string, ParameterSchema>
        {
            ["format"] = new("string", "Output format", Required: false, Default: "json")
        };
        var schema = new ToolSchema("export", "Exports data", parameters);
        var tool = new StubTool("export", "Exports data", schema);
        var agent = new AgentBuilder().Role("R").Goal("G").WithTool(tool).Build();
        var prompt = new StringBuilder();

        ExecutionOrchestrator.AppendToolsSection(prompt, agent, supportsNativeToolCalling: true);

        var result = prompt.ToString();
        Assert.Contains("Optional: format (default: json)", result);
        Assert.DoesNotContain("Required:", result);
    }

    /// <summary>
    /// AppendToolParameters: tool with no schema emits only the tool name/description, no param lines.
    /// </summary>
    [Fact]
    public void AppendToolsSection_ToolWithNoSchema_EmitsNoParamLines()
    {
        var tool = new StubTool("ping", "Pings a host");
        var agent = new AgentBuilder().Role("R").Goal("G").WithTool(tool).Build();
        var prompt = new StringBuilder();

        ExecutionOrchestrator.AppendToolsSection(prompt, agent, supportsNativeToolCalling: true);

        var result = prompt.ToString();
        Assert.Contains("ping", result);
        Assert.DoesNotContain("Required:", result);
        Assert.DoesNotContain("Optional:", result);
    }

    /// <summary>
    /// AppendToolParameters: tool with optional param and no default renders the key name only (no suffix).
    /// </summary>
    [Fact]
    public void AppendToolsSection_ToolWithOptionalParamNoDefault_RendersKeyWithNoSuffix()
    {
        var parameters = new Dictionary<string, ParameterSchema>
        {
            ["timeout"] = new("integer", "Timeout in ms", Required: false)
        };
        var schema = new ToolSchema("call", "Makes a call", parameters);
        var tool = new StubTool("call", "Makes a call", schema);
        var agent = new AgentBuilder().Role("R").Goal("G").WithTool(tool).Build();
        var prompt = new StringBuilder();

        ExecutionOrchestrator.AppendToolsSection(prompt, agent, supportsNativeToolCalling: true);

        var result = prompt.ToString();
        Assert.Contains("Optional: timeout", result);
        Assert.DoesNotContain("(default:", result);
    }

    private static DomainAgent CreateAgentWithTool()
    {
        var tool = new StubTool("test_tool", "A test tool for unit testing");
        return new AgentBuilder()
            .Role("Tester")
            .Goal("Run tests")
            .WithTool(tool)
            .Build();
    }

    private static DomainAgent CreateAgentWithoutTools()
    {
        return new AgentBuilder()
            .Role("Tester")
            .Goal("Run tests")
            .Build();
    }

    private static DomainAgent CreateAgentWithToolAndParameters()
    {
        var parameters = new Dictionary<string, ParameterSchema>
        {
            ["path"] = new("string", "The file path", Required: true),
            ["encoding"] = new("string", "The file encoding", Required: false, Default: "utf-8")
        };
        var schema = new ToolSchema("file_read", "Reads a file", parameters);
        var tool = new StubTool("file_read", "Reads a file from disk", schema);

        return new AgentBuilder()
            .Role("Tester")
            .Goal("Run tests")
            .WithTool(tool)
            .Build();
    }

    /// <summary>
    /// Minimal ITool stub for testing prompt generation.
    /// </summary>
    private sealed class StubTool : ITool
    {
        public string Name { get; }
        public string Description { get; }
        public ToolSchema Schema { get; }

        public StubTool(string name, string description, ToolSchema? schema = null)
        {
            Name = name;
            Description = description;
            Schema = schema ?? new ToolSchema(name, description, new Dictionary<string, ParameterSchema>());
        }

        public System.Threading.Tasks.Task<ToolCallResponse> CallAsync(
            Orkeon.Domain.Tools.Protocol.ToolCallRequest request, CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.FromResult(
                new ToolCallResponse(true, "stub result", null));

        public System.Threading.Tasks.Task<ToolResult> ExecuteAsync(
            string input, CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.FromResult(
                ToolResult.CreateSuccess("stub result"));

        public bool ValidateInput(string input) => true;
    }
}
