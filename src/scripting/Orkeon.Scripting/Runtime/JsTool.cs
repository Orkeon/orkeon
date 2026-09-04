using System.Text.Json;
using Jint;
using Jint.Native;
using Orkeon.Domain.Common;
using Orkeon.Domain.Tools;
using JsValueExt = Jint.JsValueExtensions;
using ToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;
using ToolCallResponse = Orkeon.Domain.Tools.Protocol.ToolCallResponse;
using ToolSchema = Orkeon.Domain.Tools.Protocol.ToolSchema;

namespace Orkeon.Scripting.Runtime;

/// <summary>
/// The tool produced by <c>toolBuilder()</c> — a first-class citizen of the tool
/// pipeline (EX-01): it implements <see cref="ITool"/> (the marker
/// <see cref="Orkeon.Domain.Common.ITool"/> the crew factory's strict resolution
/// requires), so a script-defined tool registered with the runtime registry is
/// callable by the LLM exactly like a C# tool.
/// </summary>
/// <remarks>
/// Two call paths, two threading realities:
/// <list type="bullet">
/// <item><description><b>From the script</b> (<c>myTool.execute(input)</c>): the caller IS the engine
/// thread, mid-evaluation. The lowercase <see cref="execute"/> member invokes the callback
/// re-entrantly and returns the raw result — a value or a Promise the script awaits itself,
/// settled by Jint's own event loop. No gate, no pump.</description></item>
/// <item><description><b>From the orchestrator</b> (<see cref="CallAsync"/>): the engine is at rest
/// (the script finished evaluating when the crew loaded), but a <c>process: parallel</c> crew
/// can fire several tool calls at once — the per-engine <see cref="JsEngineGate"/> serializes
/// them on the single-threaded engine. Promise results are settled with the same
/// <c>UnwrapIfPromise(TimeSpan)</c> pattern as <see cref="JsCrew"/>: Jint's
/// <c>UnwrapIfPromiseAsync</c> bakes in a 10 s ceiling, which an I/O-bound tool
/// (an <c>httpApi</c> call in its body) outlives routinely.</description></item>
/// </list>
/// </remarks>
#pragma warning disable IDE1006
#pragma warning disable CS1591
// CA1708: the lowercase name/description/execute members are the deliberate JS mirror
// (Jint member resolution is ordinal); the PascalCase members are the CLR contract.
#pragma warning disable CA1708
public sealed class JsTool : ITool
{
    public string Name { get; }
    public string Description { get; }
    public ToolSchema Schema { get; }
    public ToolAccess Access { get; }

    /// <summary>Whether <c>.withSchema(...)</c> was called on the builder.</summary>
    internal bool HasExplicitSchema { get; }

    /// <summary>Mirror of <see cref="JsCrew"/>'s body-promise ceiling, for the same reason.</summary>
    private static readonly TimeSpan ToolPromiseTimeout = TimeSpan.FromMinutes(30);

    private readonly Engine _engine;
    private readonly JsValue _executeCallback;

    internal JsTool(string name, string description, ToolSchema schema, bool hasExplicitSchema,
        Engine engine, JsValue executeCallback, ToolAccess access = ToolAccess.Unspecified)
    {
        Name = name;
        Description = description;
        Schema = schema;
        HasExplicitSchema = hasExplicitSchema;
        _engine = engine;
        _executeCallback = executeCallback;
        Access = access;
    }

    // ---- Script-facing members (lowercase: Jint member resolution is ordinal) ----

    public string name => Name;
    public string description => Description;

    /// <summary>
    /// Direct invocation from the script body: returns the callback's raw result — a
    /// value, or a Promise the caller awaits (Jint's event loop settles it). <paramref name="ctx"/>
    /// is passed through verbatim so a caller that has a context can forward it.
    /// </summary>
    public JsValue execute(JsValue input, JsValue? ctx = null) =>
        _engine.Invoke(_executeCallback, [input, ctx ?? JsValue.Undefined]);

    // ---- Pipeline-facing members ----

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Tool-execution fault barrier: any failure of the user-supplied JS execute callback (Jint JavaScriptException or CLR error) is converted to an unsuccessful ToolCallResponse carrying the message, so a buggy script tool cannot crash the host.")]
    public Task<ToolCallResponse> CallAsync(ToolCallRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return CallCoreAsync();

        async Task<ToolCallResponse> CallCoreAsync()
        {
            var gate = JsEngineGate.For(_engine);
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // Task.Run mirrors JsCrew.UnwrapPromise: the synchronous
                // UnwrapIfPromise(TimeSpan) pumps the engine's event loop, and
                // dispatching it off the caller preserves await semantics.
                return await Task.Run(() =>
                {
                    try
                    {
                        var input = JsValue.FromObject(_engine, request.Parameters);
                        var result = _engine.Invoke(_executeCallback, [input, JsValue.Undefined]);
                        var settled = result.IsPromise()
                            ? JsValueExt.UnwrapIfPromise(result, ToolPromiseTimeout)
                            : result;
                        return new ToolCallResponse(true, settled.ToObject(), null);
                    }
                    catch (Exception ex)
                    {
                        return new ToolCallResponse(false, null, ex.Message);
                    }
                }, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
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
#pragma warning restore CA1708
#pragma warning restore CS1591
#pragma warning restore IDE1006
