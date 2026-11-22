using System.Diagnostics.CodeAnalysis;
using Jint;
using Jint.Native;
using Orkeon.Application.Crew.Routing;
using Orkeon.Scripting.Internal;
using Orkeon.Scripting.Runtime;

namespace Orkeon.Cli.Commands.Scripting.Runtime;

#pragma warning disable IDE1006 // camelCase member: this CLR type is exposed to scripts as ctx.services.get("pick")

/// <summary>
/// System One's pick for a command (#463): <c>ctx.services.get("pick").pick(request, options)</c>, the contract of
/// <c>ctx.llm.pick</c> (#460) for a <c>*.cmd.ts</c>, which has no <c>ctx.llm</c>. The same request and options, the
/// same refusals (named after <c>services.get("pick").pick</c>, an <c>InvalidScriptError</c> before any decision
/// call), the same answer, in one decision call that follows the command's <c>ctx.signal</c>. The call is metered like
/// the command's other LLM calls: a <c>decision</c> usage event to the host's usage sink, which reaches the session's
/// total (and a dispatched instance's row when the command runs as one). Served when the host wires an
/// <see cref="ISystemOnePicker"/>, from the scope the script engines read their scoped ports from. The service is
/// bound to the run that got it: a script gets it inside the handler, never once at module scope.
/// </summary>
[Experimental("ORKEXP008", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public sealed class PickService
{
    private const string Prefix = "services.get(\"pick\").pick";

    private readonly ISystemOnePicker _picker;
    private readonly CancellationToken _ct;

    private PickService(ISystemOnePicker picker, Engine engine, CancellationToken ct)
    {
        _picker = picker;
        _ct = ct;
        pick = JsHostError.BridgeAsync(engine, PickAsync);
    }

    /// <summary>
    /// <c>pick(request, { profile?, effort? })</c>: a profile, a thinking level, or both, as <c>ctx.llm.pick</c>
    /// answers them (<c>{ profile?, effort? }</c>, each <c>{ value, reason, picked, probability }</c>).
    /// </summary>
    public JsValue pick { get; }

    internal Func<JsValue, JsValue?, Task<object>> PickAsync => async (request, options) =>
    {
        var asked = ScriptPick.Request(request, options, agentRole: string.Empty, Prefix);
        return await ScriptPick.AskAsync(_picker, asked, Prefix, _ct).ConfigureAwait(false);
    };

    /// <summary>What the whitelist registers: the host's pick, bound to each command run that asks for it.</summary>
    internal sealed class Registration(ISystemOnePicker picker) : IScriptEngineBoundService
    {
        public object Bind(Engine engine, CancellationToken ct) => new PickService(picker, engine, ct);
    }
}
