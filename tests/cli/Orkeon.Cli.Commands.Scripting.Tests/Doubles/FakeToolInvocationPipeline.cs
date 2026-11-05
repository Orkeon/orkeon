using Orkeon.Application.Interfaces.Security;

namespace Orkeon.Cli.Commands.Scripting.Tests.Doubles;

/// <summary><see cref="IToolInvocationPipeline"/> answering what it is configured to.</summary>
internal sealed class FakeToolInvocationPipeline : IToolInvocationPipeline
{
    public bool Success { get; set; } = true;

    public Action? OnInvoke { get; set; }

    public int Calls { get; private set; }

    public Task<ToolInvocationResult> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken = default)
    {
        Calls++;
        OnInvoke?.Invoke();
        return Task.FromResult(new ToolInvocationResult { ConversationText = "", RawText = "", Success = Success });
    }
}
