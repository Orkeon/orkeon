using Orkeon.Domain.Tools;
using ToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;
using ToolCallResponse = Orkeon.Domain.Tools.Protocol.ToolCallResponse;
using ToolSchema = Orkeon.Domain.Tools.Protocol.ToolSchema;

namespace Orkeon.Tests.Shared.Doubles;

/// <summary>
/// A wrapper a host puts around a tool (an observer, a meter, a bridge proxy): it keeps the wrapped tool's name,
/// schema and access, and forwards every call to it.
/// </summary>
public sealed class StubWrapperTool : IBaseTool, IToolWrapper
{
    /// <summary>Wraps <paramref name="inner"/>.</summary>
    public StubWrapperTool(IBaseTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        Inner = inner;
    }

    /// <inheritdoc />
    public IBaseTool Inner { get; }

    /// <inheritdoc />
    public string Name => Inner.Name;

    /// <inheritdoc />
    public string Description => Inner.Description;

    /// <inheritdoc />
    public ToolSchema Schema => Inner.Schema;

    /// <inheritdoc />
    public ToolAccess Access => Inner.Access;

    /// <inheritdoc />
    public ToolAccess AccessFor(IReadOnlyDictionary<string, object?> arguments) => Inner.AccessFor(arguments);

    /// <inheritdoc />
    public Task<ToolCallResponse> CallAsync(ToolCallRequest request, CancellationToken cancellationToken = default) =>
        Inner.CallAsync(request, cancellationToken);

    /// <inheritdoc />
    public Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default) =>
        Inner.ExecuteAsync(input, cancellationToken);

    /// <inheritdoc />
    public bool ValidateInput(string input) => Inner.ValidateInput(input);
}
