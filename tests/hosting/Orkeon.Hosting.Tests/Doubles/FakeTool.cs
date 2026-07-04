using Orkeon.Domain.Common;
using Orkeon.Domain.Tools;
using ToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;
using ToolCallResponse = Orkeon.Domain.Tools.Protocol.ToolCallResponse;
using ToolSchema = Orkeon.Domain.Tools.Protocol.ToolSchema;
using ParameterSchema = Orkeon.Domain.Tools.Protocol.ParameterSchema;

namespace Orkeon.Hosting.Tests.Doubles;

/// <summary>
/// Hand-rolled <see cref="ITool"/> double (CLAUDE.md convention — no mocking framework).
/// Only carries an identity (<see cref="Name"/>); used to exercise the name-based
/// filtering of <c>ServiceProviderToolRegistry.GetToolsAsync(IEnumerable&lt;ITool&gt;)</c>.
/// </summary>
public sealed class FakeTool : ITool
{
    /// <summary>Initialises a fake tool exposed under <paramref name="name"/>.</summary>
    public FakeTool(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        Schema = new ToolSchema(name, "fake tool", new Dictionary<string, ParameterSchema>());
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public string Description => "fake tool";

    /// <inheritdoc />
    public ToolSchema Schema { get; }

    /// <inheritdoc />
    public Task<ToolCallResponse> CallAsync(ToolCallRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(new ToolCallResponse(true, null, null));

    /// <inheritdoc />
    public Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default)
        => Task.FromResult(ToolResult.CreateSuccess(string.Empty));

    /// <inheritdoc />
    public bool ValidateInput(string input) => true;
}
