namespace Orkeon.Scripting.ErrorPolicy;

/// <summary>
/// Context passed to <c>onError</c> handlers describing what just failed. Modeled to
/// stay JS-friendly while preserving the C# exception object for advanced use cases.
/// </summary>
#pragma warning disable IDE1006
#pragma warning disable CS1591
public sealed class JsErrorContext
{
    public string code { get; }
    public string message { get; }
    public Exception exception { get; }
    public int attempt { get; }
    public JsAgentRef agent { get; }

    internal JsErrorContext(string code, string message, Exception exception, int attempt, JsAgentRef agent)
    {
        this.code = code;
        this.message = message;
        this.exception = exception;
        this.attempt = attempt;
        this.agent = agent;
    }
}

public sealed class JsAgentRef
{
    public string id { get; }
    public string name { get; }

    internal JsAgentRef(string id, string name)
    {
        this.id = id;
        this.name = name;
    }
}
#pragma warning restore CS1591
#pragma warning restore IDE1006
