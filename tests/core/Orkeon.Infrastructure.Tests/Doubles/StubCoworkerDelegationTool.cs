using Orkeon.Domain.Tools;
using ToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;
using ToolCallResponse = Orkeon.Domain.Tools.Protocol.ToolCallResponse;
using ToolSchema = Orkeon.Domain.Tools.Protocol.ToolSchema;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>A coworker delegation tool (<see cref="ICoworkerDelegationTool"/>) that answers nothing of note.</summary>
internal sealed class StubCoworkerDelegationTool(string name = CoworkerDelegation.DelegateWorkToolName) : ICoworkerDelegationTool
{
    public string Name => name;

    public string Description => name;

    public ToolSchema Schema => new(name, name, []);

    public System.Threading.Tasks.Task<ToolCallResponse> CallAsync(ToolCallRequest request, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult(new ToolCallResponse(true, "delegated", null));

    public System.Threading.Tasks.Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public bool ValidateInput(string input) => true;
}
