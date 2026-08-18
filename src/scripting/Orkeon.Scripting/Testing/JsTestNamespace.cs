using System.Text.RegularExpressions;
using Jint;
using Jint.Native;

namespace Orkeon.Scripting.Testing;

/// <summary>
/// Surface exposed to JS as <c>test</c>. Provides mock factories
/// (<c>test.mockLlm</c>, <c>test.mockTool(name)</c>) and assertion helpers
/// (<c>test.assertLlmCalled</c>, <c>test.assertToolCalled</c>).
/// </summary>
#pragma warning disable IDE1006
#pragma warning disable CS1591
public sealed class JsTestNamespace
{
    public MockLlmProvider Llm { get; }
    private readonly Dictionary<string, MockTool> _tools = new(StringComparer.Ordinal);

    public JsMockLlmHandle mockLlm { get; }
    public Func<string, JsMockToolHandle> mockTool { get; }

    public JsTestNamespace()
    {
        Llm = new MockLlmProvider();
        mockLlm = new JsMockLlmHandle(Llm);
        mockTool = name =>
        {
            if (!_tools.TryGetValue(name, out var tool))
                _tools[name] = tool = new MockTool(name);
            return new JsMockToolHandle(tool);
        };
    }

    /// <summary>Returns the mock tool registered for <paramref name="name"/>.</summary>
    public MockTool? GetMockTool(string name) => _tools.TryGetValue(name, out var t) ? t : null;

    public Action<JsValue?> assertLlmCalled => options =>
    {
        var (times, with) = ParseAssertOptions(options);
        var matchingCalls = Llm.GenerateCalls.Concat(Llm.ChatCalls.SelectMany(m => m.Select(x => x.Content)))
            .Count(call => with is null || call.Contains(with, StringComparison.OrdinalIgnoreCase));
        if (times.HasValue && matchingCalls != times.Value)
            throw new AssertionException(
                $"assertLlmCalled: expected {times.Value} calls but observed {matchingCalls}.");
        if (!times.HasValue && matchingCalls == 0)
            throw new AssertionException("assertLlmCalled: expected at least one call but none were observed.");
    };

    public Action<string, JsValue?> assertToolCalled => (name, options) =>
    {
        if (!_tools.TryGetValue(name, out var tool))
            throw new AssertionException($"assertToolCalled: no mock tool registered as '{name}'.");
        var (times, _) = ParseAssertOptions(options);
        if (times.HasValue && tool.Calls.Count != times.Value)
            throw new AssertionException(
                $"assertToolCalled('{name}'): expected {times.Value} calls but observed {tool.Calls.Count}.");
        if (!times.HasValue && tool.Calls.Count == 0)
            throw new AssertionException(
                $"assertToolCalled('{name}'): expected at least one call but none were observed.");
    };

    private static (int? Times, string? With) ParseAssertOptions(JsValue? options)
    {
        if (options is null || !options.IsObject()) return (null, null);
        int? times = null;
        string? with = null;
        var t = options.Get("times"); if (t.IsNumber()) times = (int)t.AsNumber();
        var w = options.Get("with"); if (w.IsString()) with = w.AsString();
        return (times, with);
    }
}

public sealed class JsMockLlmHandle
{
    private readonly MockLlmProvider _provider;
    internal JsMockLlmHandle(MockLlmProvider provider) { _provider = provider; }
    public JsMockLlmExpectationBuilder when(JsValue matcher) => new(_provider, matcher);
}

/// <summary>
/// Builds mock-LLM expectations in the scripting test namespace.
/// </summary>
public sealed class JsMockLlmExpectationBuilder
{
    private readonly MockLlmProvider _provider;
    private readonly JsValue _matcher;
    internal JsMockLlmExpectationBuilder(MockLlmProvider p, JsValue m) { _provider = p; _matcher = m; }

    public void respond(JsValue response)
    {
        ArgumentNullException.ThrowIfNull(response);
        string content;
        if (response.IsString())
            content = response.AsString();
        else if (response.IsObject() && response.Get("content").IsString())
            content = response.Get("content").AsString();
        else
            content = response.ToString();

        string? contains = null;
        Regex? regex = null;
        if (_matcher.IsString())
            contains = _matcher.AsString();
        else if (_matcher.IsObject())
        {
            var p = _matcher.Get("prompt");
            if (p.IsString()) contains = p.AsString();
            else if (p.IsRegExp())
            {
                var src = p.Get("source").AsString();
                regex = new Regex(src, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
            }
            else if (p.ToObject() is Regex re) regex = re;
        }
        _provider.AddExpectation(new MockLlmExpectation(contains, regex, content));
    }
}

public sealed class JsMockToolHandle
{
    private readonly MockTool _tool;
    internal JsMockToolHandle(MockTool tool) { _tool = tool; }
    public JsMockToolExpectationBuilder when(JsValue matcher) => new(_tool, matcher);

    /// <summary>Configures the default tool response.</summary>
    public void respond(JsValue response)
    {
        ArgumentNullException.ThrowIfNull(response);
        _tool.Response = response.ToObject();
    }
}

/// <summary>
/// Builds mock-tool expectations in the scripting test namespace.
/// </summary>
public sealed class JsMockToolExpectationBuilder
{
    private readonly MockTool _tool;
    private readonly JsValue _matcher;
    internal JsMockToolExpectationBuilder(MockTool t, JsValue m) { _tool = t; _matcher = m; }

    public void respond(JsValue response)
    {
        ArgumentNullException.ThrowIfNull(response);
        Func<Orkeon.Domain.Tools.Protocol.ToolCallRequest, bool> matcher;
        if (_matcher.IsObject())
        {
            var keys = new Dictionary<string, object?>();
            foreach (var prop in _matcher.AsObject().GetOwnProperties())
                keys[prop.Key.ToString()!] = prop.Value.Value.ToObject();
            matcher = req => keys.All(kv =>
                req.Parameters.TryGetValue(kv.Key, out var actual) && Equals(actual, kv.Value));
        }
        else
        {
            matcher = _ => true;
        }
        _tool.AddExpectation(matcher, response.ToObject());
    }
}

[Serializable]
public sealed class AssertionException : Exception
{
    public AssertionException(string message) : base(message) { }

    public AssertionException() { }

    public AssertionException(string message, Exception innerException) : base(message, innerException) { }

#pragma warning disable SYSLIB0051 // Required by S3925 ISerializable pattern
    private AssertionException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context)
        : base(info, context) { }
#pragma warning restore SYSLIB0051
}
#pragma warning restore CS1591
#pragma warning restore IDE1006
