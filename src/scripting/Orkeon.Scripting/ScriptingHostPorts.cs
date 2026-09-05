namespace Orkeon.Scripting;

/// <summary>
/// The host-side ports a scripting engine hands to its bindings: the gate consulted
/// before a scripted tool call runs, plus the two sinks the <c>ctx.llm</c> surface
/// reports to (streamed deltas, per-call usage). Bundled so
/// <see cref="JsEngineFactory"/> keeps a readable constructor; every member stays
/// individually optional and a null bundle simply means the host wired none of them.
/// </summary>
public sealed record ScriptingHostPorts
{
    /// <summary>
    /// Per-tool-call permission gate consulted by <c>ctx.llm.act</c> before each tool
    /// execution. Null = ungated.
    /// </summary>
    public Orkeon.Application.Interfaces.Security.IPermissionGate? PermissionGate { get; init; }

    /// <summary>
    /// Host-native renderer for streamed <c>ctx.llm.act</c> deltas. Null = buffered
    /// behaviour unless the script passes <c>onDelta</c>.
    /// </summary>
    public Orkeon.Application.Interfaces.Ports.ILlmDeltaSink? DeltaSink { get; init; }

    /// <summary>
    /// Host-native receiver for per-call LLM usage events. Null = usage not observed.
    /// </summary>
    public Orkeon.Application.Interfaces.Ports.ILlmUsageSink? UsageSink { get; init; }
}
