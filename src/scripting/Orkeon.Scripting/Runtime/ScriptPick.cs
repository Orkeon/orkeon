using System.Collections.Immutable;
using System.Dynamic;
using Jint;
using Jint.Native;
using Orkeon.Application.Crew.Routing;
using Orkeon.Scripting.Exceptions;

namespace Orkeon.Scripting.Runtime;

/// <summary>
/// The script side of a System One pick (#460, #463), shared by <c>ctx.llm.pick</c> and a command's
/// <c>ctx.services.get("pick").pick</c>: the request and its options read from the script's values, refused
/// before any decision call with a message that names the option after <c>prefix</c>, and the answer as the
/// script reads it. Everything else (the clamps, the ladder, the answers that need no call, the circuit) is the
/// host's <see cref="ISystemOnePicker"/>.
/// </summary>
internal static class ScriptPick
{
    private static readonly string[] s_pickKeys = ["profile", "effort"];
    private static readonly string[] s_profileKeys = ["ladder", "floor", "ceiling", "fallback"];
    private static readonly string[] s_effortKeys = ["min", "max", "fallback"];

    /// <summary>Asks <paramref name="picker"/> and returns the answer as the script reads it.</summary>
    /// <param name="picker">The host's pick.</param>
    /// <param name="asked">The request, read by <see cref="Request"/>.</param>
    /// <param name="prefix">What the script called, at the head of every refusal (<c>ctx.llm.pick</c>).</param>
    /// <param name="ct">The token the call follows.</param>
    /// <returns><c>{ profile?, effort? }</c>, each <c>{ value, reason, picked, probability }</c>.</returns>
    public static async Task<object> AskAsync(ISystemOnePicker picker, SystemOnePickRequest asked, string prefix, CancellationToken ct)
    {
        SystemOnePickResponse answer;
        try
        {
            answer = await picker.PickAsync(asked, ct).ConfigureAwait(false);
        }
        catch (ArgumentException ex) when (ex.ParamName is { } option)
        {
            var suffix = $" (Parameter '{option}')";
            var message = ex.Message.EndsWith(suffix, StringComparison.Ordinal) ? ex.Message[..^suffix.Length] : ex.Message;
            throw new InvalidScriptException($"{prefix} {{ {option} }} {message}", ex);
        }

        IDictionary<string, object?> result = new ExpandoObject();
        if (answer.Profile is { } profile)
            result["profile"] = Answer(profile);
        if (answer.Effort is { } effort)
            result["effort"] = Answer(effort);
        return result;
    }

    /// <summary>Reads <c>pick(request, { profile?, effort? })</c>; throws an <see cref="InvalidScriptException"/> naming the option.</summary>
    /// <param name="request">The text the decision model reads.</param>
    /// <param name="options">The options object.</param>
    /// <param name="agentRole">The role the decision model reads; empty when no agent asks.</param>
    /// <param name="prefix">What the script called, at the head of every refusal.</param>
    /// <returns>The request the picker takes.</returns>
    public static SystemOnePickRequest Request(JsValue request, JsValue? options, string agentRole, string prefix)
    {
        if (!request.IsString())
            throw Refused(prefix, "request", "takes the text the decision model reads, as a string.");
        if (options is null || !options.IsObject())
            throw Refused(prefix, "options", "takes { profile?, effort? }: give one of them, or both.");
        RefuseUnknownKeys(options, s_pickKeys, "options", prefix);

        var profile = Bag(options, "profile", prefix) is { } p
            ? new ProfilePickRequest
            {
                Ladder = Names(p, "ladder", prefix),
                Floor = Name(p, "floor", "profile.floor", prefix),
                Ceiling = Name(p, "ceiling", "profile.ceiling", prefix),
                Fallback = Name(p, "fallback", "profile.fallback", prefix),
            }
            : null;
        var effort = Bag(options, "effort", prefix) is { } e
            ? new EffortPickRequest
            {
                Min = Name(e, "min", "effort.min", prefix),
                Max = Name(e, "max", "effort.max", prefix),
                Fallback = Name(e, "fallback", "effort.fallback", prefix),
            }
            : null;
        if (profile is null && effort is null)
            throw Refused(prefix, "options", "asks neither a profile nor an effort: give { profile } or { effort }, or both.");

        return new SystemOnePickRequest
        {
            Request = request.AsString(),
            AgentRole = agentRole,
            Profile = profile,
            Effort = effort,
        };
    }

    private static JsValue? Bag(JsValue options, string key, string prefix)
    {
        var value = options.Get(key);
        if (value.IsUndefined() || value.IsNull())
            return null;
        if (!value.IsObject() || value is Jint.Native.Array.ArrayInstance)
            throw Refused(prefix, key, "takes an object.");
        RefuseUnknownKeys(value, key == "profile" ? s_profileKeys : s_effortKeys, key, prefix);
        return value;
    }

    private static string? Name(JsValue bag, string key, string option, string prefix)
    {
        var value = bag.Get(key);
        if (value.IsUndefined() || value.IsNull())
            return null;
        if (!value.IsString())
            throw Refused(prefix, option, "takes a name, as a string.");
        return value.AsString();
    }

    private static ImmutableList<string>? Names(JsValue bag, string key, string prefix)
    {
        var value = bag.Get(key);
        if (value.IsUndefined() || value.IsNull())
            return null;
        if (value is not Jint.Native.Array.ArrayInstance array)
            throw Refused(prefix, "profile.ladder", "takes an array of profile names, cheapest first.");
        var length = (int)Jint.Runtime.TypeConverter.ToInteger(array.Get("length"));
        var names = ImmutableList.CreateBuilder<string>();
        for (var i = 0; i < length; i++)
        {
            var rung = array.Get(i.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (!rung.IsString())
                throw Refused(prefix, "profile.ladder", $"takes profile names: rung {i} is not a string.");
            names.Add(rung.AsString());
        }

        return names.ToImmutable();
    }

    private static void RefuseUnknownKeys(JsValue bag, string[] known, string option, string prefix)
    {
        foreach (var key in bag.AsObject().GetOwnPropertyKeys(Jint.Runtime.Types.String))
        {
            var name = key.ToString();
            if (!known.Contains(name, StringComparer.Ordinal))
                throw Refused(prefix, option, $"has no option '{name}': it takes {string.Join(", ", known)}.");
        }
    }

    private static InvalidScriptException Refused(string prefix, string option, string message) =>
        new($"{prefix} {{ {option} }} {message}");

    private static IDictionary<string, object?> Answer(SystemOnePickAnswer answer)
    {
        IDictionary<string, object?> bag = new ExpandoObject();
        bag["value"] = answer.Value;
        bag["reason"] = answer.Reason;
        bag["picked"] = answer.Picked;
        bag["probability"] = answer.Probability;
        return bag;
    }
}
