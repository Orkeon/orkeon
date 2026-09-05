namespace Orkeon.Scripting.ErrorPolicy;

/// <summary>
/// Context passed to <c>onError</c> handlers describing what just failed. Modeled to
/// stay JS-friendly while preserving the C# exception object for advanced use cases.
/// </summary>
#pragma warning disable IDE1006
public sealed class JsErrorContext
{
    /// <summary>Normalized code produced by <see cref="ErrorCodeMapper.MapToCode"/>.</summary>
    public string code { get; }

    /// <summary>Message of the failing exception.</summary>
    public string message { get; }

    /// <summary>The CLR exception itself, for handlers that need more than the code.</summary>
    public Exception exception { get; }

    /// <summary>1-based attempt number of the body that just failed.</summary>
    public int attempt { get; }

    /// <summary>Identity of the agent whose body failed.</summary>
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

/// <summary>
/// Minimal agent identity carried by <see cref="JsErrorContext"/>: enough for a handler to
/// branch on, without exposing the whole agent to the error path.
/// </summary>
public sealed class JsAgentRef
{
    /// <summary>ULID-formatted agent identifier (26 chars).</summary>
    public string id { get; }

    /// <summary>Human-readable agent name declared by the script.</summary>
    public string name { get; }

    internal JsAgentRef(string id, string name)
    {
        this.id = id;
        this.name = name;
    }
}
#pragma warning restore IDE1006
