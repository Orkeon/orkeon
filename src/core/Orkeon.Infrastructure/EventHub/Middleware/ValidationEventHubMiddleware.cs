using Orkeon.Application.EventHub;
using Orkeon.Application.EventHub.Exceptions;

namespace Orkeon.Infrastructure.EventHub.Middleware;

/// <summary>
/// Checks that a message's <c>SchemaId</c> names a contract the registry actually holds
/// (HUB-04, spec §12). Last of the five stages, because it is the only one that touches an
/// external store.
/// <para>
/// **What it checks, precisely**: that the declared contract exists — not that the payload
/// conforms to it. The repository ships no JSON Schema engine, and half of one would look like
/// a guarantee while being none. Naming a schema nobody registered is the mistake this stage
/// catches: a typo in a schema id, or an event type the deployment never declared.
/// </para>
/// <para>
/// A message carrying <see cref="Message.NoDeclaredSchemaId"/> declares no contract at all —
/// every <c>Post</c>, <c>Send</c> and <c>Reply</c> does — and passes. Declaring a schema is what
/// engages the check, the same way declaring a link is what closes the ACL's door.
/// </para>
/// </summary>
internal sealed class ValidationEventHubMiddleware : IEventHubMiddleware
{
    private readonly IEventSchemaRegistry _schemas;

    /// <summary>Builds the stage over the schema registry it consults.</summary>
    public ValidationEventHubMiddleware(IEventSchemaRegistry schemas) =>
        _schemas = schemas ?? throw new ArgumentNullException(nameof(schemas));

    /// <inheritdoc />
    public async Task<Message> OnPublishAsync(Message message, Func<Message, Task<Message>> nextHandler, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(nextHandler);

        if (!string.Equals(message.SchemaId, Message.NoDeclaredSchemaId, StringComparison.Ordinal))
        {
            var schema = await _schemas.GetAsync(message.SchemaId, ct).ConfigureAwait(false);
            if (schema is null)
                throw new EventValidationException(message.SchemaId, message.Topic);
        }

        return await nextHandler(message).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<Message> OnReceiveAsync(Message message, Func<Message, Task<Message>> nextHandler, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(nextHandler);

        // The refusal has to reach whoever can act on it. A subscriber cannot fix a schema
        // declared by someone else, so the check stays on the publish side — the same reasoning
        // that puts the ACL there.
        return nextHandler(message);
    }
}
