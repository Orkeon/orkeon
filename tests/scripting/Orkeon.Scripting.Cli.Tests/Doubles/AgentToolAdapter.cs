using Orkeon.Domain.Common;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;
using ToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;

namespace Orkeon.Scripting.Cli.Tests.Doubles;

/// <summary>
/// Hands a base tool to an agent built in code: the agent builder takes <see cref="ITool"/>,
/// and a tool like <c>rag_search</c> implements only <see cref="IBaseTool"/>. Forwards
/// everything to the tool it wraps.
/// </summary>
internal sealed class AgentToolAdapter(IBaseTool inner) : ITool
{
    public string Name => inner.Name;

    public string Description => inner.Description;

    public ToolSchema Schema => inner.Schema;

    public ToolAccess Access => inner.Access;

    public Task<ToolCallResponse> CallAsync(ToolCallRequest request, CancellationToken cancellationToken = default)
        => inner.CallAsync(request, cancellationToken);

    public Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default)
        => inner.ExecuteAsync(input, cancellationToken);

    public bool ValidateInput(string input) => inner.ValidateInput(input);
}
