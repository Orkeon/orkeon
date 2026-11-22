using System.Collections.Immutable;
using System.Dynamic;
using Jint;
using Jint.Native;
using Orkeon.Application.Crew.Routing;
using Orkeon.Scripting.Exceptions;
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
    private static readonly string[] s_pickKeys = ["profile", "effort"];
    private static readonly string[] s_profileKeys = ["ladder", "floor", "ceiling", "fallback"];
    private static readonly string[] s_effortKeys = ["min", "max", "fallback"];

    private readonly JsLlmPicking? _picking;
    private JsValue? _pickJs;

    /// <summary><c>pick</c> as the script calls it: <see cref="PickAsync"/> behind the async bridge.</summary>
    public JsValue pick => _pickJs ??= JsHostError.BridgeAsync(_engine, PickAsync);

    /// <summary>
    /// The CLR side of <c>pick(request, { profile, effort })</c> (#460): System One's profile and thinking level for a
    /// script that runs no task, in one decision call. Every refusal throws an <see cref="InvalidScriptException"/>
    /// naming the option before any decision call; the call follows this context's token.
    /// </summary>
    internal Func<JsValue, JsValue?, Task<object>> PickAsync => async (request, options) =>
    {
        var asked = PickRequest(request, options);
        if (_picking?.Picker is not { } picker)
            throw new InvalidOperationException(
                "ctx.llm.pick: this host wires no System One pick (ISystemOnePicker); run the script under orkeon run, " +
                "orkeon-repl, orkeon-host or mcp serve --crew.");

        SystemOnePickResponse answer;
        try
        {
            using var usage = BeginUsageScope("pick");
            answer = await picker.PickAsync(asked, _ct).ConfigureAwait(false);
        }
        catch (ArgumentException ex) when (ex.ParamName is { } option)
        {
            var suffix = $" (Parameter '{option}')";
            var message = ex.Message.EndsWith(suffix, StringComparison.Ordinal) ? ex.Message[..^suffix.Length] : ex.Message;
            throw new InvalidScriptException($"ctx.llm.pick {{ {option} }} {message}", ex);
        }

        IDictionary<string, object?> result = new ExpandoObject();
        if (answer.Profile is { } profile)
            result["profile"] = PickAnswer(profile);
        if (answer.Effort is { } effort)
            result["effort"] = PickAnswer(effort);
        return result;
    };

    private SystemOnePickRequest PickRequest(JsValue request, JsValue? options)
    {
        if (!request.IsString())
            throw Refused("request", "takes the text the decision model reads, as a string.");
        if (options is null || !options.IsObject())
            throw Refused("options", "takes { profile?, effort? }: give one of them, or both.");
        RefuseUnknownKeys(options, s_pickKeys, "options");

        var profile = Bag(options, "profile") is { } p
            ? new ProfilePickRequest
            {
                Ladder = Names(p, "ladder"),
                Floor = Name(p, "floor", "profile.floor"),
                Ceiling = Name(p, "ceiling", "profile.ceiling"),
                Fallback = Name(p, "fallback", "profile.fallback"),
            }
            : null;
        var effort = Bag(options, "effort") is { } e
            ? new EffortPickRequest
            {
                Min = Name(e, "min", "effort.min"),
                Max = Name(e, "max", "effort.max"),
                Fallback = Name(e, "fallback", "effort.fallback"),
            }
            : null;
        if (profile is null && effort is null)
            throw Refused("options", "asks neither a profile nor an effort: give { profile } or { effort }, or both.");

        return new SystemOnePickRequest
        {
            Request = request.AsString(),
            AgentRole = _picking?.AgentRole ?? string.Empty,
            Profile = profile,
            Effort = effort,
        };
    }

    private static JsValue? Bag(JsValue options, string key)
    {
        var value = options.Get(key);
        if (value.IsUndefined() || value.IsNull())
            return null;
        if (!value.IsObject() || value is Jint.Native.Array.ArrayInstance)
            throw Refused(key, "takes an object.");
        RefuseUnknownKeys(value, key == "profile" ? s_profileKeys : s_effortKeys, key);
        return value;
    }

    private static string? Name(JsValue bag, string key, string option)
    {
        var value = bag.Get(key);
        if (value.IsUndefined() || value.IsNull())
            return null;
        if (!value.IsString())
            throw Refused(option, "takes a name, as a string.");
        return value.AsString();
    }

    private static ImmutableList<string>? Names(JsValue bag, string key)
    {
        var value = bag.Get(key);
        if (value.IsUndefined() || value.IsNull())
            return null;
        if (value is not Jint.Native.Array.ArrayInstance array)
            throw Refused("profile.ladder", "takes an array of profile names, cheapest first.");
        var length = (int)Jint.Runtime.TypeConverter.ToInteger(array.Get("length"));
        var names = ImmutableList.CreateBuilder<string>();
        for (var i = 0; i < length; i++)
        {
            var rung = array.Get(i.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (!rung.IsString())
                throw Refused("profile.ladder", $"takes profile names: rung {i} is not a string.");
            names.Add(rung.AsString());
        }

        return names.ToImmutable();
    }

    private static void RefuseUnknownKeys(JsValue bag, string[] known, string option)
    {
        foreach (var key in bag.AsObject().GetOwnPropertyKeys(Jint.Runtime.Types.String))
        {
            var name = key.ToString();
            if (!known.Contains(name, StringComparer.Ordinal))
                throw Refused(option, $"has no option '{name}': it takes {string.Join(", ", known)}.");
        }
    }

    private static InvalidScriptException Refused(string option, string message) =>
        new($"ctx.llm.pick {{ {option} }} {message}");

    private static IDictionary<string, object?> PickAnswer(SystemOnePickAnswer answer)
    {
        IDictionary<string, object?> bag = new ExpandoObject();
        bag["value"] = answer.Value;
        bag["reason"] = answer.Reason;
        bag["picked"] = answer.Picked;
        bag["probability"] = answer.Probability;
        return bag;
    }
}
