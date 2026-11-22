using Jint.Native;
using Orkeon.Application.Crew.Routing;
using Orkeon.Scripting.Internal;

namespace Orkeon.Scripting.Runtime;

/// <summary>What <c>ctx.llm.pick</c> asks through (#460): the host's System One pick, and the agent it picks for.</summary>
/// <param name="Picker">The host's pick; null when the host wires none.</param>
/// <param name="AgentRole">The role of the script's agent, as the decision model reads it.</param>
internal sealed record JsLlmPicking(ISystemOnePicker? Picker, string AgentRole);

#pragma warning disable IDE1006
#pragma warning disable CS1591 // JS-interop mirror of LlmFacade.pick in Typings/context.d.ts; that declaration is the contract scripts read.
public sealed partial class JsLlmFacade
{
    private const string PickPrefix = "ctx.llm.pick";

    private readonly JsLlmPicking? _picking;
    private JsValue? _pickJs;

    /// <summary><c>pick</c> as the script calls it: <see cref="PickAsync"/> behind the async bridge.</summary>
    [System.Diagnostics.CodeAnalysis.Experimental("ORKEXP008", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
    public JsValue pick => _pickJs ??= JsHostError.BridgeAsync(_engine, PickAsync);

    /// <summary>
    /// The CLR side of <c>pick(request, { profile, effort })</c> (#460): System One's profile and thinking level for a
    /// script that runs no task, in one decision call. Every refusal throws an <see cref="Exceptions.InvalidScriptException"/>
    /// naming the option before any decision call; the call follows this context's token.
    /// </summary>
    internal Func<JsValue, JsValue?, Task<object>> PickAsync => async (request, options) =>
    {
        var asked = ScriptPick.Request(request, options, _picking?.AgentRole ?? string.Empty, PickPrefix);
        if (_picking?.Picker is not { } picker)
            throw new InvalidOperationException(
                "ctx.llm.pick: this host wires no System One pick (ISystemOnePicker); run the script under orkeon run, " +
                "orkeon-repl, orkeon-host or mcp serve --crew.");

        using var usage = BeginUsageScope("pick");
        return await ScriptPick.AskAsync(picker, asked, PickPrefix, _ct).ConfigureAwait(false);
    };
}
