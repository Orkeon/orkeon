using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;
using ToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;

namespace Orkeon.Interop.AgentCli.Tests.Doubles;

/// <summary>A permission gate answering one verdict and counting the calls put to it.</summary>
internal sealed class FakePermissionGate : IPermissionGate
{
    public PermissionVerdict Verdict { get; init; } = PermissionVerdict.Allow();

    public List<string> Tools { get; } = [];

    public Task<PermissionVerdict> CheckAsync(
        string toolName, IReadOnlyDictionary<string, object?> arguments, string mode,
        ToolAccess declaredAccess = ToolAccess.Unspecified, CancellationToken cancellationToken = default)
    {
        lock (Tools)
            Tools.Add(toolName);
        return Task.FromResult(Verdict);
    }
}

/// <summary>A tool named <c>file_write</c> that counts the times it ran.</summary>
internal sealed class FakeWriteTool : IBaseTool
{
    public string Name => "file_write";

    public string Description => "Writes a file";

    public ToolSchema Schema => new(Name, Description, new Dictionary<string, ParameterSchema>());

    public int CallCount { get; private set; }

    public Task<ToolCallResponse> CallAsync(ToolCallRequest request, CancellationToken cancellationToken = default)
    {
        CallCount++;
        return Task.FromResult(new ToolCallResponse(true, "written", null));
    }

    public Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default)
    {
        CallCount++;
        return Task.FromResult(ToolResult.CreateSuccess("written"));
    }

    public bool ValidateInput(string input) => true;
}

/// <summary>The plain-text provider the chat loop's call gate holds; the loop under test never reaches it.</summary>
internal sealed class StubBasicLlmProvider : IBasicLlmProvider
{
    public string Name => "stub-basic";

    public Task<string> ChatAsync(string message, LlmConfig? config = null, CancellationToken cancellationToken = default) =>
        Task.FromResult("unused");

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
}
