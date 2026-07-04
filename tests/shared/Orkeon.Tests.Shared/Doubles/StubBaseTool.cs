using Orkeon.Domain.Tools;
using ToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;
using ToolCallResponse = Orkeon.Domain.Tools.Protocol.ToolCallResponse;
using ToolSchema = Orkeon.Domain.Tools.Protocol.ToolSchema;
using ParameterSchema = Orkeon.Domain.Tools.Protocol.ParameterSchema;

namespace Orkeon.Tests.Shared.Doubles;

/// <summary>
/// Hand-rolled <see cref="IBaseTool"/> stub used by tests that previously relied on
/// <c>Mock&lt;IBaseTool&gt;</c>. Captures every <see cref="CallAsync"/> request and
/// returns a configurable response.
/// </summary>
public sealed class StubBaseTool : IBaseTool
{
    private Func<ToolCallRequest, ToolCallResponse> _responder =
        req => new ToolCallResponse(true, null, null);

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public string Description { get; init; } = "stub tool";

    /// <inheritdoc />
    public ToolSchema Schema { get; init; }

    /// <summary>Captured requests from <see cref="CallAsync"/>.</summary>
    public List<ToolCallRequest> Calls { get; } = new();

    /// <summary>Initialises a stub tool exposed under <paramref name="name"/>.</summary>
    public StubBaseTool(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        Schema = new ToolSchema(name, "stub tool", new Dictionary<string, ParameterSchema>());
    }

    /// <summary>Configures the function used to build a response from a request.</summary>
    public StubBaseTool RespondWith(Func<ToolCallRequest, ToolCallResponse> responder)
    {
        ArgumentNullException.ThrowIfNull(responder);
        _responder = responder;
        return this;
    }

    /// <summary>Configures a constant successful response.</summary>
    public StubBaseTool RespondWithSuccess(object? result)
    {
        _responder = _ => new ToolCallResponse(true, result, null);
        return this;
    }

    /// <summary>Configures a constant failed response.</summary>
    public StubBaseTool RespondWithError(string error)
    {
        _responder = _ => new ToolCallResponse(false, null, error);
        return this;
    }

    /// <inheritdoc />
    public Task<ToolCallResponse> CallAsync(ToolCallRequest request, CancellationToken cancellationToken = default)
    {
        Calls.Add(request);
        return Task.FromResult(_responder(request));
    }

    /// <inheritdoc />
    public Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default)
        => Task.FromResult(ToolResult.CreateSuccess(string.Empty));

    /// <inheritdoc />
    public bool ValidateInput(string input) => true;
}
