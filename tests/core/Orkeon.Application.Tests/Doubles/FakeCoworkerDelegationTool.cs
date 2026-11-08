using Orkeon.Domain.Tools;
using ToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;
using ToolCallResponse = Orkeon.Domain.Tools.Protocol.ToolCallResponse;
using ToolSchema = Orkeon.Domain.Tools.Protocol.ToolSchema;

namespace Orkeon.Application.Tests.Doubles;

/// <summary>
/// A coworker delegation tool (<see cref="ICoworkerDelegationTool"/>) named like the built-in ones; what a
/// call does to the run around it (a coworker's own calls) is <see cref="OnCall"/>.
/// </summary>
internal sealed class FakeCoworkerDelegationTool : ICoworkerDelegationTool
{
    public FakeCoworkerDelegationTool(string name = CoworkerDelegation.DelegateWorkToolName, Func<CancellationToken, System.Threading.Tasks.Task>? onCall = null)
    {
        Name = name;
        OnCall = onCall;
    }

    public string Name { get; }

    public string Description => Name;

    public ToolSchema Schema => new(Name, Name, []);

    public Func<CancellationToken, System.Threading.Tasks.Task>? OnCall { get; }

    public int Calls { get; private set; }

    public async System.Threading.Tasks.Task<ToolCallResponse> CallAsync(ToolCallRequest request, CancellationToken cancellationToken = default)
    {
        Calls++;
        if (OnCall is not null)
            await OnCall(cancellationToken);
        return new ToolCallResponse(true, "delegated", null);
    }

    public System.Threading.Tasks.Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public bool ValidateInput(string input) => true;
}

/// <summary>A tool that wraps another (<see cref="IToolWrapper"/>) and forwards its calls, as an observer does.</summary>
internal sealed class FakeWrappingTool(IBaseTool inner) : IBaseTool, IToolWrapper
{
    public IBaseTool Inner => inner;

    public string Name => inner.Name;

    public string Description => inner.Description;

    public ToolSchema Schema => inner.Schema;

    public System.Threading.Tasks.Task<ToolCallResponse> CallAsync(ToolCallRequest request, CancellationToken cancellationToken = default)
        => inner.CallAsync(request, cancellationToken);

    public System.Threading.Tasks.Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default)
        => inner.ExecuteAsync(input, cancellationToken);

    public bool ValidateInput(string input) => inner.ValidateInput(input);
}
