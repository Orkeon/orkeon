using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Cli.Commands.Scripting.DependencyInjection;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Scripting;
using Orkeon.Scripting.Runtime;

namespace Orkeon.Cli.Commands.Scripting.Tests.DependencyInjection;

/// <summary>R6, R22: the CLI host fills the instance caps and the tool limits of the script runtime from its configuration.</summary>
public sealed class ScriptingHostPortsWiringTests
{
    private static readonly TimeSpan Safety = TimeSpan.FromSeconds(30);

    private const string Act = "{ permissionMode: \"bypassPermissions\" }";

    private sealed class FakeChatLlmProvider(string toolCall = "") : ILlmProvider
    {
        private int _turn;
        public List<LlmMessage[]> Chats { get; } = new();

        public string Name => "fake-chat";
        public LlmConfig? BaseConfig => LlmConfig.Create("fake-chat-model");

        public Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new LlmResponse { Content = prompt });

        public Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default)
        {
            lock (Chats) Chats.Add(messages);
            if (toolCall.Length > 0)
            {
                if (_turn++ > 0) return Task.FromResult(new LlmResponse { Content = "final" });
                return Task.FromResult(new LlmResponse
                {
                    Content = "",
                    RawResponseBody = "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"\",\"tool_calls\":[{\"id\":\"c1\",\"type\":\"function\","
                        + "\"function\":{\"name\":\"" + toolCall + "\",\"arguments\":" + System.Text.Json.JsonSerializer.Serialize("{\"path\":\"p\"}") + "}}]}}]}",
                });
            }
            var last = messages.Last(m => m.Role == "user").Content;
            return Task.FromResult(new LlmResponse { Content = "R:" + last });
        }
    }

    private sealed class FakeReadTool(int resultChars) : IBaseTool
    {
        public string Name => "file_read";
        public ToolAccess Access => ToolAccess.Read;
        public string Description => "test tool";
        public ToolSchema Schema => new(Name, Description, new Dictionary<string, ParameterSchema>());

        public Task<ToolCallResponse> CallAsync(Orkeon.Domain.Tools.Protocol.ToolCallRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new ToolCallResponse(true, new Dictionary<string, object?> { ["path"] = new string('x', resultChars) }, null));

        public Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default)
            => Task.FromResult(ToolResult.CreateSuccess("ok"));

        public bool ValidateInput(string input) => true;
    }

    private static async Task RunCrewAsync(ServiceProvider provider, string js)
    {
        var engine = provider.GetRequiredService<JsEngineFactory>().Create();
        var crew = (JsCrew)(await engine.EvaluateAsync(js, null, TestContext.Current.CancellationToken)).ToObject()!;
        await crew.RunAsync(null, TestContext.Current.CancellationToken).WaitAsync(Safety, TestContext.Current.CancellationToken);
    }

    private static ServiceProvider Build(Dictionary<string, string?> settings, ILlmProvider llm, IBaseTool? tool = null)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddOptions<ToolResultLimitsOptions>().Bind(config.GetSection(ToolResultLimitsOptions.SectionName));
        services.AddSingleton(llm);
        if (tool is not null) services.AddSingleton(tool);
        services.AddScriptCommands(config);
        return services.BuildServiceProvider();
    }

    [Fact]
    [Trait("BR", "R6")]
    public async Task ShouldPassInstanceOptions_WhenCliHostBuildsScriptPorts()
    {
        var llm = new FakeChatLlmProvider();
        using var provider = Build(new() { ["Orkeon:Scripting:AgentInstances:MaxTranscriptMessages"] = "1" }, llm);

        await RunCrewAsync(provider,
            "const kid = agentBuilder().name(\"a\").role(\"R\").goal(\"G\")"
            + ".body(async (input, ctx) => (await ctx.llm.act(String(input), " + Act + ")).output).build();\n"
            + "const root = agentBuilder().name(\"root\").role(\"R\").goal(\"G\").body(async (input, ctx) => {"
            + " ctx.spawnBackground(\"a\", \"child-a\");"
            + " await ctx.llm.act(\"w\", { permissionMode: \"bypassPermissions\", waitFor: \"children\", maxIterations: 20 });"
            + " return await c.send(\"a1\", \"more\"); }).build();\n"
            + "const c = crewBuilder().name(\"c\").withAgent(root).withAgent(kid).build();\nc;");

        var resumed = llm.Chats.Single(c => c[^1].Role == "user" && c[^1].Content == "more");
        Assert.Equal(["assistant:R:child-a", "user:more"], resumed.Where(m => m.Role != "system").Select(m => $"{m.Role}:{m.Content}"));
    }

    [Fact]
    [Trait("BR", "R22")]
    public async Task ShouldPassToolLimits_WhenCliHostBuildsScriptPorts()
    {
        var llm = new FakeChatLlmProvider("file_read");
        using var provider = Build(new() { [ToolResultLimitsOptions.SectionName + ":Limits:file_read"] = "10" }, llm, new FakeReadTool(100));

        await RunCrewAsync(provider,
            "const a = agentBuilder().name(\"a\").role(\"R\").goal(\"G\").tools([\"file_read\"])"
            + ".body(async (input, ctx) => (await ctx.llm.act(\"go\", " + Act + ")).output).build();\n"
            + "crewBuilder().name(\"c\").withAgent(a).build();");

        var seen = Assert.Single(llm.Chats[1], m => m.Role == "tool").Content;
        Assert.Equal(ToolResultLimitsOptions.Cut("{\"path\":\"" + new string('x', 100) + "\"}", 10), seen);
    }
}
