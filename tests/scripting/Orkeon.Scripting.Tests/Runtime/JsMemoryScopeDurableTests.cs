using Orkeon.Scripting.Runtime;
using Orkeon.Scripting.Tests.Doubles;

namespace Orkeon.Scripting.Tests.Runtime;

/// <summary>RT-16: ctx.memory.agent is durable through the host port, per agent id and project.</summary>
public sealed class JsMemoryScopeDurableTests
{
    private static readonly TimeSpan Safety = TimeSpan.FromSeconds(30);

    private static string Script(string name, string body)
        => "const me = agentBuilder().name(\"" + name + "\").role(\"R\").goal(\"G\")"
            + ".body(async (input, ctx) => {" + body + "}).build();\n"
            + "report(me.id);\n"
            + "const c = crewBuilder().name(\"c\").withAgent(me).build();\nc;";

    private static async Task<(string Output, string AgentId)> RunAsync(FakeScriptAgentMemoryStore store, string name, string body)
    {
        var engine = new JsEngineFactory(hostPorts: new ScriptingHostPorts { AgentMemoryStore = store }).Create();
        var id = "";
        engine.SetValue("report", new Action<string>(v => id = v));
        var crew = (JsCrew)(await engine.EvaluateAsync(Script(name, body), null, TestContext.Current.CancellationToken)).ToObject()!;
        var result = await crew.RunAsync(null, TestContext.Current.CancellationToken);
        return (result.tasks[0].output!.ToString()!, id);
    }

    [Fact]
    [Trait("BR", "RT-16")]
    public async Task ShouldReturnStoredValueInNextRun_WhenSameAgentAndProject()
    {
        var store = new FakeScriptAgentMemoryStore();
        await RunAsync(store, "a", "await ctx.memory.agent.store(\"k\", {n:1}); return \"stored\";");

        var (output, _) = await RunAsync(store, "a",
            "const v = await ctx.memory.agent.get(\"k\");"
            + "const hits = await ctx.memory.agent.search(\"k\", 5);"
            + "const del = await ctx.memory.agent.delete(\"k\");"
            + "const after = await ctx.memory.agent.get(\"k\");"
            + "return JSON.stringify({ n: v.n, hits: hits.length, hitN: hits[0].value.n, del: del, gone: after == null });");

        Assert.Equal("{\"n\":1,\"hits\":1,\"hitN\":1,\"del\":true,\"gone\":true}", output);
    }

    [Fact]
    [Trait("BR", "RT-16")]
    public async Task ShouldNotLeakToOtherAgent_WhenAgentNameDiffers()
    {
        var store = new FakeScriptAgentMemoryStore();
        var (_, idA) = await RunAsync(store, "alpha", "await ctx.memory.agent.store(\"k\", {n:1}); return \"stored\";");

        var (output, idB) = await RunAsync(store, "beta",
            "const v = await ctx.memory.agent.get(\"k\"); await ctx.memory.agent.store(\"other\", 2);"
            + "return JSON.stringify({ seen: v != null });");

        Assert.Equal("{\"seen\":false}", output);
        var keys = store.AgentKeysSeen.Distinct().ToList();
        Assert.Equal(2, keys.Count);
        Assert.All(keys, k => Assert.StartsWith("agent:", k, StringComparison.Ordinal));
        Assert.Single(keys, k => k.EndsWith(":alpha", StringComparison.Ordinal));
        Assert.Single(keys, k => k.EndsWith(":beta", StringComparison.Ordinal));
        Assert.DoesNotContain(keys, k => k.EndsWith(":" + idA, StringComparison.Ordinal) || k.EndsWith(":" + idB, StringComparison.Ordinal));
    }
}
