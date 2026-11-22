using System.Collections.Concurrent;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Host.Tests.Doubles;

/// <summary>
/// A model that calls <see cref="ToolName"/> on its first <see cref="Calls"/> chat turns (an OpenAI-shaped
/// <c>tool_calls</c> body), then answers <see cref="Answer"/> to every later call. Keeps every chat turn it receives.
/// </summary>
internal sealed class FakeToolCallingLlmProvider : ILlmProvider
{
    private int _turn;

    /// <summary>The tool every scripted turn calls.</summary>
    public required string ToolName { get; init; }

    /// <summary>How many turns call the tool before the model answers.</summary>
    public int Calls { get; init; } = 3;

    /// <summary>What the model answers once it stops calling.</summary>
    public string Answer { get; init; } = "done";

    /// <summary>The chat turns received, oldest first.</summary>
    public ConcurrentQueue<LlmMessage[]> Turns { get; } = new();

    public string Name => "fake-tools";

    public LlmConfig? BaseConfig => LlmConfig.Create("fake-tools-model");

    public Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default)
        => Task.FromResult(new LlmResponse { Content = Answer });

    public Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default)
    {
        Turns.Enqueue(messages);
        var turn = Interlocked.Increment(ref _turn);
        if (turn > Calls)
            return Task.FromResult(new LlmResponse { Content = Answer });
        var arguments = System.Text.Json.JsonSerializer.Serialize("{\"path\":\"/" + turn + "\"}");
        return Task.FromResult(new LlmResponse
        {
            Content = "",
            RawResponseBody = "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"\",\"tool_calls\":[" +
                "{\"id\":\"c" + turn + "\",\"type\":\"function\",\"function\":{\"name\":\"" + ToolName + "\",\"arguments\":" + arguments + "}}]}}]}",
        });
    }
}
