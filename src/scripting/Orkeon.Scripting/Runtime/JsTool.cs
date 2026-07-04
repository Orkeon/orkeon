using System.Text.Json;
using Jint;
using Jint.Native;
using Orkeon.Domain.Tools;
using ToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;
using ToolCallResponse = Orkeon.Domain.Tools.Protocol.ToolCallResponse;
using ToolSchema = Orkeon.Domain.Tools.Protocol.ToolSchema;

namespace Orkeon.Scripting.Runtime;

/// <summary>
/// IBaseTool implementation produced by <c>toolBuilder()</c>. Wraps a JS <c>execute</c>
/// callback so that scripts can author custom tools without writing C#. The JS callback
/// receives the tool input (deserialized from <c>ToolCallRequest.Parameters</c>) and
/// returns either a value or a Promise that resolves to one.
/// </summary>
#pragma warning disable IDE1006
#pragma warning disable CS1591
public sealed class JsTool : IBaseTool
{
    public string Name { get; }
    public string Description { get; }
    public ToolSchema Schema { get; }

    /// <summary>Whether <c>.withSchema(...)</c> was called on the builder.</summary>
    internal bool HasExplicitSchema { get; }

    private readonly Engine _engine;
    private readonly JsValue _executeCallback;

    internal JsTool(string name, string description, ToolSchema schema, bool hasExplicitSchema,
        Engine engine, JsValue executeCallback)
    {
        Name = name;
        Description = description;
        Schema = schema;
        HasExplicitSchema = hasExplicitSchema;
        _engine = engine;
        _executeCallback = executeCallback;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Tool-execution fault barrier: any failure of the user-supplied JS execute callback (Jint JavaScriptException or CLR error) is converted to an unsuccessful ToolCallResponse carrying the message, so a buggy script tool cannot crash the host.")]
    public Task<ToolCallResponse> CallAsync(ToolCallRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return CallCoreAsync();

        async Task<ToolCallResponse> CallCoreAsync()
        {
            try
            {
                var input = JsValue.FromObject(_engine, request.Parameters);
                var result = _engine.Invoke(_executeCallback, [input, JsValue.Undefined]);
                var unwrapped = result.IsPromise()
                    ? await result.UnwrapIfPromiseAsync(cancellationToken).ConfigureAwait(false)
                    : result;
                return new ToolCallResponse(true, unwrapped.ToObject(), null);
            }
            catch (Exception ex)
            {
                return new ToolCallResponse(false, null, ex.Message);
            }
        }
    }

    public async Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default)
    {
        Dictionary<string, object?> parameters;
        try
        {
            parameters = string.IsNullOrWhiteSpace(input)
                ? new Dictionary<string, object?>()
                : JsonSerializer.Deserialize<Dictionary<string, object?>>(input)
                  ?? new Dictionary<string, object?>();
        }
        catch (JsonException ex)
        {
            return ToolResult.CreateError($"Invalid JSON input: {ex.Message}");
        }

        var response = await CallAsync(new ToolCallRequest(Name, parameters), cancellationToken).ConfigureAwait(false);
        return response.Success
            ? ToolResult.CreateSuccess(response.Result?.ToString() ?? string.Empty)
            : ToolResult.CreateError(response.Error ?? "Tool execution failed.");
    }

    public bool ValidateInput(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return true;
        try
        {
            JsonSerializer.Deserialize<Dictionary<string, object?>>(input);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
#pragma warning restore CS1591
#pragma warning restore IDE1006
