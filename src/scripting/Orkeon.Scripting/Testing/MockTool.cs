using Orkeon.Domain.Tools;
using ToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;
using ToolCallResponse = Orkeon.Domain.Tools.Protocol.ToolCallResponse;
using ToolSchema = Orkeon.Domain.Tools.Protocol.ToolSchema;
using ParameterSchema = Orkeon.Domain.Tools.Protocol.ParameterSchema;

namespace Orkeon.Scripting.Testing;

/// <summary>
/// In-memory <see cref="IBaseTool"/> for tests. Records every call and answers based
/// on the configured response.
/// </summary>
public sealed class MockTool : IBaseTool
{
    private readonly List<ToolCallRequest> _calls = new();

    /// <inheritdoc />
    public string Name { get; }
    /// <inheritdoc />
    public string Description { get; }
    /// <inheritdoc />
    public ToolSchema Schema { get; }

    /// <summary>Calls captured from <see cref="CallAsync"/>.</summary>
    public IReadOnlyList<ToolCallRequest> Calls => _calls;

    /// <summary>Configured default response.</summary>
    public object? Response { get; set; }

    /// <summary>Optional matcher → response overrides registered in order.</summary>
    private readonly List<(Func<ToolCallRequest, bool> Matcher, object? Response)> _expectations = new();

    /// <summary>Initialises a new mock tool with the given name.</summary>
    public MockTool(string name, string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        Description = description ?? $"mock tool '{name}'";
        Schema = new ToolSchema(Name, Description, new Dictionary<string, ParameterSchema>());
    }

    internal void AddExpectation(Func<ToolCallRequest, bool> matcher, object? response)
        => _expectations.Add((matcher, response));

    /// <inheritdoc />
    public Task<ToolCallResponse> CallAsync(ToolCallRequest request, CancellationToken cancellationToken = default)
    {
        _calls.Add(request);
        foreach (var (m, r) in _expectations)
            if (m(request)) return Task.FromResult(new ToolCallResponse(true, r, null));
        return Task.FromResult(new ToolCallResponse(true, Response, null));
    }

    /// <inheritdoc />
    public Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default)
        => Task.FromResult(ToolResult.CreateSuccess(Response?.ToString() ?? string.Empty));

    /// <inheritdoc />
    public bool ValidateInput(string input) => true;
}
