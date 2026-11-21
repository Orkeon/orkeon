using Orkeon.Domain.Attributes;
using Orkeon.Domain.Tools;
using ToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;
using ToolCallResponse = Orkeon.Domain.Tools.Protocol.ToolCallResponse;
using ToolSchema = Orkeon.Domain.Tools.Protocol.ToolSchema;
using ParameterSchema = Orkeon.Domain.Tools.Protocol.ParameterSchema;

namespace Orkeon.Domain.Tests.Doubles;

/// <summary>A tool of the caller's own run (#445): its contract says it stays with its caller.</summary>
[ToolContract("caller_stub", StaysWithCaller = true)]
public class StubCallerTool : IBaseTool
{
    /// <inheritdoc />
    public string Name => "caller_stub";

    /// <inheritdoc />
    public string Description => "a caller stub";

    /// <inheritdoc />
    public ToolSchema Schema { get; } = new("caller_stub", "a caller stub", new Dictionary<string, ParameterSchema>());

    /// <inheritdoc />
    public System.Threading.Tasks.Task<ToolCallResponse> CallAsync(ToolCallRequest request, CancellationToken cancellationToken = default) =>
        System.Threading.Tasks.Task.FromResult(new ToolCallResponse(true, null, null));

    /// <inheritdoc />
    public System.Threading.Tasks.Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default) =>
        System.Threading.Tasks.Task.FromResult(ToolResult.CreateSuccess(""));

    /// <inheritdoc />
    public bool ValidateInput(string input) => true;
}

/// <summary>A subclass that declares no contract of its own: it keeps its base class's.</summary>
public sealed class StubDerivedCallerTool : StubCallerTool;
