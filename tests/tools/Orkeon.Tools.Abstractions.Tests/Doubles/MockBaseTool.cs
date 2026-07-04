using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;
using ToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;

namespace Orkeon.Tools.Abstractions.Tests.Doubles;

/// <summary>
/// Manual mock for IBaseTool with call tracking and configurable results.
/// </summary>
public class MockBaseTool : IBaseTool
{
    private ToolCallResponse _callResult = new(true, "mock result", null);
    private Func<ToolCallRequest, CancellationToken, Task<ToolCallResponse>>? _callFunc;
    private ToolResult _executeResult = ToolResult.CreateSuccess("mock output");
    private bool _validateResult = true;

    // --- Tracking ---
    public int CallAsyncCallCount { get; private set; }
    public ToolCallRequest? LastCallRequest { get; private set; }
    public List<ToolCallRequest> AllCallRequests { get; } = [];

    public int ExecuteAsyncCallCount { get; private set; }
    public string? LastExecuteInput { get; private set; }
    public List<string> AllExecuteInputs { get; } = [];

    public int ValidateInputCallCount { get; private set; }
    public string? LastValidateInput { get; private set; }

    // --- Configuration ---
    public string Name { get; set; } = "mock_tool";
    public string Description { get; set; } = "A mock tool for testing";

    public ToolSchema Schema { get; set; } = new(
        "mock_tool",
        "A mock tool for testing",
        new Dictionary<string, ParameterSchema>
        {
            ["input"] = new("string", "The input", true)
        });

    public void SetCallResult(ToolCallResponse result) => _callResult = result;

    public void SetCallSuccess(object? result) =>
        _callResult = new ToolCallResponse(true, result, null);

    public void SetCallError(string error) =>
        _callResult = new ToolCallResponse(false, null, error);

    public void SetExecuteResult(ToolResult result) => _executeResult = result;

    public void SetExecuteSuccess(string output) =>
        _executeResult = ToolResult.CreateSuccess(output);

    public void SetExecuteError(string error) =>
        _executeResult = ToolResult.CreateError(error);

    public void SetValidateResult(bool result) => _validateResult = result;

    /// <summary>
    /// Sets a function to dynamically handle CallAsync invocations.
    /// </summary>
    public void SetCallFunc(Func<ToolCallRequest, CancellationToken, Task<ToolCallResponse>> func) =>
        _callFunc = func;

    // --- IBaseTool ---
    public async Task<ToolCallResponse> CallAsync(ToolCallRequest request, CancellationToken cancellationToken = default)
    {
        CallAsyncCallCount++;
        LastCallRequest = request;
        AllCallRequests.Add(request);
        if (_callFunc != null)
            return await _callFunc(request, cancellationToken);
        return _callResult;
    }

    public Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default)
    {
        ExecuteAsyncCallCount++;
        LastExecuteInput = input;
        AllExecuteInputs.Add(input);
        return Task.FromResult(_executeResult);
    }

    public bool ValidateInput(string input)
    {
        ValidateInputCallCount++;
        LastValidateInput = input;
        return _validateResult;
    }
}
