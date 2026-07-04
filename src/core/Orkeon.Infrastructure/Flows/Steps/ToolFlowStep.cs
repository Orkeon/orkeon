using Orkeon.Domain.Tools;
using Orkeon.Domain.Flows.ValueObjects;
using Orkeon.Infrastructure.Flows.Base;

namespace Orkeon.Infrastructure.Flows.Steps;

/// <summary>Typed input for <see cref="ToolFlowStep"/>.</summary>
public sealed class ToolFlowStepInput
{
    /// <summary>Optional runtime override for the tool name.</summary>
    public string? ToolName { get; set; }

    /// <summary>Optional runtime input for the tool execution.</summary>
    public string? Input { get; set; }
}

/// <summary>Typed output for <see cref="ToolFlowStep"/>.</summary>
public sealed class ToolFlowStepOutput
{
    /// <summary>Gets or sets the output text produced by the tool execution.</summary>
    public string ToolOutput { get; set; } = string.Empty;
}

/// <summary>
/// Flow step that executes a tool from the tool registry.
/// </summary>
public class ToolFlowStep : FlowStepBase<ToolFlowStepInput, ToolFlowStepOutput>
{
    private readonly IToolRegistry _toolRegistry;
    private readonly FlowStepParameters _parameters;
    private readonly string _name;

    /// <inheritdoc />
    public override string Name => _name;
    /// <inheritdoc />
    public override string Description => $"Executes tool: {_parameters.Get<string>("tool_name") ?? "unknown"}";

    /// <summary>Initializes a new instance of <see cref="ToolFlowStep"/>.</summary>
    /// <param name="name">The step name.</param>
    /// <param name="parameters">The step parameters.</param>
    /// <param name="toolRegistry">The tool registry for resolving tools.</param>
    public ToolFlowStep(
        string name,
        FlowStepParameters parameters,
        IToolRegistry toolRegistry)
    {
        _name = name;
        _parameters = parameters;
        ArgumentNullException.ThrowIfNull(toolRegistry);
        _toolRegistry = toolRegistry;
    }

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(ToolFlowStepInput request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var toolName = _parameters.Get<string>("tool_name") ?? request.ToolName;
        return string.IsNullOrEmpty(toolName) ? "Missing 'tool_name' parameter." : null;
    }

    /// <inheritdoc />
    protected override Task<ToolFlowStepOutput> ExecuteTypedAsync(
        ToolFlowStepInput request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ExecuteTypedCoreAsync();

        async Task<ToolFlowStepOutput> ExecuteTypedCoreAsync()
        {
            var toolName = _parameters.Get<string>("tool_name") ?? request.ToolName
                ?? throw new InvalidOperationException("Missing 'tool_name' parameter.");

            var tool = await _toolRegistry.GetToolByNameAsync(toolName).ConfigureAwait(false);
            if (tool == null)
            {
                throw new InvalidOperationException($"Tool '{toolName}' not found in registry.");
            }

            // Build input from parameters or runtime context
            var input = _parameters.Get<string>("input")
                        ?? request.Input
                        ?? string.Empty;

            var result = await tool.ExecuteAsync(input, cancellationToken).ConfigureAwait(false);

            if (!result.Success)
            {
                throw new InvalidOperationException($"Tool '{toolName}' execution failed: {result.Error}");
            }

            return new ToolFlowStepOutput
            {
                ToolOutput = result.Output ?? string.Empty
            };
        }
    }
}
