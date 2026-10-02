using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Scripting.Tests.Doubles;

/// <summary>
/// A provider whose first chat turn asks for one tool call and whose second answers with the
/// text the tool fed back — the minimal shape of an <c>act</c> loop. It suspends before
/// answering (<c>Task.Delay</c>), so the loop resumes on a thread-pool continuation as it does
/// against a real HTTP provider. Records every conversation it was handed.
/// </summary>
internal sealed class FakeToolCallingLlmProvider : ILlmProvider
{
    private readonly string _toolName;
    private readonly string _argumentsJson;
    private int _turn;

    public FakeToolCallingLlmProvider(string toolName, string argumentsJson)
    {
        _toolName = toolName;
        _argumentsJson = argumentsJson;
    }

    public string Name => "fake-tools";
    public LlmConfig? BaseConfig => LlmConfig.Create("fake-tools-model");

    /// <summary>The messages of each chat turn, in order.</summary>
    public List<LlmMessage[]> Turns { get; } = new();

    /// <summary>The tool names the first turn offered the model.</summary>
    public IReadOnlyList<string> OfferedTools { get; private set; } = [];

    public Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default)
        => Task.FromResult(new LlmResponse { Content = prompt });

    public async Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default)
    {
        await Task.Delay(3, cancellationToken).ConfigureAwait(false);
        Turns.Add(messages);
        if (_turn++ == 0)
        {
            OfferedTools = config?.Tools?.Select(t => t.Name).ToList() ?? [];
            var args = System.Text.Json.JsonSerializer.Serialize(_argumentsJson);
            return new LlmResponse
            {
                Content = "",
                RawResponseBody = "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"\",\"tool_calls\":[" +
                    "{\"id\":\"c1\",\"type\":\"function\",\"function\":{\"name\":\"" + _toolName + "\",\"arguments\":" + args + "}}]}}]}",
            };
        }
        return new LlmResponse { Content = "final: " + messages[^1].Content };
    }
}
