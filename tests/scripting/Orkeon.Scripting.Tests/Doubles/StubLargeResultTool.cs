using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;

namespace Orkeon.Scripting.Tests.Doubles;

/// <summary>A read tool whose every call answers <see cref="Size"/> characters of text: a result worth pruning.</summary>
internal sealed class StubLargeResultTool : IBaseTool
{
    public StubLargeResultTool(string name, int size)
    {
        Name = name;
        Size = size;
    }

    public string Name { get; }
    public int Size { get; }
    public ToolAccess Access => ToolAccess.Read;
    public string Description => "test tool with a large result";
    public ToolSchema Schema => new(Name, Description, new Dictionary<string, ParameterSchema>());

    public Task<ToolCallResponse> CallAsync(Orkeon.Domain.Tools.Protocol.ToolCallRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(new ToolCallResponse(true, new Dictionary<string, object?> { ["text"] = new string('x', Size) }, null));

    public Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default)
        => Task.FromResult(ToolResult.CreateSuccess(new string('x', Size)));

    public bool ValidateInput(string input) => true;
}
