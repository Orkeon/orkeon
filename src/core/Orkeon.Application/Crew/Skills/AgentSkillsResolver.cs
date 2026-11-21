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
/// <see cref="MaxPicks"/> skills; with the decision circuit off (one warning), a failed call or an empty
/// library, its fallback list stands, and with no fallback the prompt carries no skill. Nothing here fails a
/// task: a skill that cannot be read is left out with a warning.
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
    private int _offWarned;

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
            : new Choice(AgentSkillsModes.List, skills.Paths, 0, null);
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
        };
        AgentSkillsLog.Chosen(_logger, agent.Role.Value, taskId, choice.Mode, kept.Count == 0 ? "none" : string.Join(", ", kept));
        return new AgentSkillsResolution { Block = block, Notice = notice };
    }

    private sealed record Choice(string Mode, IReadOnlyList<string> Paths, int Offered, string? Reason);

    private async System.Threading.Tasks.Task<Choice> PickAsync(
        DomainAgent agent, Domain.Agent.AgentSkills skills, string request, DecisionCircuit? scopeCircuit, CancellationToken cancellationToken)
    {
        var circuit = DecisionCircuit.Of(_services, scopeCircuit);
        var model = circuit.Enabled ? _services.GetService<IDecisionModel>() : null;
        if (model is null)
        {
            var reason = circuit.OffReason ?? DecisionCircuit.OffBy(DecisionCircuitCause.NoProvider).OffReason!;
            if (Interlocked.Exchange(ref _offWarned, 1) == 0)
                AgentSkillsLog.CircuitOff(_logger, reason);
            return Fallback(skills, reason);
        }

        var cards = await CardsAsync(cancellationToken).ConfigureAwait(false);
        if (cards.Count == 0)
            return Fallback(skills, $"the library {_options.Library} holds no skill");

        DecisionResponse response;
        try
        {
            response = await model.DecideAsync(Question(cards, request), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            AgentSkillsLog.PickFailed(_logger, agent.Role.Value, ex.Message);
            return Fallback(skills, $"the decision model failed: {ex.Message}");
        }

        return new Choice(AgentSkillsModes.Auto, Picks(response, cards), cards.Count, null);
    }

    // What a read of the library or of a skill may throw: a path outside the mounts, a missing file or folder,
    // a denied access. Each is no skill, never a failed task.
    private static bool IsReadFault(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or FileAccessDeniedException or ArgumentException or InvalidOperationException;

    private static Choice Fallback(Domain.Agent.AgentSkills skills, string reason) =>
        new(AgentSkillsModes.Fallback, skills.Fallback, 0, reason);

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
            State = JsonValue.Create(state.Length <= RequestChars ? state : state[..RequestChars])!,
            Questions = ImmutableDictionary<string, DecisionQuestion>.Empty.Add(QuestionName, new ChoiceQuestion
            {
                Instructions = JsonValue.Create(SkillsPrompts.QuestionInstructions),
                Criteria = criteria.ToImmutable(),
            }),
        };
    }

    /// <summary>
    /// The skills whose share reaches <see cref="MinShare"/>, the largest first, <see cref="MaxPicks"/> at most;
    /// none when the model chose that none fits or gave no choice.
    /// </summary>
    internal static IReadOnlyList<string> Picks(DecisionResponse response, IReadOnlyList<SkillCard> cards)
    {
        if (!response.Answers.TryGetValue(QuestionName, out var answer) || answer is not ChoiceAnswer choice
            || choice.Choice == NoSkillOption)
            return [];

        return [.. cards
            .Select((card, i) => (card.Path, Share: choice.Probabilities.GetValueOrDefault(Option(i))))
            .Where(pick => pick.Share >= MinShare)
            .OrderByDescending(pick => pick.Share)
            .Take(MaxPicks)
            .Select(pick => pick.Path)];
    }

    private static string Option(int index) => "s" + (index + 1).ToString(CultureInfo.InvariantCulture);

    // The card of each markdown file of the library, in name order, MaxCandidates at most; an unreadable
    // library or file is no skill.
    private async System.Threading.Tasks.Task<List<SkillCard>> CardsAsync(CancellationToken cancellationToken)
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
            return [];
        }

        var cards = new List<SkillCard>();
        foreach (var path in paths.Order(StringComparer.Ordinal).Take(MaxCandidates))
        {
            var head = await ReadHeadAsync(path, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(head))
                cards.Add(SkillCard.Of(path, head));
        }
        return cards;
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

    private async System.Threading.Tasks.Task<string?> ReadAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await _fileSystem.TryReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsReadFault(ex))
        {
            return null;
        }
    }

    // The block: each skill under its name and path, cut at MaxSkillChars and at what remains of MaxBlockChars.
    private async System.Threading.Tasks.Task<(string Block, ImmutableList<string> Kept, ImmutableList<string> Cut)> ComposeAsync(
        DomainAgent agent, Choice choice, CancellationToken cancellationToken)
    {
        var sections = new StringBuilder();
        var kept = ImmutableList.CreateBuilder<string>();
        var cut = ImmutableList.CreateBuilder<string>();
        var remaining = _options.MaxBlockChars;
        foreach (var path in choice.Paths.Distinct(StringComparer.Ordinal))
        {
            var text = (await ReadAsync(path, cancellationToken).ConfigureAwait(false))?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                AgentSkillsLog.SkillLeftOut(_logger, agent.Role.Value, path);
                cut.Add(path);
                continue;
            }

            if (remaining <= 0)
            {
                cut.Add(path);
                continue;
            }

            var shown = Math.Min(text.Length, Math.Min(_options.MaxSkillChars, remaining));
            remaining -= shown;
            kept.Add(path);
            sections.AppendLine().Append("### ").Append(SkillCard.FileName(path)).Append(" (").Append(path).AppendLine(")");
            sections.AppendLine(text[..shown]);
            if (shown < text.Length)
            {
                cut.Add(path);
                sections.AppendLine(SkillsPrompts.CutNote(shown, text.Length));
            }
        }

        if (kept.Count == 0)
            return (string.Empty, kept.ToImmutable(), cut.ToImmutable());

        var block = new StringBuilder()
            .AppendLine(SkillsPrompts.Heading)
            .AppendLine()
            .AppendLine(choice.Mode == AgentSkillsModes.Auto ? SkillsPrompts.PickIntro : SkillsPrompts.ListIntro)
            .Append(sections);
        return (block.ToString().TrimEnd(), kept.ToImmutable(), cut.ToImmutable());
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
