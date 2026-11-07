using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Orkeon.Application.Runs;
using Orkeon.Infrastructure.Security;

namespace Orkeon.Infrastructure.Runs;

/// <summary>
/// Masks the secrets an event carries before a durable sink writes it: the arguments of a tool call, the content of a model
/// call and the message of an error reach the history in clear otherwise, and the history is a folder on disk. The masking is
/// the repository's own (<see cref="LogSanitizer"/>, the one the exchange log and the logs use): every string of the payload
/// goes through it, and the value of a property named like a credential (<c>password</c>, <c>token</c>, <c>api_key</c>,
/// <c>authorization</c>...) is replaced whatever its shape. The stdout stream is not masked here: it is the live contract with
/// the process that started the run.
/// </summary>
[Experimental("ORKEXP007", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public sealed partial class RedactingRunEventSink : IRunEventSink
{
    private const string Mask = "***REDACTED***";

    private readonly IRunEventSink _inner;

    /// <summary>Wraps <paramref name="inner"/>.</summary>
    public RedactingRunEventSink(IRunEventSink inner) => _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    /// <inheritdoc />
    public System.Threading.Tasks.Task WriteAsync(RunEvent runEvent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(runEvent);
        return _inner.WriteAsync(runEvent.Payload is null ? runEvent : runEvent with { Payload = (JsonObject)Redact(runEvent.Payload)! }, ct);
    }

    [GeneratedRegex(@"^(?:pass(?:word|wd)|secret|(?:client|api|access|refresh|auth)?_?(?:token|key)|credentials?|authorization|cookie)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CredentialName();

    private static JsonNode? Redact(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                var copy = new JsonObject();
                foreach (var property in obj)
                {
                    copy[property.Key] = property.Value is JsonValue value && value.GetValueKind() == JsonValueKind.String && CredentialName().IsMatch(property.Key)
                        ? JsonValue.Create(Mask)
                        : Redact(property.Value);
                }

                return copy;
            case JsonArray array:
                var items = new JsonArray();
                foreach (var item in array)
                    items.Add(Redact(item));
                return items;
            case JsonValue text when text.GetValueKind() == JsonValueKind.String:
                return JsonValue.Create(LogSanitizer.SanitizeString(text.GetValue<string>()));
            default:
                return node?.DeepClone();
        }
    }
}
