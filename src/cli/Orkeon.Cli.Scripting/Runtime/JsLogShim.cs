using Jint;
using Jint.Native;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Orkeon.Cli.Scripting.Runtime;

#pragma warning disable IDE1006 // intentional camelCase: exposed to JS as ctx.log
/// <summary>
/// Structured logger surface exposed to JS as <c>ctx.log</c>. Mirrors the four-method
/// shape of <c>JsExecutionContext.JsLogger</c> (debug/info/warn/error) so users move
/// between agent scripts and CLI command scripts without re-learning the API.
/// </summary>
/// <remarks>
/// The second <c>data</c> argument (an arbitrary object) declared in the <c>.d.ts</c> is
/// best-effort: when supplied we render it as a JSON-ish tail on the log message. Going
/// through Jint, the JS side may pass any <see cref="JsValue"/>; we coerce to string.
/// </remarks>
public sealed partial class JsLogShim
{
    private readonly ILogger _logger;

    public JsLogShim(ILogger? logger)
    {
        _logger = logger ?? NullLogger.Instance;
    }

    public void debug(string message)
    {
        if (_logger.IsEnabled(LogLevel.Debug))
            LogDebugMsg(message);
    }

    public void debug(string message, JsValue? data)
    {
        // CA1062: `data` is nullable by contract; Render maps null/undefined to string.Empty,
        // so we coalesce rather than throw — preserving null-tolerant behaviour.
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            var rendered = Render(data ?? JsValue.Undefined);
            LogDebugMsgData(message, rendered);
        }
    }

    public void info(string message)
    {
        if (_logger.IsEnabled(LogLevel.Information))
            LogInfoMsg(message);
    }

    public void info(string message, JsValue? data)
    {
        if (_logger.IsEnabled(LogLevel.Information))
        {
            var rendered = Render(data ?? JsValue.Undefined);
            LogInfoMsgData(message, rendered);
        }
    }

    public void warn(string message) => LogWarnMsg(message);
    public void warn(string message, JsValue? data) => LogWarnMsgData(message, Render(data ?? JsValue.Undefined));

    public void error(string message) => LogErrorMsg(message);
    public void error(string message, JsValue? data) => LogErrorMsgData(message, Render(data ?? JsValue.Undefined));

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Defensive rendering of an arbitrary JS value: any ToObject/Serialize failure falls back to the raw JsValue string so logging can never throw.")]
    private static string Render(JsValue? data)
    {
        if (data is null || data.IsUndefined() || data.IsNull()) return string.Empty;
        try
        {
            var clr = data.ToObject();
            return clr is null ? string.Empty : System.Text.Json.JsonSerializer.Serialize(clr);
        }
        catch
        {
            return data.ToString() ?? string.Empty;
        }
    }

    // --- source-generated logging ---

    [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "{Msg}")]
    partial void LogDebugMsg(string msg);

    [LoggerMessage(EventId = 2, Level = LogLevel.Debug, Message = "{Msg} {Data}")]
    partial void LogDebugMsgData(string msg, string data);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "{Msg}")]
    partial void LogInfoMsg(string msg);

    [LoggerMessage(EventId = 4, Level = LogLevel.Information, Message = "{Msg} {Data}")]
    partial void LogInfoMsgData(string msg, string data);

    [LoggerMessage(EventId = 5, Level = LogLevel.Warning, Message = "{Msg}")]
    partial void LogWarnMsg(string msg);

    [LoggerMessage(EventId = 6, Level = LogLevel.Warning, Message = "{Msg} {Data}")]
    partial void LogWarnMsgData(string msg, string data);

    [LoggerMessage(EventId = 7, Level = LogLevel.Error, Message = "{Msg}")]
    partial void LogErrorMsg(string msg);

    [LoggerMessage(EventId = 8, Level = LogLevel.Error, Message = "{Msg} {Data}")]
    partial void LogErrorMsgData(string msg, string data);
}
#pragma warning restore IDE1006
