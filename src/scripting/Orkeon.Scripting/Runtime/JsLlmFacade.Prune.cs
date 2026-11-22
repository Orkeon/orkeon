using System.Globalization;
using Jint;
using Jint.Native;
using Orkeon.Application.Crew.Compaction;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Scripting.Runtime;

/// <summary>
/// The <c>prune</c> option of <c>act</c> (#461): the orchestrator's pruning of older read-tool results
/// (<c>Orkeon:Compaction:Prune</c>), for the script's own loop. Before each model turn whose estimated prompt
/// reaches the fill share of the call's usable window, the decision model judges the results of the reads this
/// call ran, older than the last messages, from their call alone: kept, cut to their head, or dropped. Without
/// <c>prune</c> no decision call is made, whatever <c>Orkeon:Compaction:Prune:Enabled</c> says; an act is never
/// compacted.
/// </summary>
public sealed partial class JsLlmFacade
{
    private readonly IConversationPruner? _pruner;

    /// <summary>1 once the absence of a host pruning was said: once per facade, never per turn.</summary>
    private int _noPrunerWarned;

    private void WarnNoPrunerOnce()
    {
        if (Interlocked.Exchange(ref _noPrunerWarned, 1) == 0 && _logger is not null)
            LogNoPruner(_logger, _agentName);
    }

    [Microsoft.Extensions.Logging.LoggerMessage(
        EventId = 8, Level = Microsoft.Extensions.Logging.LogLevel.Warning,
        Message = "act: agent '{Agent}' asked for prune, but this host provides no pruner (IConversationPruner): its read results are kept whole")]
    static partial void LogNoPruner(Microsoft.Extensions.Logging.ILogger logger, string agent);

    /// <summary>The <c>prune</c> option of one call: null takes the <c>Orkeon:Compaction:Prune</c> number.</summary>
    /// <param name="FillShare">The fill share of the usable window from which the results are judged.</param>
    /// <param name="KeepRecentMessages">The last messages whose results are never offered.</param>
    /// <param name="HeadChars">The characters a head verdict keeps.</param>
    internal sealed record PruneSettings(double? FillShare, int? KeepRecentMessages, int? HeadChars)
    {
        private const string Option = "ctx.llm.act { prune }";

        /// <summary>
        /// Reads <c>options.prune</c>: absent, <c>false</c> or null asks nothing; <c>true</c> takes the configured
        /// numbers; an object overrides them, each within the bounds of <see cref="ToolResultPruningOptions.Problems"/>.
        /// Refused next to <c>onContextPressure</c> or <c>contextPressure</c>: the call prunes one way.
        /// </summary>
        public static PruneSettings? From(JsValue? options)
        {
            if (options is null || !options.IsObject())
                return null;
            var raw = options.Get("prune");
            if (raw.IsUndefined() || raw.IsNull() || (raw.IsBoolean() && !raw.AsBoolean()))
                return null;
            if (options.Get("onContextPressure") is Jint.Native.Function.Function)
                throw Refused("is refused next to onContextPressure: the call prunes one way, drop one of prune and onContextPressure.");
            if (!options.Get("contextPressure").IsUndefined())
                throw Refused("is refused next to contextPressure: the call prunes one way, drop one of prune and contextPressure.");
            if (raw.IsBoolean())
                return new PruneSettings(null, null, null);
            if (!raw.IsObject() || raw.IsArray())
                throw Refused("takes true or { fillShare?, keepRecentMessages?, headChars? }.");

            var settings = new PruneSettings(
                Number(raw, "fillShare"),
                Whole(raw, "keepRecentMessages"),
                Whole(raw, "headChars"));
            if (settings.FillShare is { } share && share is not (> 0 and <= 1))
                throw Refused($"fillShare is '{share.ToString(CultureInfo.InvariantCulture)}': write a number above 0 and at most 1, such as {ToolResultPruningOptions.DefaultFillShare.ToString(CultureInfo.InvariantCulture)}.");
            if (settings.KeepRecentMessages is < 0)
                throw Refused($"keepRecentMessages is '{settings.KeepRecentMessages.Value.ToString(CultureInfo.InvariantCulture)}': write 0 or more, such as {ToolResultPruningOptions.DefaultKeepRecentMessages.ToString(CultureInfo.InvariantCulture)}.");
            if (settings.HeadChars is < 1)
                throw Refused($"headChars is '{settings.HeadChars.Value.ToString(CultureInfo.InvariantCulture)}', which keeps nothing: write 1 or more, such as {ToolResultPruningOptions.DefaultHeadChars.ToString(CultureInfo.InvariantCulture)}.");
            return settings;
        }

        private static Orkeon.Scripting.Exceptions.InvalidScriptException Refused(string why) => new($"{Option} {why}");

        private static double? Number(JsValue settings, string name)
        {
            var value = settings.Get(name);
            if (value.IsUndefined() || value.IsNull())
                return null;
            if (!value.IsNumber() || double.IsNaN(value.AsNumber()))
                throw Refused($"{name} takes a number.");
            return value.AsNumber();
        }

        private static int? Whole(JsValue settings, string name)
        {
            var number = Number(settings, name);
            if (number is not { } value)
                return null;
            if (value != Math.Floor(value) || value > int.MaxValue || value < int.MinValue)
                throw Refused($"{name} takes a whole number.");
            return (int)value;
        }
    }

    /// <summary>
    /// What the pruning of one call judged and did, summed over its turns: the act's <c>result.pruned</c>.
    /// The lowercase members are what the script reads.
    /// </summary>
    internal sealed class JsPruneTally
    {
        public int offered { get; private set; }
        public int judged { get; private set; }
        public int kept { get; private set; }
        public int cut { get; private set; }
        public int dropped { get; private set; }
        public int freedTokens { get; private set; }

        /// <summary>Adds one turn's pruning.</summary>
        internal void Add(ConversationPruneResponse turn)
        {
            offered += turn.Offered;
            judged += turn.Judged;
            kept += turn.Kept;
            cut += turn.Cut;
            dropped += turn.Dropped;
            freedTokens += turn.FreedTokens;
        }
    }

    /// <summary>The state of the pruning of one call: its numbers, its view of the messages and its tally.</summary>
    internal sealed class ActPruning(PruneSettings settings, List<LlmMessage> messages, int firstIndex)
    {
        /// <summary>The call's numbers.</summary>
        public PruneSettings Settings { get; } = settings;

        /// <summary>The view the pruning reads and rewrites; it holds what was settled, so one per call.</summary>
        public ActConversationView View { get; } = new(messages);

        /// <summary>The first message whose result may be offered: the one after the prompt.</summary>
        public int FirstIndex { get; } = firstIndex;

        /// <summary>What was judged and done so far.</summary>
        public JsPruneTally Tally { get; } = new();

        /// <summary>The tally, once something was judged; null before.</summary>
        public JsPruneTally? Pruned => Tally.judged > 0 ? Tally : null;
    }

    /// <summary>
    /// Notes a read that ran, so the pruning may offer its result: by its position and its <c>call-N</c> id, unique
    /// over the call, since a provider's own call ids need not be.
    /// </summary>
    private static void NoteReadForPruning(ActSession session, ActToolRecord record)
    {
        if (session.Pruning is { } pruning)
            pruning.View.NoteReadCall(record.Index, record.Id, record.ToolName, record.Arguments);
    }

    /// <summary>
    /// Asks the host's pruning for this turn when the call set <c>prune</c>: the estimate of the messages, the
    /// prompt as the task, and the call's window. Nothing without <c>prune</c>; without a host pruning, nothing but one
    /// warning for the life of the facade.
    /// </summary>
    private async Task RelievePruneAsync(ActSession session)
    {
        if (session.Pruning is not { } pruning)
            return;
        if (_pruner is null)
        {
            WarnNoPrunerOnce();
            return;
        }
        var config = session.Config;
        var model = string.IsNullOrWhiteSpace(config.Model) ? session.Provider?.BaseConfig?.Model : config.Model;
        var request = new ConversationPruneRequest
        {
            // The agent's role, as a pick's agent.route names it (#460); its name when the facade knows no role.
            AgentRole = _picking?.AgentRole is { Length: > 0 } role ? role : _agentName,
            Task = session.Prompt,
            FirstIndex = pruning.FirstIndex,
            EstimatedTokens = EstimateTokens(session.Messages.Sum(MessageChars)),
            Profile = string.IsNullOrWhiteSpace(config.Profile) ? "default" : config.Profile,
            Model = string.IsNullOrWhiteSpace(model) ? null : model,
            ContextWindowTokens = config.ContextWindowTokens,
            MaxOutputTokens = config.MaxTokens,
            FillShare = pruning.Settings.FillShare,
            KeepRecentMessages = pruning.Settings.KeepRecentMessages,
            HeadChars = pruning.Settings.HeadChars,
        };
        if (await _pruner.PruneAsync(request, pruning.View, _ct).ConfigureAwait(false) is { } turn)
            pruning.Tally.Add(turn);
    }
}
