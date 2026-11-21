using System.Text.Json;
using Orkeon.Domain.Tools;
using ProtocolToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;
using ProtocolToolCallResponse = Orkeon.Domain.Tools.Protocol.ToolCallResponse;
using ToolSchema = Orkeon.Domain.Tools.Protocol.ToolSchema;

namespace Orkeon.Hosting.ToolBridge;

/// <summary>
/// A tool of the caller that runs on the bridge (#445): it wraps the caller's own instance, whose name, description,
/// schema and access classes the caller's pipeline keeps reading, and sends only the call. A bridged tool never runs
/// locally: a bridge that is lost fails the call with its reason, and the next call tries again.
/// <para>
/// Cancelling the call aborts its request and asks the bridge to cancel it there. The call carries its arguments
/// and context alone; the run it belongs to and the files it reads or writes are added to the call by a later pass.
/// </para>
/// </summary>
internal sealed class ToolBridgeProxy : IBaseTool, IToolWrapper
{
    /// <summary>The metadata key a failure of the bridge itself sets, so a reader tells it from the tool's own failure.</summary>
    public const string BridgeFailureKey = "bridge_failure";

    // How long the cancellation of an abandoned call may take.
    private static readonly TimeSpan CancelBound = TimeSpan.FromSeconds(5);

    private readonly ToolBridgeClient _client;

    /// <summary>Wraps <paramref name="inner"/>, whose calls go to <paramref name="client"/>.</summary>
    /// <param name="inner">The caller's own instance of the tool.</param>
    /// <param name="client">The bridge.</param>
    public ToolBridgeProxy(IBaseTool inner, ToolBridgeClient client)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(client);
        Inner = inner;
        _client = client;
    }

    /// <inheritdoc />
    public IBaseTool Inner { get; }

    /// <inheritdoc />
    public string Name => Inner.Name;

    /// <inheritdoc />
    public string Description => Inner.Description;

    /// <inheritdoc />
    public ToolSchema Schema => Inner.Schema;

    /// <inheritdoc />
    public ToolAccess Access => Inner.Access;

    /// <inheritdoc />
    public ToolAccess AccessFor(IReadOnlyDictionary<string, object?> arguments) => Inner.AccessFor(arguments);

    /// <inheritdoc />
    public bool UsesFileSystem => Inner.UsesFileSystem;

    /// <inheritdoc />
    public async Task<ProtocolToolCallResponse> CallAsync(ProtocolToolCallRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var call = new BridgeCallRequest
        {
            CallId = Guid.NewGuid().ToString("N"),
            Tool = Name,
            Arguments = JsonSerializer.SerializeToElement(request.Parameters ?? [], ToolBridgeProtocol.JsonOptions),
            Context = request.Context,
        };
        try
        {
            return FromWire(await _client.CallAsync(call, cancellationToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            using var bound = new CancellationTokenSource(CancelBound);
            await _client.CancelAsync(call.CallId, bound.Token).ConfigureAwait(false);
            return new ProtocolToolCallResponse(false, null, "Operation cancelled", new Dictionary<string, object?> { ["cancelled"] = true });
        }
        catch (ToolBridgeException ex)
        {
            return new ProtocolToolCallResponse(false, null, $"{Name} runs on the tool bridge, and {ex.Message}",
                new Dictionary<string, object?> { [BridgeFailureKey] = ex.Failure.ToString() });
        }
    }

    /// <inheritdoc />
    public async Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default)
    {
        Dictionary<string, object?> arguments;
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(input) ? "{}" : input);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return ToolResult.CreateError($"{Name} takes a JSON object of arguments.");
            arguments = ToolBridgeProtocol.ToPlainObject(document.RootElement);
        }
        catch (JsonException)
        {
            return ToolResult.CreateError($"{Name} takes a JSON object of arguments.");
        }

        var response = await CallAsync(new ProtocolToolCallRequest(Name, arguments), cancellationToken).ConfigureAwait(false);
        return response.Success
            ? ToolResult.CreateSuccess(response.Result switch
            {
                null => "",
                string text => text,
                var other => JsonSerializer.Serialize(other, ToolBridgeProtocol.JsonOptions),
            })
            : ToolResult.CreateError(response.Error ?? $"{Name} failed on the tool bridge.");
    }

    /// <inheritdoc />
    public bool ValidateInput(string input) => Inner.ValidateInput(input);

    /// <summary>
    /// The wire's response as the caller's pipeline reads it: a JSON string becomes a string, an object a dictionary,
    /// an array a list, so the pipeline renders them as the tool's own result.
    /// </summary>
    /// <param name="response">The bridge's answer.</param>
    /// <returns>The tool's response.</returns>
    internal static ProtocolToolCallResponse FromWire(BridgeCallResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        var metadata = response.Metadata?.ToDictionary(p => p.Key, p => ToolBridgeProtocol.ToPlain(p.Value), StringComparer.Ordinal);
        var result = response.Result is { } element ? ToolBridgeProtocol.ToPlain(element) : null;
        return new ProtocolToolCallResponse(response.Success, result, response.Error, metadata)
        {
            AlreadyBounded = response.AlreadyBounded,
        };
    }
}
