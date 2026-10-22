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

    private readonly IReadOnlyList<ScriptStep>? _script;

    /// <summary>
    /// Plays one step per chat turn, in call order across every agent sharing this instance
    /// (nested runs consume the script depth-first). A turn past the script answers "script exhausted".
    /// </summary>
    public FakeToolCallingLlmProvider(params ScriptStep[] script)
    {
        _toolName = "";
        _argumentsJson = "";
        _script = script;
    }

    /// <summary>One scripted turn: a tool call, or a final text answer.</summary>
    public sealed record ScriptStep(string? ToolName, string? ArgumentsJson, string? Text)
    {
        public static ScriptStep Call(string toolName, string argumentsJson) => new(toolName, argumentsJson, null);
        public static ScriptStep Answer(string text) => new(null, null, text);
    }

    /// <summary>The content of every tool-result message received, in order.</summary>
    public List<string> ToolResults { get; } = new();

    private static LlmResponse ToolCall(string toolName, string argumentsJson)
    {
        var args = System.Text.Json.JsonSerializer.Serialize(argumentsJson);
        return new LlmResponse
        {
            Content = "",
            RawResponseBody = "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"\",\"tool_calls\":[" +
                "{\"id\":\"c1\",\"type\":\"function\",\"function\":{\"name\":\"" + toolName + "\",\"arguments\":" + args + "}}]}}]}",
        };
    }

    public string Name => "fake-tools";
    public LlmConfig? BaseConfig => LlmConfig.Create("fake-tools-model");

    /// <summary>The messages of each chat turn, in order.</summary>
    public List<LlmMessage[]> Turns { get; } = new();

    /// <summary>The tool names offered with each chat request, in order.</summary>
    public List<IReadOnlyList<string>> OfferedToolsPerTurn { get; } = new();

    /// <summary>The thinking setting (enabled, effort) seen on each chat request, in order.</summary>
    public List<(bool? Enabled, string? Effort)> ThinkingPerTurn { get; } = new();

    /// <summary>The tool names the first turn offered the model.</summary>
    public IReadOnlyList<string> OfferedTools { get; private set; } = [];

    public Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default)
        => Task.FromResult(new LlmResponse { Content = prompt });

    public async Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default)
    {
        await Task.Delay(3, cancellationToken).ConfigureAwait(false);
        Turns.Add(messages);
        OfferedToolsPerTurn.Add(config?.Tools?.Select(t => t.Name).ToList() ?? []);
        ThinkingPerTurn.Add((config?.Thinking?.Enabled, config?.Thinking?.Effort));
        if (messages.Length > 0 && messages[^1].Role == "tool") ToolResults.Add(messages[^1].Content);
        if (_script is not null)
        {
            var step = _turn < _script.Count ? _script[_turn] : ScriptStep.Answer("script exhausted");
            _turn++;
            return step.ToolName is null ? new LlmResponse { Content = step.Text! } : ToolCall(step.ToolName, step.ArgumentsJson!);
        }
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
