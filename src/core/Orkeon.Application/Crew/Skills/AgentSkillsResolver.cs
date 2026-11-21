using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Decision.DTOs;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.FileSystem;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Application.Crew.Skills;

/// <summary>
/// The skills block of a task's system prompt (#434). A fixed list is read as written. An <c>auto</c> agent
/// has the decision model pick, from the one-line card of each skill of the library (never a body), at most
/// <see cref="MaxPicks"/> skills; with the decision circuit off (one warning per run), a failed call or an empty or
/// unreadable library (one warning per run), its fallback list stands, and with no fallback the prompt carries no skill.
/// Nothing here fails a task: a skill that cannot be read is left out with a warning. The block, its heading and
/// each skill's title line included, stays within <see cref="AgentSkillsOptions.MaxBlockChars"/>.
/// </summary>
internal sealed class AgentSkillsResolver
{
    /// <summary>The skills picked for one task at most, as in Orkeon Code (<c>SUGGEST_MAX_PICKS</c>).</summary>
    internal const int MaxPicks = 3;

    /// <summary>
    /// The share of the model's probability from which a skill is picked: the question is one choice, so
    /// skills that fit split the probability, and at most three can reach it (Orkeon Code's <c>SUGGEST_MIN_SHARE</c>).
    /// </summary>
    internal const double MinShare = 0.3;

    /// <summary>The skills offered in one question, in name order: past it the options crowd each other out.</summary>
    internal const int MaxCandidates = 24;

    /// <summary>What is read of a skill file to write its card.</summary>
    internal const int HeadChars = 2_000;

    /// <summary>The task, as the decision model reads it.</summary>
    internal const int RequestChars = 2_000;

    /// <summary>The name of the question, and the option that says no skill fits.</summary>
    internal const string QuestionName = "skill";

    /// <inheritdoc cref="QuestionName"/>
    internal const string NoSkillOption = "none";

    private readonly IFileSystemService _fileSystem;
    private readonly IServiceProvider _services;
    private readonly AgentSkillsOptions _options;
    private readonly ILogger _logger;
    private readonly RunWarnings _warnings = new();

    /// <param name="fileSystem">The file system the skills and the library are read through.</param>
    /// <param name="services">The container whose decision circuit and model an <c>auto</c> agent uses.</param>
    /// <param name="options">The library and the caps.</param>
    /// <param name="logger">Where the warnings go.</param>
    public AgentSkillsResolver(IFileSystemService fileSystem, IServiceProvider services, AgentSkillsOptions options, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _fileSystem = fileSystem;
        _services = services;
        _options = options;
        _logger = logger;
    }

    /// <summary>The skills block of the task, and what was chosen; null when the agent declares no skill.</summary>
    /// <param name="agent">The agent.</param>
    /// <param name="taskId">The task, for the notice.</param>
    /// <param name="request">The task as the decision model reads it (cut at <see cref="RequestChars"/>).</param>
    /// <param name="cancellationToken">Cancels the reads and the decision call.</param>
    public System.Threading.Tasks.Task<AgentSkillsResolution?> ResolveAsync(
        DomainAgent agent, string taskId, string request, CancellationToken cancellationToken) =>
        ResolveAsync(agent, taskId, request, scopeCircuit: null, cancellationToken);

    /// <summary>The skills block of the task, and what was chosen; null when the agent declares no skill.</summary>
    /// <param name="agent">The agent.</param>
    /// <param name="taskId">The task, for the notice.</param>
    /// <param name="request">The task as the decision model reads it (cut at <see cref="RequestChars"/>).</param>
    /// <param name="scopeCircuit">
    /// The circuit of the run's scope when it is not the container's (<see cref="DecisionCircuitOverride"/>, #431);
    /// null follows the container's.
    /// </param>
    /// <param name="cancellationToken">Cancels the reads and the decision call.</param>
    public async System.Threading.Tasks.Task<AgentSkillsResolution?> ResolveAsync(
        DomainAgent agent, string taskId, string request, DecisionCircuit? scopeCircuit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(agent);
        if (agent.Skills is not { } skills)
            return null;

        var choice = skills.Auto
            ? await PickAsync(agent, skills, request, scopeCircuit, cancellationToken).ConfigureAwait(false)
            : new Choice(AgentSkillsModes.List, skills.Paths, 0, null, null);
        var (block, kept, cut) = await ComposeAsync(agent, choice, cancellationToken).ConfigureAwait(false);
        var notice = new AgentSkillsNotice
        {
            TaskId = taskId,
            AgentRole = agent.Role.Value,
            Mode = choice.Mode,
            Skills = kept,
            Offered = choice.Offered,
            Cut = cut,
            Reason = choice.Reason,
            Detail = choice.Detail,
        };
        AgentSkillsLog.Chosen(_logger, agent.Role.Value, taskId, choice.Mode, kept.Count == 0 ? "none" : string.Join(", ", kept));
        return new AgentSkillsResolution { Block = block, Notice = notice };
    }

    private sealed record Choice(string Mode, IReadOnlyList<string> Paths, int Offered, string? Reason, string? Detail);

    private async System.Threading.Tasks.Task<Choice> PickAsync(
        DomainAgent agent, Domain.Agent.AgentSkills skills, string request, DecisionCircuit? scopeCircuit, CancellationToken cancellationToken)
    {
        var circuit = DecisionCircuit.Of(_services, scopeCircuit);
        var model = circuit.Enabled ? _services.GetService<IDecisionModel>() : null;
        if (model is null)
        {
            var reason = circuit.OffReason ?? DecisionCircuit.OffBy(DecisionCircuitCause.NoProvider).OffReason!;
            if (RunWarnings.Of(_warnings).First("skills.circuit-off"))
                AgentSkillsLog.CircuitOff(_logger, reason);
            return Fallback(skills, AgentSkillsReasons.CircuitOff, reason);
        }

        var (cards, fault) = await CardsAsync(cancellationToken).ConfigureAwait(false);
        if (cards.Count == 0)
        {
            var empty = fault is null
                ? $"the library {_options.Library} holds no skill"
                : $"the library {_options.Library} could not be read: {fault}";
            if (RunWarnings.Of(_warnings).First("skills.empty-library"))
                AgentSkillsLog.EmptyLibrary(_logger, empty);
            return Fallback(skills, AgentSkillsReasons.EmptyLibrary, empty);
        }

        DecisionResponse response;
        try
        {
            response = await model.DecideAsync(Question(cards, request), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            AgentSkillsLog.PickFailed(_logger, agent.Role.Value, ex.Message);
            return Fallback(skills, AgentSkillsReasons.PickFailed, $"the decision model failed: {ex.Message}");
        }

        // An answer that says nothing usable is no pick: the fallback stands, as when the call fails.
        if (Picks(response, cards) is not { } picks)
        {
            AgentSkillsLog.PickFailed(_logger, agent.Role.Value, SkillsPrompts.NoUsableChoice);
            return Fallback(skills, AgentSkillsReasons.PickFailed, $"the decision model's answer: {SkillsPrompts.NoUsableChoice}");
        }

        return new Choice(AgentSkillsModes.Auto, picks, cards.Count, null, null);
    }

    // What a read of the library or of a skill may throw: a path outside the mounts, a missing file or folder,
    // a denied access. Each is no skill, never a failed task.
    private static bool IsReadFault(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or FileAccessDeniedException or ArgumentException or InvalidOperationException;

    private static Choice Fallback(Domain.Agent.AgentSkills skills, string reason, string detail) =>
        new(AgentSkillsModes.Fallback, skills.Fallback, 0, reason, detail);

    /// <summary>One choice among the skills, numbered from 1, and the option that none fits.</summary>
    internal static DecisionRequest Question(IReadOnlyList<SkillCard> cards, string request)
    {
        var criteria = ImmutableDictionary.CreateBuilder<string, JsonNode?>(StringComparer.Ordinal);
        for (var i = 0; i < cards.Count; i++)
            criteria[Option(i)] = JsonValue.Create(cards[i].Line);
        criteria[NoSkillOption] = JsonValue.Create(SkillsPrompts.NoSkillFits);
        var state = request.Trim();
        return new DecisionRequest
        {
            State = JsonValue.Create(TextCut.Head(state, RequestChars))!,
            Questions = ImmutableDictionary<string, DecisionQuestion>.Empty.Add(QuestionName, new ChoiceQuestion
            {
                Instructions = JsonValue.Create(SkillsPrompts.QuestionInstructions),
                Criteria = criteria.ToImmutable(),
            }),
        };
    }

    /// <summary>
    /// The skills whose share reaches <see cref="MinShare"/>, the largest first, <see cref="MaxPicks"/> at most;
    /// none when the model chose that none fits. Null when the answer is no usable choice: missing, of another
    /// kind, an option that was not offered, or no share at all.
    /// </summary>
    internal static IReadOnlyList<string>? Picks(DecisionResponse response, IReadOnlyList<SkillCard> cards)
    {
        if (!response.Answers.TryGetValue(QuestionName, out var answer) || answer is not ChoiceAnswer choice)
            return null;
        if (choice.Choice == NoSkillOption)
            return [];
        var offered = Enumerable.Range(0, cards.Count).Select(Option).ToHashSet(StringComparer.Ordinal);
        if (!offered.Contains(choice.Choice) || !choice.Probabilities.Keys.Any(k => offered.Contains(k) || k == NoSkillOption))
            return null;

        return [.. cards
            .Select((card, i) => (card.Path, Share: choice.Probabilities.GetValueOrDefault(Option(i))))
            .Where(pick => pick.Share >= MinShare)
            .OrderByDescending(pick => pick.Share)
            .Take(MaxPicks)
            .Select(pick => pick.Path)];
    }

    private static string Option(int index) => "s" + (index + 1).ToString(CultureInfo.InvariantCulture);

    // The card of each markdown file of the library, in name order, MaxCandidates at most; an unreadable
    // file is no skill, and an unreadable library none, its fault given.
    private async System.Threading.Tasks.Task<(List<SkillCard> Cards, string? Fault)> CardsAsync(CancellationToken cancellationToken)
    {
        var paths = new List<string>();
        try
        {
            await foreach (var entry in _fileSystem.EnumerateFilesAsync(
                _options.Library, new VirtualEnumerationOptions(Recursive: false, SearchPattern: "*.md"), cancellationToken).ConfigureAwait(false))
            {
                if (entry.Kind == VirtualEntryKind.File)
                    paths.Add(entry.VirtualPath);
            }
        }
        catch (Exception ex) when (IsReadFault(ex))
        {
            return ([], ex.Message);
        }

        var cards = new List<SkillCard>();
        foreach (var path in paths.Order(StringComparer.Ordinal).Take(MaxCandidates))
        {
            var head = await ReadHeadAsync(path, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(head))
                cards.Add(SkillCard.Of(path, head));
        }
        return (cards, null);
    }

    private async System.Threading.Tasks.Task<string?> ReadHeadAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var stream = await _fileSystem.OpenReadStreamAsync(path, cancellationToken).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                using var reader = new StreamReader(stream, Encoding.UTF8);
                var buffer = new char[HeadChars];
                var read = await reader.ReadBlockAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                return new string(buffer, 0, read);
            }
        }
        catch (Exception ex) when (IsReadFault(ex))
        {
            return null;
        }
    }

    /// <summary>
    /// A skill's text, its leading white space skipped, read through the file system no further than
    /// <paramref name="cap"/> characters and one more: that one says the file goes on (white space aside). Null
    /// when the file cannot be read.
    /// </summary>
    private async System.Threading.Tasks.Task<(string Text, bool Longer)?> ReadSkillAsync(string path, int cap, CancellationToken cancellationToken)
    {
        try
        {
            var stream = await _fileSystem.OpenReadStreamAsync(path, cancellationToken).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4_096);
                var text = new StringBuilder();
                var buffer = new char[Math.Min(cap + 1, 4_096)];
                int read;
                while (text.Length <= cap
                    && (read = await reader.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, cap + 1 - text.Length)), cancellationToken).ConfigureAwait(false)) > 0)
                {
                    var chunk = buffer.AsSpan(0, read);
                    if (text.Length == 0)
                        chunk = chunk.TrimStart();
                    text.Append(chunk);
                }

                if (text.Length <= cap)
                    return (text.ToString().TrimEnd(), false);
                // Past the cap, never inside a surrogate pair: the file is longer unless all that follows is white space.
                var whole = text.ToString();
                var keep = TextCut.HeadLength(whole, cap);
                var longer = !whole.AsSpan(keep).IsWhiteSpace() || await RestIsTextAsync(reader, buffer, cancellationToken).ConfigureAwait(false);
                return longer ? (whole[..keep], true) : (whole[..keep].TrimEnd(), false);
            }
        }
        catch (Exception ex) when (IsReadFault(ex))
        {
            return null;
        }
    }

    // Whether what is left to read holds anything but white space, read a buffer at a time and stopped at the first.
    private static async System.Threading.Tasks.Task<bool> RestIsTextAsync(StreamReader reader, char[] buffer, CancellationToken cancellationToken)
    {
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (!buffer.AsSpan(0, read).IsWhiteSpace())
                return true;
        }
        return false;
    }

    // The block: each skill under its name and path, cut at MaxSkillChars and at what remains of MaxBlockChars,
    // which the heading, the intro, each title line and each cut note are charged against too. A skill with no
    // room left is left out, said in the notice's Cut alone.
    private async System.Threading.Tasks.Task<(string Block, ImmutableList<string> Kept, ImmutableList<string> Cut)> ComposeAsync(
        DomainAgent agent, Choice choice, CancellationToken cancellationToken)
    {
        var header = new StringBuilder()
            .AppendLine(SkillsPrompts.Heading)
            .AppendLine()
            .AppendLine(choice.Mode == AgentSkillsModes.Auto ? SkillsPrompts.PickIntro : SkillsPrompts.ListIntro)
            .ToString();
        var sections = new StringBuilder();
        var kept = ImmutableList.CreateBuilder<string>();
        var cut = ImmutableList.CreateBuilder<string>();
        var remaining = _options.MaxBlockChars - header.Length;
        var newLine = Environment.NewLine.Length;
        foreach (var path in choice.Paths.Distinct(StringComparer.Ordinal))
        {
            var read = await ReadSkillAsync(path, _options.MaxSkillChars, cancellationToken).ConfigureAwait(false);
            var text = read?.Text;
            if (string.IsNullOrEmpty(text))
            {
                AgentSkillsLog.SkillLeftOut(_logger, agent.Role.Value, path);
                cut.Add(path);
                continue;
            }

            var title = new StringBuilder().AppendLine().Append("### ").Append(SkillCard.FileName(path))
                .Append(" (").Append(path).AppendLine(")").ToString();
            // What the skill's text may take here, its line end included.
            var room = remaining - title.Length - newLine;
            var shown = Math.Min(text.Length, Math.Min(_options.MaxSkillChars, room));
            string? note = null;
            if (shown < text.Length || read!.Value.Longer)
            {
                // A cut text carries its note: room is kept for the longest it can be.
                var noteRoom = SkillsPrompts.CutNote(text.Length).Length + newLine;
                shown = TextCut.HeadLength(text, Math.Min(shown, room - noteRoom));
                note = SkillsPrompts.CutNote(shown);
            }

            if (shown <= 0)
            {
                cut.Add(path);
                continue;
            }

            remaining -= title.Length + shown + newLine + (note is null ? 0 : note.Length + newLine);
            kept.Add(path);
            sections.Append(title).AppendLine(text[..shown]);
            if (note is not null)
            {
                cut.Add(path);
                sections.AppendLine(note);
            }
        }

        if (kept.Count == 0)
            return (string.Empty, kept.ToImmutable(), cut.ToImmutable());

        return ((header + sections).TrimEnd(), kept.ToImmutable(), cut.ToImmutable());
    }
}

/// <summary>The skills block of a task's system prompt, and the notice of what was chosen (#434).</summary>
internal sealed record AgentSkillsResolution
{
    /// <summary>The block appended to the system prompt; empty when no skill is carried.</summary>
    public required string Block { get; init; }

    /// <summary>What was chosen, the fields of <c>agent.skills</c>.</summary>
    public required AgentSkillsNotice Notice { get; init; }
}
