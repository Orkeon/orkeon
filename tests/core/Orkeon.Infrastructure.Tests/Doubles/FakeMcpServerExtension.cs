using System.Text.Json;
using Orkeon.Infrastructure.MCP;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// An extension with one tool and one prompt. The tool reports <see cref="ProgressSteps"/> steps, then waits for
/// <see cref="Finish"/> when <see cref="WaitForFinish"/> is set; it records what it was called with.
/// </summary>
public sealed class FakeMcpServerExtension : IMcpServerExtension
{
    private readonly TaskCompletionSource _finish = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>How many steps the tool reports before it answers.</summary>
    public int ProgressSteps { get; set; }

    /// <summary>Whether the tool waits for <see cref="Finish"/> after its steps.</summary>
    public bool WaitForFinish { get; set; }

    /// <summary>The prompts served; one by default.</summary>
    public List<McpPrompt> PromptList { get; } = [new McpPrompt { Name = "greet", Description = "Greets" }];

    /// <summary>The arguments of the last tool call; null until a call started.</summary>
    public JsonElement? LastArguments { get; private set; }

    /// <summary>When set, a tool call throws it, as a crew whose host failed would.</summary>
    public Exception? ThrowOnCall { get; set; }

    /// <summary>Whether a call saw its token cancelled.</summary>
    public bool SawCancellation { get; private set; }

    /// <inheritdoc />
    public IReadOnlyList<McpToolDefinition> Tools { get; } = [new McpToolDefinition { Name = "run_crew_demo", Description = "Runs demo" }];

    /// <inheritdoc />
    public IReadOnlyList<McpPrompt> Prompts => PromptList;

    /// <summary>Lets a waiting tool call answer.</summary>
    public void Finish() => _finish.TrySetResult();

    /// <inheritdoc />
    public bool OwnsTool(string name) => name == "run_crew_demo";

    /// <summary>Whether the server said its input ended.</summary>
    public bool StoppedAdmitting { get; private set; }

    /// <inheritdoc />
    public void StopAdmitting() => StoppedAdmitting = true;

    /// <inheritdoc />
    public async Task<McpToolCallResult> CallToolAsync(string name, JsonElement? arguments, IMcpProgress progress, CancellationToken ct)
    {
        LastArguments = arguments ?? JsonDocument.Parse("{}").RootElement;
        if (ThrowOnCall is { } failure)
            throw failure;
        for (var i = 1; i <= ProgressSteps; i++)
            progress.Report(i, ProgressSteps, $"step {i}");
        if (WaitForFinish)
        {
            try
            {
                await _finish.Task.WaitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                SawCancellation = true;
                throw;
            }
        }

        return new McpToolCallResult { Content = [new McpContent { Text = "final output" }] };
    }

    /// <inheritdoc />
    public Task<McpPromptGetResult> GetPromptAsync(string name, JsonElement? arguments, CancellationToken ct)
    {
        if (name != "greet")
            throw new McpRequestException(JsonRpcErrorCodes.InvalidParams, $"Prompt not found: {name}");
        return Task.FromResult(new McpPromptGetResult
        {
            Messages = [new McpPromptMessage { Content = new McpContent { Text = "hello" } }],
        });
    }
}
