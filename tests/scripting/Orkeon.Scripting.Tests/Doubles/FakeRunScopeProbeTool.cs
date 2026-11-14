using System.Collections.Concurrent;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.Execution;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;

namespace Orkeon.Scripting.Tests.Doubles;

/// <summary>
/// A <c>file_write</c> tool that touches nothing: each call records the working root (RT-06) and the
/// file-system scope (#349, as its description) of the run scope it runs in, <c>null</c> without one.
/// </summary>
internal sealed class FakeRunScopeProbeTool : IBaseTool
{
    /// <summary>The working root of each call's run scope, in call order.</summary>
    public ConcurrentQueue<string?> Roots { get; } = new();

    /// <summary>The file-system scope of each call's run scope, described, in call order.</summary>
    public ConcurrentQueue<string?> Scopes { get; } = new();

    public string Name => "file_write";
    public ToolAccess Access => ToolAccess.Read;
    public string Description => "probe";
    public ToolSchema Schema => new(Name, Description, new Dictionary<string, ParameterSchema>());

    public Task<ToolCallResponse> CallAsync(Orkeon.Domain.Tools.Protocol.ToolCallRequest request, CancellationToken cancellationToken = default)
    {
        Roots.Enqueue(AgentRunScope.Current?.Root);
        Scopes.Enqueue(AgentRunScope.Current?.FileSystem?.Describe());
        return Task.FromResult(new ToolCallResponse(true, new Dictionary<string, object?> { ["ok"] = true }, null));
    }

    public Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default)
        => Task.FromResult(ToolResult.CreateSuccess("ok"));

    public bool ValidateInput(string input) => true;
}
