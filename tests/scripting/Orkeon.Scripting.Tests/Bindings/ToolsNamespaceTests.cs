using System.Text.Json;
using Jint;
using Orkeon.Scripting.Bindings;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Scripting.Tests.Bindings;

public sealed class ToolsNamespaceTests
{
    /// <summary>
    /// Regression: <see cref="JsonSerializer.Deserialize{T}"/> with target
    /// <c>Dictionary&lt;string, object?&gt;</c> populates values as
    /// <see cref="JsonElement"/> structs whose backing buffer is pooled.
    /// Calling <c>JsValue.FromObject</c> on such a dict used to throw
    /// <see cref="ArgumentException"/> ("Offset and length out of bounds")
    /// — which killed any script invoking <c>tools.indexCodebase</c>.
    /// The binding must unwrap JsonElement values before handing them to Jint.
    /// </summary>
    [Fact]
    public async Task Tools_invocation_unwraps_JsonElement_in_typed_response_dict()
    {
        // Simulate what ToolBase<TReq,TRes>.ExecuteCoreAsync returns: a dict
        // that came back from JsonSerializer.Deserialize<Dictionary<string, object?>>,
        // i.e. values are JsonElement instances.
        var json = """
          {
            "index_id": "abc123",
            "node_count": 42,
            "elapsed": "00:00:01.5",
            "errors": ["e1", "e2"],
            "embedding_stats": { "provider": "OpenAI", "dimensions": 1536 }
          }
          """;
        var elementBackedDict = JsonSerializer.Deserialize<Dictionary<string, object?>>(json)!;
        // Sanity: confirm the dict is indeed JsonElement-laden (otherwise the test
        // would not be reproducing the original failure mode).
        Assert.IsType<JsonElement>(elementBackedDict["index_id"]);

        var tool = new StubBaseTool("index_codebase").RespondWithSuccess(elementBackedDict);
        var engine = new JsEngineFactory(builtInTools: [(Orkeon.Domain.Tools.IBaseTool)tool]).Create();

        var result = await Task.Run(() => engine.Evaluate(
            "tools.indexCodebase({ root_path: '/src' }).then(r => r.index_id + ':' + r.node_count)")
            .UnwrapIfPromise());

        Assert.Equal("abc123:42", result.AsString());
    }


    /// <summary>
    /// Regression (#247): Jint's memory limit reads the engine thread's allocation counter, and a tool
    /// whose async method completes synchronously (SQLite, an in-memory index build) used to run whole on
    /// that thread, so its allocations were charged to the script. The call now starts off the engine
    /// thread: a tool, however heavy, does not count against the script's ceiling.
    /// </summary>
    [Fact]
    public async Task Tools_invocation_does_not_charge_the_tools_allocations_to_the_script_memory_limit()
    {
        var tool = new StubBaseTool("index_codebase").RespondWith(_ =>
        {
            var chunks = new List<byte[]>();
            for (var i = 0; i < 64; i++)
                chunks.Add(new byte[1024 * 1024]);
            return new ToolCallResponse(true, chunks.Count, null);
        });
        var limits = new Orkeon.Scripting.Configuration.ScriptingLimitsOptions { MemoryLimitBytes = 16L * 1024 * 1024 };
        var engine = new JsEngineFactory(limits, builtInTools: [(Orkeon.Domain.Tools.IBaseTool)tool]).Create();

        var result = await Task.Run(() => engine.Evaluate(
            "tools.indexCodebase({ root_path: '/src' }).then(n => 'chunks:' + n)")
            .UnwrapIfPromise(TestContext.Current.CancellationToken))
            .WaitAsync(HangGuards.EnginePromise, TestContext.Current.CancellationToken);

        Assert.Equal("chunks:64", result.AsString());
    }

    [Fact]
    public async Task Tools_invocation_still_charges_the_scripts_own_allocations_after_a_tool_call()
    {
        var tool = new StubBaseTool("index_codebase").RespondWith(_ =>
        {
            var chunks = new List<byte[]>();
            for (var i = 0; i < 64; i++)
                chunks.Add(new byte[1024 * 1024]);
            return new ToolCallResponse(true, chunks.Count, null);
        });
        var limits = new Orkeon.Scripting.Configuration.ScriptingLimitsOptions { MemoryLimitBytes = 16L * 1024 * 1024 };
        var engine = new JsEngineFactory(limits, builtInTools: [(Orkeon.Domain.Tools.IBaseTool)tool]).Create();

        var thrown = await Assert.ThrowsAnyAsync<Exception>(() => Task.Run(() => engine.Evaluate("""
            tools.indexCodebase({ root_path: '/src' }).then(() => {
                var arr = []; for (var i = 0; i < 1000000; i++) { arr.push('x'.repeat(1000)); }
                return arr.length;
            })
            """).UnwrapIfPromise(TestContext.Current.CancellationToken))
            .WaitAsync(HangGuards.EnginePromise, TestContext.Current.CancellationToken));

        Assert.Contains("is limited to 16777216", thrown.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ToCamelCase_converts_snake_and_kebab_to_camel()
    {
        Assert.Equal("fileRead", ToolsNamespaceBinding.ToCamelCase("file_read"));
        Assert.Equal("webScrape", ToolsNamespaceBinding.ToCamelCase("web_scrape"));
        Assert.Equal("databaseQuery", ToolsNamespaceBinding.ToCamelCase("database_query"));
        Assert.Equal("kebabCaseName", ToolsNamespaceBinding.ToCamelCase("kebab-case-name"));
        Assert.Equal("alreadyCamel", ToolsNamespaceBinding.ToCamelCase("alreadyCamel"));
    }

    [Fact]
    public void Tools_namespace_exposes_registered_IBaseTool_via_camelCase_alias()
    {
        var tool = new StubBaseTool("file_read").RespondWithSuccess("ok");
        var engine = new JsEngineFactory(builtInTools: [(Orkeon.Domain.Tools.IBaseTool)tool]).Create();

        var exists = engine.Evaluate("typeof tools.fileRead === 'function'").AsBoolean();

        Assert.True(exists);
    }

    [Fact]
    public async Task Tools_invocation_calls_IBaseTool_CallAsync_with_supplied_parameters()
    {
        var tool = new StubBaseTool("file_read").RespondWithSuccess("content here");
        var engine = new JsEngineFactory(builtInTools: [(Orkeon.Domain.Tools.IBaseTool)tool]).Create();

        var result = await Task.Run(() =>
            engine.Evaluate("tools.fileRead({ path: '/data/x.txt', encoding: 'utf-8' })").UnwrapIfPromise());

        Assert.Equal("content here", result.AsString());
        var captured = Assert.Single(tool.Calls);
        Assert.Equal("file_read", captured.ToolName);
        Assert.Contains("path", captured.Parameters.Keys);
        Assert.Equal("/data/x.txt", captured.Parameters["path"]!.ToString());
    }

    [Fact]
    public void Tools_unregistered_tool_returns_undefined()
    {
        var engine = new JsEngineFactory(builtInTools: Array.Empty<Orkeon.Domain.Tools.IBaseTool>()).Create();

        var v = engine.Evaluate("typeof tools.somethingMissing");

        Assert.Equal("undefined", v.AsString());
    }

    [Fact]
    public async Task Tools_invocation_throws_when_underlying_tool_returns_failure()
    {
        var tool = new StubBaseTool("explode").RespondWithError("kaboom");
        var engine = new JsEngineFactory(builtInTools: [(Orkeon.Domain.Tools.IBaseTool)tool]).Create();

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => Task.Run(() =>
            engine.Evaluate("tools.explode({})").UnwrapIfPromise()));
        Assert.Contains("kaboom", ex.ToString());
    }
}
