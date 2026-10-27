namespace Orkeon.Cli.Commands.Scripting.Dispatch;

#pragma warning disable IDE1006 // camelCase: this CLR type is reflected to JS as the resolved value of request()/completed(result)
/// <summary>
/// JS-facing result of a dispatched command — the value a synchronous <c>request()</c>
/// resolves to, and the <c>result</c> argument handed to <c>completed(result, ctx)</c>.
/// </summary>
/// <remarks>
/// Mirrors the agent's <c>AgentChannelResponse</c> plus the routing metadata (agent name,
/// intent) so a handler can react without re-deriving them.
/// </remarks>
public sealed class CommandResponse
{
    public CommandResponse(string agent, string intent, bool success, string payload, string? error)
    {
        this.agent = agent ?? string.Empty;
        this.intent = intent ?? string.Empty;
        this.success = success;
        this.payload = payload ?? string.Empty;
        this.error = error;
    }

    /// <summary>Logical name of the agent that finished the command.</summary>
    public string agent { get; }

    /// <summary>Intent the command was dispatched with.</summary>
    public string intent { get; }

    /// <summary>True when the agent's handler returned a success response.</summary>
    public bool success { get; }

    /// <summary>The agent's response payload (empty on failure).</summary>
    public string payload { get; }

    /// <summary>
    /// True when the turn that produced <see cref="payload"/> already streamed inline to the console (#228):
    /// the foreground answer, written token by token while it arrived, so the script skips its own print.
    /// False when console streaming is off, when the agent did not stream, and for a background instance
    /// (<c>post</c>, <c>runCrewAsync</c>): its turns are written as whole lines prefixed with the ticket
    /// (<c>[t2] </c>), not inline.
    /// </summary>
    public bool streamed { get; init; }

    /// <summary>Failure reason when <see cref="success"/> is false; otherwise <see langword="null"/>.</summary>
    public string? error { get; }
}
#pragma warning restore IDE1006
