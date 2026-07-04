using Jint;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Scripting.Internal;
using Orkeon.Scripting.Runtime;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Scripting.Tests.Bindings;

/// <summary>
/// Regression tests for the INFRA-1/2/3 error propagation fixes in
/// <see cref="Orkeon.Scripting.Bindings.ToolsNamespaceBinding"/> and
/// <see cref="JsCrew"/>. Background: R14 of the
/// 02-ts-coding-agent-replication experiment surfaced an empty
/// <c>tools.fileWrite failed:</c> exception because the typed tool
/// pipeline populates <see cref="ToolCallResponse.Result"/>'s
/// <c>errors</c> list rather than the protocol-level
/// <see cref="ToolCallResponse.Error"/> singular. The binding now
/// mines the dict, tags call-site args, and the crew hook peels
/// promise / aggregate wrappers.
/// </summary>
public sealed class ToolsNamespaceErrorPropagationTests
{
    [Fact]
    public async Task INFRA1_Error_message_falls_back_to_errors_list_in_Result_dict()
    {
        var resultWithErrorsList = new Dictionary<string, object?>
        {
            ["path"] = "/foo.md",
            ["errors"] = new[]
            {
                "FQN=Bar::baz: symbol not found in store",
                "FQN=Baz::qux: SHA mismatch",
            },
        };
        var tool = new StubBaseTool("file_write")
            .RespondWith(_ => new ToolCallResponse(false, resultWithErrorsList, null));
        var engine = new JsEngineFactory(builtInTools: [tool]).Create();

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => Task.Run(() =>
            engine.Evaluate("tools.fileWrite({ path: '/foo.md', content: 'x' })").UnwrapIfPromise()));

        Assert.Contains("FQN=Bar::baz: symbol not found in store", ex.ToString());
        Assert.Contains("FQN=Baz::qux: SHA mismatch", ex.ToString());
    }

    [Fact]
    public async Task INFRA1_Error_message_falls_back_to_singular_error_key_in_Result_dict()
    {
        var resultWithSingularError = new Dictionary<string, object?>
        {
            ["error"] = "permission denied",
        };
        var tool = new StubBaseTool("file_write")
            .RespondWith(_ => new ToolCallResponse(false, resultWithSingularError, null));
        var engine = new JsEngineFactory(builtInTools: [tool]).Create();

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => Task.Run(() =>
            engine.Evaluate("tools.fileWrite({ path: '/foo.md', content: 'x' })").UnwrapIfPromise()));

        Assert.Contains("permission denied", ex.ToString());
    }

    [Fact]
    public async Task INFRA1_Falls_back_to_explicit_placeholder_when_no_detail_anywhere()
    {
        var tool = new StubBaseTool("file_write")
            .RespondWith(_ => new ToolCallResponse(false, null, null));
        var engine = new JsEngineFactory(builtInTools: [tool]).Create();

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => Task.Run(() =>
            engine.Evaluate("tools.fileWrite({ path: '/foo.md', content: 'x' })").UnwrapIfPromise()));

        Assert.Contains("no error message provided", ex.ToString());
    }

    [Fact]
    public async Task INFRA2_Exception_message_surfaces_path_argument_as_call_site_hint()
    {
        var tool = new StubBaseTool("file_write").RespondWithError("boom");
        var engine = new JsEngineFactory(builtInTools: [tool]).Create();

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => Task.Run(() =>
            engine.Evaluate("tools.fileWrite({ path: '/output/03-crews-design.md', content: '...' })").UnwrapIfPromise()));

        Assert.Contains("path=/output/03-crews-design.md", ex.ToString());
        Assert.Contains("boom", ex.ToString());
    }

    [Fact]
    public async Task INFRA2_Exception_message_surfaces_fqn_argument_for_symbol_tools()
    {
        var tool = new StubBaseTool("symbol_source").RespondWithError("not in index");
        var engine = new JsEngineFactory(builtInTools: [tool]).Create();

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => Task.Run(() =>
            engine.Evaluate("tools.symbolSource({ fqn: '/src/foo.ts::Bar::baz' })").UnwrapIfPromise()));

        Assert.Contains("fqn=/src/foo.ts::Bar::baz", ex.ToString());
    }

    [Fact]
    public async Task INFRA2_Long_arg_values_are_truncated_with_ellipsis()
    {
        var tool = new StubBaseTool("file_write").RespondWithError("boom");
        var engine = new JsEngineFactory(builtInTools: [tool]).Create();
        var longPath = "/output/" + new string('x', 500) + ".md";

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => Task.Run(() =>
            engine.Evaluate($"tools.fileWrite({{ path: '{longPath}', content: '...' }})").UnwrapIfPromise()));

        Assert.Contains("path=/output/xxxx", ex.ToString());
        Assert.Contains("…", ex.ToString());
    }

    [Fact]
    public void INFRA3_UnwrapToInnermost_peels_AggregateException_to_inner_message()
    {
        var inner = new InvalidOperationException("tools.fileWrite failed [path=/x.md]: FQN=Foo not found");
        var agg = new AggregateException(inner);

        var unwrapped = JsExceptionUnwrap.UnwrapToInnermost(agg);

        Assert.IsType<InvalidOperationException>(unwrapped);
        Assert.Equal(inner.Message, unwrapped.Message);
    }

    [Fact]
    public void INFRA3_UnwrapToInnermost_peels_nested_AggregateException_chain()
    {
        var leaf = new InvalidOperationException("real cause");
        var agg1 = new AggregateException(leaf);
        var agg2 = new AggregateException(agg1);
        var agg3 = new AggregateException(agg2);

        var unwrapped = JsExceptionUnwrap.UnwrapToInnermost(agg3);

        Assert.Equal("real cause", unwrapped.Message);
    }

    [Fact]
    public void INFRA3_UnwrapToInnermost_peels_RuntimeException_typenames_by_suffix()
    {
        // The unwrap logic identifies Jint wrappers by FullName suffix
        // (".RuntimeException" / ".WrappedException") because the Scripting
        // assembly does not directly reference Jint.Runtime. A test double
        // class named *RuntimeException is enough to exercise the path.
        var leaf = new InvalidOperationException("real cause");
        var wrapper = new FakeRuntimeException("promise wrapper", leaf);

        var unwrapped = JsExceptionUnwrap.UnwrapToInnermost(wrapper);

        Assert.Equal("real cause", unwrapped.Message);
    }

    [Fact]
    public void INFRA3_UnwrapToInnermost_returns_itself_for_a_plain_exception()
    {
        var ex = new InvalidOperationException("standalone");

        var unwrapped = JsExceptionUnwrap.UnwrapToInnermost(ex);

        Assert.Same(ex, unwrapped);
    }

    [Fact]
    public void INFRA3_UnwrapToInnermost_is_bounded_against_pathological_cycles()
    {
        var deep = new InvalidOperationException("deep");
        Exception current = deep;
        for (var i = 0; i < 32; i++)
        {
            current = new AggregateException(current);
        }

        var unwrapped = JsExceptionUnwrap.UnwrapToInnermost(current);

        Assert.NotNull(unwrapped);
    }

    private sealed class FakeRuntimeException : Exception
    {
        public FakeRuntimeException(string message, Exception inner) : base(message, inner) { }
    }
}
