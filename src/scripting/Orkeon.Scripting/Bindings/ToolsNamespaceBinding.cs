using System.Collections;
using System.Dynamic;
using System.Text;
using System.Text.Json;
using Jint;
using Jint.Native;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.Tools;
using Orkeon.Scripting.Telemetry;
using ToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;

namespace Orkeon.Scripting.Bindings;

/// <summary>
/// Registers the global <c>tools</c> namespace on a Jint engine. Each registered
/// <see cref="IBaseTool"/> is exposed under its name converted from snake_case to
/// camelCase (e.g. <c>file_read</c> → <c>tools.fileRead</c>).
/// </summary>
public static partial class ToolsNamespaceBinding
{
    /// <summary>Name of the global namespace exposed to scripts.</summary>
    public const string GlobalName = "tools";

    /// <summary>
    /// Adds <c>tools</c> to <paramref name="engine"/>'s global scope, populating it from
    /// <paramref name="tools"/>.
    /// </summary>
    public static void Register(Engine engine, IEnumerable<IBaseTool> tools, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(tools);
        var log = logger ?? NullLogger.Instance;

        IDictionary<string, object?> ns = new ExpandoObject();
        foreach (var tool in tools)
        {
            var jsName = ToCamelCase(tool.Name);
            ns[jsName] = WrapTool(tool);
            LogToolExposed(log, tool.Name, jsName);
        }
        engine.SetValue(GlobalName, ns);
    }

    private static Func<JsValue, Task<object?>> WrapTool(IBaseTool tool)
    {
        return async input =>
        {
            using var activity = ScriptingActivitySource.Instance.StartActivity(
                Orkeon.Constants.Llm.GenAiAttributes.SpanName(ScriptingActivitySource.ToolCallSpan, tool.Name));
            activity?.SetTag(Orkeon.Constants.Llm.GenAiAttributes.OperationName, Orkeon.Constants.Llm.GenAiAttributes.OperationExecuteTool);
            activity?.SetTag(Orkeon.Constants.Llm.GenAiAttributes.ToolName, tool.Name);
            var parameters = new Dictionary<string, object?>();
            if (input is not null && input.IsObject())
            {
                foreach (var prop in input.AsObject().GetOwnProperties())
                {
                    parameters[prop.Key.ToString()!] = prop.Value.Value.ToObject();
                }
            }
            // INFRA-2: tag the call site with disambiguating args (path,
            // url, name, query). The activity already carries `tool.name`
            // but multiple calls to the same tool need more context to be
            // identifiable in log scrapes; without this an empty
            // "tools.fileWrite failed:" trace gives no hint of which file
            // failed when there are 5 sequential writes.
            TagCallSiteArgs(activity, parameters);

            var resp = await tool.CallAsync(new ToolCallRequest(tool.Name, parameters)).ConfigureAwait(false);
            if (!resp.Success)
                throw BuildToolFailure(tool, resp, parameters);
            if (resp.Result is JsValue jv) return jv;
            // JsonElement values can crash JsValue.FromObject (the underlying pooled
            // buffer may be released before Jint reflects on them — surfaces as
            // ArgumentException "Offset and length out of bounds"). Materialise to
            // plain CLR types before handing off. The conversion itself is Jint's, on
            // its event loop: this continuation runs on a thread-pool thread, and a
            // Promise.all over several tool calls converted concurrently would race
            // inside the single-threaded engine.
            return UnwrapJsonElements(resp.Result);
        };
    }

    private static InvalidOperationException BuildToolFailure(
        IBaseTool tool,
        Orkeon.Domain.Tools.Protocol.ToolCallResponse resp,
        Dictionary<string, object?> parameters)
    {
        // INFRA-1: when ToolCallResponse.Error is empty (typed
        // tools that populate Errors[] on the response object
        // rather than the protocol-level Error), fall back to
        // mining the Result dict for an `errors` list. This
        // turns "tools.fileWrite failed: " into a meaningful
        // diagnostic like "tools.fileWrite failed: FQN=...:
        // symbol not found in store".
        var detail = !string.IsNullOrWhiteSpace(resp.Error)
            ? resp.Error
            : ExtractDetailFromResult(resp.Result);
        if (string.IsNullOrWhiteSpace(detail))
            detail = "(no error message provided)";

        var argHint = DescribeArgs(parameters);
        var argSuffix = string.IsNullOrEmpty(argHint) ? string.Empty : $" [{argHint}]";
        return new InvalidOperationException(
            $"tools.{ToCamelCase(tool.Name)} failed{argSuffix}: {detail}");
    }

    private static void TagCallSiteArgs(System.Diagnostics.Activity? activity, Dictionary<string, object?> parameters)
    {
        if (activity is null) return;
        // Common disambiguating keys — first non-null wins per tag. Result is discarded:
        // tagging is best-effort and each call is independent.
        _ = TryTagFirst(activity, parameters, "tool.arg.path", "path", "file_path", "virtual_path");
        _ = TryTagFirst(activity, parameters, "tool.arg.url", "url", "endpoint");
        _ = TryTagFirst(activity, parameters, "tool.arg.name", "name", "tool_name", "fqn", "symbol", "id");
        _ = TryTagFirst(activity, parameters, "tool.arg.query", "query", "q", "search");
    }

    private static bool TryTagFirst(System.Diagnostics.Activity activity, Dictionary<string, object?> parameters, string tagKey, params string[] candidateKeys)
    {
        foreach (var k in candidateKeys)
        {
            if (parameters.TryGetValue(k, out var v) && v is not null)
            {
                var s = v.ToString();
                if (!string.IsNullOrWhiteSpace(s))
                {
                    activity.SetTag(tagKey, Truncate(s!, 200));
                    return true;
                }
            }
        }
        return false;
    }

    private static string DescribeArgs(Dictionary<string, object?> parameters)
    {
        // Surface the same disambiguating keys in the exception message
        // (capped to 200 chars to avoid log noise).
        foreach (var k in new[] { "path", "file_path", "virtual_path", "url", "endpoint", "fqn", "symbol", "query", "q", "name" })
        {
            if (parameters.TryGetValue(k, out var v) && v is not null)
            {
                var s = v.ToString();
                if (!string.IsNullOrWhiteSpace(s)) return $"{k}={Truncate(s!, 200)}";
            }
        }
        return string.Empty;
    }

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s.AsSpan(0, max).ToString() + "…";

    private static string? ExtractDetailFromResult(object? result)
    {
        var dict = AsStringKeyedDictionary(result);
        if (dict is null) return null;

        if (dict.TryGetValue("error", out var err) && err is string es && !string.IsNullOrWhiteSpace(es))
            return es;

        if (dict.TryGetValue("errors", out var errs) && errs is not null)
            return ExtractErrorsDetail(errs);

        return null;
    }

    private static IDictionary<string, object?>? AsStringKeyedDictionary(object? result)
    {
        if (result is IDictionary<string, object?> d) return d;
        if (result is IDictionary<string, object> d2)
            return d2.ToDictionary<KeyValuePair<string, object>, string, object?>(p => p.Key, p => p.Value);
        return new Dictionary<string, object?>();
    }

    private static string? ExtractErrorsDetail(object errs)
    {
        if (errs is string single) return single;
        if (errs is not System.Collections.IEnumerable enumerable) return null;

        var parts = new List<string>();
        foreach (var item in enumerable)
        {
            var s = item?.ToString();
            if (!string.IsNullOrWhiteSpace(s)) parts.Add(s!);
        }
        return parts.Count > 0 ? string.Join("; ", parts) : null;
    }

    private static object? UnwrapJsonElements(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case JsonElement el:
                return UnwrapJsonElement(el);
            case string s:
                return s;
            // NB: IDictionary<string, object?> and IDictionary<string, object> are the
            // same type at runtime (nullability annotations are erased), so this single
            // case covers both shapes the legacy if-chain attempted to distinguish; the
            // second if-branch in the original was dead for the same reason.
            case IDictionary<string, object?> nullableDict:
                return UnwrapNullableDictionary(nullableDict);
            case IEnumerable list:
                return UnwrapEnumerable(list);
            default:
                return value;
        }
    }

    private static object? UnwrapJsonElement(JsonElement el)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                var objDict = new Dictionary<string, object?>();
                foreach (var p in el.EnumerateObject())
                    objDict[p.Name] = UnwrapJsonElements(p.Value);
                return objDict;
            case JsonValueKind.Array:
                return UnwrapEnumerable(el.EnumerateArray());
            case JsonValueKind.String:
                return el.GetString();
            case JsonValueKind.Number:
                return el.TryGetInt64(out var i) ? (object)i : el.GetDouble();
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.Null:
                return null;
            default:
                return el.GetRawText();
        }
    }

    private static Dictionary<string, object?> UnwrapNullableDictionary(IDictionary<string, object?> source)
    {
        var d = new Dictionary<string, object?>(source.Count);
        foreach (var (k, v) in source) d[k] = UnwrapJsonElements(v);
        return d;
    }

    // Return a CLR array (not List<>) so Jint exposes it as a real JS Array
    // with full Array.prototype (.map/.filter/.some/.concat).
    private static object?[] UnwrapEnumerable(IEnumerable list)
    {
        var outList = new List<object?>();
        foreach (var item in list) outList.Add(UnwrapJsonElements(item));
        return outList.ToArray();
    }

    /// <summary>
    /// Converts <c>snake_case</c> (or <c>kebab-case</c>) tool names to camelCase.
    /// Internal so tests can verify the conversion logic in isolation.
    /// </summary>
    internal static string ToCamelCase(string source)
    {
        if (string.IsNullOrEmpty(source)) return source;
        var sb = new StringBuilder(source.Length);
        var nextUpper = false;
        foreach (var c in source)
        {
            if (c == '_' || c == '-')
            {
                nextUpper = sb.Length > 0;
                continue;
            }
            sb.Append(nextUpper ? char.ToUpperInvariant(c) : c);
            nextUpper = false;
        }
        if (sb.Length > 0) sb[0] = char.ToLowerInvariant(sb[0]);
        return sb.ToString();
    }

    // --- source-generated logging ---

    [LoggerMessage(EventId = 1, Level = LogLevel.Trace,
        Message = "Exposed tool '{Name}' as 'tools.{JsName}'")]
    static partial void LogToolExposed(ILogger logger, string name, string jsName);
}
