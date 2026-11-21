using Orkeon.Domain.Attributes;
using Orkeon.Domain.Tools;
using ToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;
using ToolCallResponse = Orkeon.Domain.Tools.Protocol.ToolCallResponse;
using ToolSchema = Orkeon.Domain.Tools.Protocol.ToolSchema;
using ParameterSchema = Orkeon.Domain.Tools.Protocol.ParameterSchema;

namespace Orkeon.Domain.Tests.Doubles;

/// <summary>A tool whose contract says it acts on the workspace (#445); it answers every call with success.</summary>
[ToolContract("workspace_stub", ActsOnWorkspace = true)]
public sealed class StubWorkspaceTool : IBaseTool
{
    /// <inheritdoc />
    public string Name => "workspace_stub";

    /// <inheritdoc />
    public string Description => "a workspace stub";

    /// <inheritdoc />
    public ToolSchema Schema { get; } = new("workspace_stub", "a workspace stub", new Dictionary<string, ParameterSchema>());

    /// <inheritdoc />
    public System.Threading.Tasks.Task<ToolCallResponse> CallAsync(ToolCallRequest request, CancellationToken cancellationToken = default) =>
        System.Threading.Tasks.Task.FromResult(new ToolCallResponse(true, null, null));

    /// <inheritdoc />
    public System.Threading.Tasks.Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default) =>
        System.Threading.Tasks.Task.FromResult(ToolResult.CreateSuccess(""));

    /// <inheritdoc />
    public bool ValidateInput(string input) => true;
}
