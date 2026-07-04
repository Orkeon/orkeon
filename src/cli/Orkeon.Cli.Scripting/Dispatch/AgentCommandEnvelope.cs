namespace Orkeon.Cli.Scripting.Dispatch;

#pragma warning disable IDE1006 // camelCase: reflected to JS as the argument of an agent's onCommand handler
/// <summary>
/// The request envelope handed to an agent's <c>onCommand</c> handler (design §10 open point,
/// resolved here). A handler reads <see cref="intent"/>/<see cref="payload"/> and returns the
/// response payload (string) or <c>{ success?, payload, error? }</c>.
/// </summary>
public sealed class AgentCommandEnvelope
{
    public AgentCommandEnvelope(string intent, string payload, string from, string correlationId)
    {
        this.intent = intent;
        this.payload = payload;
        this.from = from;
        this.correlationId = correlationId;
    }

    /// <summary>Intent the command was dispatched with.</summary>
    public string intent { get; }

    /// <summary>Command payload.</summary>
    public string payload { get; }

    /// <summary>Logical id of the caller (the CLI dispatcher).</summary>
    public string from { get; }

    /// <summary>Host-side correlation id of the dispatched command.</summary>
    public string correlationId { get; }
}
#pragma warning restore IDE1006
