using Orkeon.Application.Interfaces.Ports;
using Orkeon.Cli.Commands.Scripting.Dispatch;

namespace Orkeon.Cli.Commands.Scripting.Runtime;

#pragma warning disable IDE1006 // camelCase members: these CLR types are exposed to scripts as ctx.services.get("usage")

/// <summary>
/// The session's LLM usage, as a command reads it: <c>ctx.services.get("usage")</c>. Read-only. Each
/// <see cref="report"/> reads the figures of that moment: the session's totals and each model from the
/// session's <see cref="ICostBudgetManager"/> (the totals over every call, a call whose provider named no model
/// included, so they may exceed the sum of the model rows), each command instance (a crew launched with <c>runCrewAsync</c>
/// included) from what <see cref="Progress.InstanceAttributingUsageSink"/> credited it.
/// </summary>
public sealed class ScriptSessionUsage
{
    private readonly ICostBudgetManager? _costManager;
    private readonly CommandInstanceRegistry? _instances;

    internal ScriptSessionUsage(ICostBudgetManager? costManager, CommandInstanceRegistry? instances)
    {
        _costManager = costManager;
        _instances = instances;
    }

    /// <summary>The session's usage now: totals, by model, by command instance.</summary>
    public ScriptUsageReport report()
    {
        var session = _costManager?.GetReport();
        var models = session?.ModelUsageByModel
            .OrderBy(m => m.Key, StringComparer.Ordinal)
            .Select(m => ScriptModelUsage.From(m.Key, m.Value))
            .ToArray() ?? [];
        var commands = _instances?.All()
            .OrderByDescending(i => i.StartedAt)
            .Select(ScriptCommandUsage.From)
            .ToArray() ?? [];
        // Every recorded call, the ones whose provider named no model included: never the sum of the model rows.
        var total = session?.TotalUsage ?? new ModelUsage();
        var totalCost = session?.TotalCost ?? 0m;

        return new ScriptUsageReport
        {
            promptTokens = total.PromptTokens,
            completionTokens = total.CompletionTokens,
            cacheHitTokens = total.CacheReadTokens,
            cacheWriteTokens = total.CacheWriteTokens,
            reasoningTokens = total.ReasoningTokens,
            totalTokens = total.PromptTokens + total.CompletionTokens,
            calls = session?.TotalCalls ?? 0,
            unpricedCalls = total.UnpricedCalls,
            cost = total.Priced || totalCost != 0m ? (double)totalCost : null,
            byModel = models,
            byCommand = commands,
        };
    }
}

/// <summary>The session's usage, as <see cref="ScriptSessionUsage.report"/> returns it.</summary>
public sealed class ScriptUsageReport
{
    /// <summary>Prompt (input) tokens of the session.</summary>
    public long promptTokens { get; init; }

    /// <summary>Completion (output) tokens of the session.</summary>
    public long completionTokens { get; init; }

    /// <summary>Prompt tokens served from a provider's cache, a part of <see cref="promptTokens"/>.</summary>
    public long cacheHitTokens { get; init; }

    /// <summary>Prompt tokens written to a provider's cache, a part of <see cref="promptTokens"/>.</summary>
    public long cacheWriteTokens { get; init; }

    /// <summary>Completion tokens spent reasoning, a part of <see cref="completionTokens"/>.</summary>
    public long reasoningTokens { get; init; }

    /// <summary><see cref="promptTokens"/> + <see cref="completionTokens"/>.</summary>
    public long totalTokens { get; init; }

    /// <summary>The LLM calls of the session.</summary>
    public int calls { get; init; }

    /// <summary>The calls nobody priced: <see cref="cost"/> counts them as 0, so it is a lower bound while this is not 0.</summary>
    public int unpricedCalls { get; init; }

    /// <summary>The session's cost; null when no price is known for any call it made.</summary>
    public double? cost { get; init; }

    /// <summary>
    /// One row per model, by name. A call whose provider named no model has no row: the session's totals count it,
    /// so they may exceed the sum of these rows.
    /// </summary>
    public required IReadOnlyList<ScriptModelUsage> byModel { get; init; }

    /// <summary>One row per command instance the session keeps, the newest first.</summary>
    public required IReadOnlyList<ScriptCommandUsage> byCommand { get; init; }
}

/// <summary>The usage of one model in <see cref="ScriptUsageReport.byModel"/>.</summary>
public sealed class ScriptModelUsage
{
    /// <summary>The model, as the provider named it in its answer.</summary>
    public required string model { get; init; }

    /// <summary>Prompt (input) tokens.</summary>
    public long promptTokens { get; init; }

    /// <summary>Completion (output) tokens.</summary>
    public long completionTokens { get; init; }

    /// <summary>Prompt tokens served from the provider's cache.</summary>
    public long cacheHitTokens { get; init; }

    /// <summary>Prompt tokens written to the provider's cache.</summary>
    public long cacheWriteTokens { get; init; }

    /// <summary>Completion tokens spent reasoning.</summary>
    public long reasoningTokens { get; init; }

    /// <summary>The LLM calls of the model.</summary>
    public int calls { get; init; }

    /// <summary>The calls nobody priced: <see cref="cost"/> counts them as 0.</summary>
    public int unpricedCalls { get; init; }

    /// <summary>The model's cost; null when the vendor billed nothing and no price is configured for it.</summary>
    public double? cost { get; init; }

    internal static ScriptModelUsage From(string model, ModelUsage usage) => new()
    {
        calls = usage.Calls,
        unpricedCalls = usage.UnpricedCalls,
        model = model,
        promptTokens = usage.PromptTokens,
        completionTokens = usage.CompletionTokens,
        cacheHitTokens = usage.CacheReadTokens,
        cacheWriteTokens = usage.CacheWriteTokens,
        reasoningTokens = usage.ReasoningTokens,
        cost = usage.Priced ? (double)usage.Cost : null,
    };
}

/// <summary>The usage of one command instance in <see cref="ScriptUsageReport.byCommand"/>.</summary>
public sealed class ScriptCommandUsage
{
    /// <summary>The instance's ticket (<c>t1</c>).</summary>
    public required string ticket { get; init; }

    /// <summary>The command's name (<c>dev-batches</c>).</summary>
    public required string name { get; init; }

    /// <summary>The instance's state (<c>running</c>, <c>completed</c>...).</summary>
    public required string state { get; init; }

    /// <summary>Prompt (input) tokens.</summary>
    public long promptTokens { get; init; }

    /// <summary>Completion (output) tokens.</summary>
    public long completionTokens { get; init; }

    /// <summary>Prompt tokens served from the provider's cache.</summary>
    public long cacheHitTokens { get; init; }

    /// <summary>Prompt tokens written to the provider's cache.</summary>
    public long cacheWriteTokens { get; init; }

    /// <summary>Completion tokens spent reasoning.</summary>
    public long reasoningTokens { get; init; }

    /// <summary>The LLM calls of the instance.</summary>
    public int calls { get; init; }

    /// <summary>The calls nobody priced: <see cref="cost"/> counts them as 0.</summary>
    public int unpricedCalls { get; init; }

    /// <summary>The instance's cost; null while none of its calls was priced.</summary>
    public double? cost { get; init; }

    internal static ScriptCommandUsage From(CommandInstance instance)
    {
        var usage = instance.Usage;
        return new ScriptCommandUsage
        {
            ticket = instance.Ticket,
            name = instance.Name,
            state = instance.State.ToToken(),
            promptTokens = usage.PromptTokens,
            completionTokens = usage.CompletionTokens,
            cacheHitTokens = usage.CacheHitTokens,
            cacheWriteTokens = usage.CacheWriteTokens,
            reasoningTokens = usage.ReasoningTokens,
            calls = usage.Calls,
            unpricedCalls = usage.UnpricedCalls,
            cost = usage.Cost is { } cost ? (double)cost : null,
        };
    }
}
#pragma warning restore IDE1006
