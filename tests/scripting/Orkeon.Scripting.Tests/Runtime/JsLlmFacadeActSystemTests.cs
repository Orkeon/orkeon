using Jint;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Scripting.Runtime;
using ProtocolToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;

namespace Orkeon.Scripting.Tests.Runtime;

/// <summary>
/// The <c>system</c> act option: seeds a system message as the FIRST message of the
/// tool-call conversation. Until this option existed, a scripted agent could not have a
/// real system prompt at all — <c>act()</c> always sent a single user message, so identity
/// and tool policy travelled with user-level authority (and the providers' native system
/// handling never fired).
/// </summary>
public sealed class JsLlmFacadeActSystemTests
{
    // Non-async on purpose: evaluating a literal object is instantaneous, and calling
    // Engine.Evaluate from the async test bodies trips CA1849.
    private static Jint.Native.JsValue EvalOptions(Engine engine, string js) => engine.Evaluate(js);

    private static string ToolCallBody(string name, string argsJson)
    {
        var argsEncoded = System.Text.Json.JsonSerializer.Serialize(argsJson);
        return "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"\",\"tool_calls\":[" +
               "{\"id\":\"c1\",\"type\":\"function\",\"function\":{\"name\":\"" + name + "\",\"arguments\":" + argsEncoded + "}}]}}]}";
    }

    [Fact]
    public async Task System_option_seeds_a_system_first_message()
    {
        using var engine = new Engine();
        var provider = new MessageCapturingProvider(new[]
        {
            new LlmResponse { Content = "done", RawResponseBody = null },
        });
        var facade = new JsLlmFacade(engine, provider, CancellationToken.None, Array.Empty<IBaseTool>());

        var options = EvalOptions(engine, "({ system: \"You are the coding agent.\" })");
        var result = await facade.act("hello", options);

        Assert.Equal("done", result.Get("output").AsString());
        var turn1 = provider.MessagesPerCall[0];
        Assert.Equal(2, turn1.Length);
        Assert.Equal("system", turn1[0].Role);
        Assert.Equal("You are the coding agent.", turn1[0].Content);
        Assert.Equal("user", turn1[1].Role);
        Assert.Equal("hello", turn1[1].Content);
    }

    [Fact]
    public async Task Without_system_the_conversation_is_a_single_user_message()
    {
        // Non-regression pin: the historical shape must stay byte-identical when the
        // option is absent — every existing script relies on it.
        using var engine = new Engine();
        var provider = new MessageCapturingProvider(new[]
        {
            new LlmResponse { Content = "done", RawResponseBody = null },
        });
        var facade = new JsLlmFacade(engine, provider, CancellationToken.None, Array.Empty<IBaseTool>());

        await facade.act("hello", null);

        var turn1 = provider.MessagesPerCall[0];
        Assert.Single(turn1);
        Assert.Equal("user", turn1[0].Role);
        Assert.Equal("hello", turn1[0].Content);
    }

    [Fact]
    public async Task Blank_system_is_treated_as_absent()
    {
        using var engine = new Engine();
        var provider = new MessageCapturingProvider(new[]
        {
            new LlmResponse { Content = "done", RawResponseBody = null },
        });
        var facade = new JsLlmFacade(engine, provider, CancellationToken.None, Array.Empty<IBaseTool>());

        var options = EvalOptions(engine, "({ system: \"   \" })");
        await facade.act("hello", options);

        Assert.Single(provider.MessagesPerCall[0]);
        Assert.Equal("user", provider.MessagesPerCall[0][0].Role);
    }

    [Fact]
    public async Task System_message_persists_across_tool_call_iterations()
    {
        // The system prompt is part of the conversation list, so every later turn of the
        // loop re-sends it in position 0 — the model never loses its instructions mid-loop.
        using var engine = new Engine();
        var tool = new EchoTool("probe_tool");
        var provider = new MessageCapturingProvider(new[]
        {
            new LlmResponse { Content = "", RawResponseBody = ToolCallBody("probe_tool", "{}") },
            new LlmResponse { Content = "final", RawResponseBody = null },
        });
        var facade = new JsLlmFacade(engine, provider, CancellationToken.None, new IBaseTool[] { tool });

        var options = EvalOptions(engine, "({ system: \"stay on task\" })");
        var result = await facade.act("go", options);

        Assert.Equal("final", result.Get("output").AsString());
        Assert.Equal(2, provider.MessagesPerCall.Count);
        var turn2 = provider.MessagesPerCall[1];
        Assert.Equal("system", turn2[0].Role);
        Assert.Equal("stay on task", turn2[0].Content);
        // The tool result still comes back as a user turn after the seed pair.
        Assert.Contains(turn2, m => m.Role == "user" && m.Content.Contains("[tool:probe_tool]", StringComparison.Ordinal));
    }

    // ── fakes ────────────────────────────────────────────────────────────────

    private sealed class MessageCapturingProvider : ILlmProvider
    {
        private readonly LlmResponse[] _responses;
        private int _i;
        public List<LlmMessage[]> MessagesPerCall { get; } = new();

        public MessageCapturingProvider(LlmResponse[] responses) => _responses = responses;

        public string Name => "fake";
        public LlmConfig? BaseConfig => LlmConfig.Default() with { Model = "fake-model" };

        public Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken ct = default)
            => Task.FromResult(_responses[Math.Min(_i++, _responses.Length - 1)]);

        public Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken ct = default)
        {
            MessagesPerCall.Add(messages);
            return Task.FromResult(_responses[Math.Min(_i++, _responses.Length - 1)]);
        }
    }

    private sealed class EchoTool : IBaseTool
    {
        public EchoTool(string name) => Name = name;

        public string Name { get; }
        public ToolAccess Access => ToolAccess.Read;
        public string Description => "test tool";
        public ToolSchema Schema => new(Name, Description, new Dictionary<string, ParameterSchema>());

        public Task<ToolCallResponse> CallAsync(ProtocolToolCallRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new ToolCallResponse(true, new Dictionary<string, object?> { ["ok"] = true }, null));

        public Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default)
            => Task.FromResult(ToolResult.CreateSuccess("ok"));

        public bool ValidateInput(string input) => true;
    }
}
