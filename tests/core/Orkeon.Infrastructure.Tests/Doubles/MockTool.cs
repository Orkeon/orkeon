using Orkeon.Domain.Common;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;
using ProtocolRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Manual mock for ITool with configurable Name and Description.
/// </summary>
public class MockTool : ITool
{
    public MockTool(string name = "mock_tool", string description = "A mock tool")
    {
        Name = name;
        Description = description;
    }

    public string Name { get; set; }
    public string Description { get; set; }

    public ToolSchema Schema => new(
        Name,
        Description,
        []);

    public Task<ToolCallResponse> CallAsync(ProtocolRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ToolCallResponse(
            Success: true,
            Result: new Dictionary<string, object> { ["output"] = "mock result" },
            Error: null));
    }

    public Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(ToolResult.CreateSuccess("mock result"));
    }

    public bool ValidateInput(string input) => true;
}
