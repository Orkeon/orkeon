using Jint.Native;

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
    /// True when any turn of this request was written to the console in the foreground (#228): streamed inline,
    /// or as whole unprefixed lines when another turn held the inline claim, so the script skips its own print of
    /// <see cref="payload"/>. Background and labelled output report false: console streaming off, an agent that
    /// did not stream, and a background instance (<c>post</c>, <c>runCrewAsync</c>), whose turns are written as
    /// whole lines prefixed with the ticket (<c>[t2] </c>).
    /// </summary>
    public bool streamed { get; init; }

    /// <summary>Failure reason when <see cref="success"/> is false; otherwise <see langword="null"/>.</summary>
    public string? error { get; }

    /// <summary>
    /// The ticket of the instance that ended (#493), the one its launch returned. Set on the <c>result</c> a
    /// <c>completed</c> receives; <see langword="null"/> on the answer of a synchronous <c>request()</c>.
    /// </summary>
    public string? ticket { get; init; }

    /// <summary>
    /// The <c>state</c> the launch attached (<c>runCrewAsync(name, input, { state })</c>, <c>commands.post(..., { state })</c>),
    /// parsed again for each <c>completed</c>; undefined when the launch attached none (#493). A launch chained from a
    /// <c>completed</c> carries the state it was given, never its parent's.
    /// </summary>
    public JsValue? state { get; init; }

    /// <summary>A copy of this answer naming the instance that ended and the state its launch attached.</summary>
    internal CommandResponse ForCompletion(string completedTicket, JsValue launchState)
        => new(agent, intent, success, payload, error) { streamed = streamed, ticket = completedTicket, state = launchState };
}
#pragma warning restore IDE1006
