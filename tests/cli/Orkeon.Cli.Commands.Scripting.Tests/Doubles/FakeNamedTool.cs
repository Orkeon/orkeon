using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;

namespace Orkeon.Cli.Commands.Scripting.Tests.Doubles;

/// <summary>A tool that holds the name it is given and does nothing.</summary>
internal sealed class FakeNamedTool(string name) : IBaseTool
{
    public string Name => name;

    public string Description => "Does nothing (hand-written test double).";

    public ToolSchema Schema => new(Name, Description, new Dictionary<string, ParameterSchema>());

    public Task<ToolCallResponse> CallAsync(Orkeon.Domain.Tools.Protocol.ToolCallRequest request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default)
        => Task.FromResult(ToolResult.CreateSuccess(""));

    public bool ValidateInput(string input) => true;
}
