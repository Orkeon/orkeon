using System.Text.RegularExpressions;
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

    /// <summary>
    /// Starts an expectation for calls with a parameter whose text contains <paramref name="contains"/>
    /// (case ignored). Complete it with <see cref="MockToolExpectationBuilder.Respond(object?)"/>; the
    /// first expectation that fits a call answers it.
    /// </summary>
    /// <param name="contains">The text to look for in the parameter values.</param>
    public MockToolExpectationBuilder When(string contains)
    {
        ArgumentNullException.ThrowIfNull(contains);
        return new MockToolExpectationBuilder(this, request => request.Parameters.Values.Any(
            v => v?.ToString()?.Contains(contains, StringComparison.OrdinalIgnoreCase) == true));
    }

    /// <summary>
    /// Starts an expectation for calls with a parameter whose text <paramref name="pattern"/> matches.
    /// Complete it with <see cref="MockToolExpectationBuilder.Respond(object?)"/>.
    /// </summary>
    /// <param name="pattern">The pattern the parameter values are tested against.</param>
    public MockToolExpectationBuilder When(Regex pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        return new MockToolExpectationBuilder(this, request => request.Parameters.Values.Any(
            v => v?.ToString() is { } text && pattern.IsMatch(text)));
    }

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

/// <summary>
/// Opens an expectation on a <see cref="MockTool"/> (<see cref="MockTool.When(string)"/>).
/// </summary>
public sealed class MockToolExpectationBuilder
{
    private readonly MockTool _tool;
    private readonly Func<ToolCallRequest, bool> _matcher;

    internal MockToolExpectationBuilder(MockTool tool, Func<ToolCallRequest, bool> matcher)
    {
        _tool = tool;
        _matcher = matcher;
    }

    /// <summary>Registers <paramref name="response"/> as what the tool returns for the matched calls.</summary>
    /// <param name="response">The value the tool answers with.</param>
    /// <returns>The tool, to chain another <c>When</c>.</returns>
    public MockTool Respond(object? response)
    {
        _tool.AddExpectation(_matcher, response);
        return _tool;
    }
}
