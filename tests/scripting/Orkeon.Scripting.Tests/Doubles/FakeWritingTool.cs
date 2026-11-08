using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;

namespace Orkeon.Scripting.Tests.Doubles;

/// <summary>A tool with <see cref="ToolAccess.Edit"/> access (or the one given) that counts the calls it runs.</summary>
internal sealed class FakeWritingTool(string name, ToolAccess access = ToolAccess.Edit) : IBaseTool
{
    public int CallCount;

    public string Name { get; } = name;
    public ToolAccess Access { get; } = access;
    public string Description => "test writing tool";
    public ToolSchema Schema => new(Name, Description, new Dictionary<string, ParameterSchema>());

    public Task<ToolCallResponse> CallAsync(Orkeon.Domain.Tools.Protocol.ToolCallRequest request, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref CallCount);
        return Task.FromResult(new ToolCallResponse(true, new Dictionary<string, object?> { ["ok"] = true }, null));
    }

    public Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default)
        => Task.FromResult(ToolResult.CreateSuccess("ok"));

    public bool ValidateInput(string input) => true;
}
