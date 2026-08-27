using Orkeon.Studio.Core.Events;
using Orkeon.Studio.Core.FileSystem;
using System.Text.Json;

namespace Orkeon.Studio.Core.Forge;

/// <summary>One bubble of the conversation.</summary>
public sealed record ForgeChatMessage(string Role, string Text)
{
    /// <summary>The assistant's role name.</summary>
    public const string Assistant = "assistant";

    /// <summary>The user's role name.</summary>
    public const string User = "user";
}

/// <summary>One line of the "success" card — an acceptance criterion in the user's words.</summary>
public sealed record ForgeCriterion(string Id, string Statement, bool Must);

/// <summary>One numbered step of the narrative proposal (a task, told as an action).</summary>
public sealed record ForgeProposalStep(string Description, string? AgentRole);

/// <summary>One agent of the blueprint, as the Composer step shows and edits it.</summary>
public sealed record ForgeAgentView(
    string Key,
    string Role,
    string? Goal,
    string? Backstory,
    IReadOnlyList<string> Tools);

/// <summary>The proposal card: steps, the plain-words rationale, the agents, and what the team may touch.</summary>
public sealed record ForgeProposal(
    IReadOnlyList<ForgeProposalStep> Steps,
    string? Rationale,
    IReadOnlyList<string> Tools,
    IReadOnlyList<ForgeAgentView> Agents);

/// <summary>
/// A mount the agents themselves imply (v3 W-04): the write side comes from the tasks'
/// deliverable roots, the read side from the reading tools — exactly what the trial
/// sandbox mounts. Informative, not removable: editing an agent is what changes it.
/// </summary>
public sealed record ForgeDerivedMount(string VirtualPath, bool IsReadWrite);

/// <summary>One completed task of the running try.</summary>
public sealed record ForgeTaskProgress(string? TaskId, string? AgentRole, bool Success, long DurationMs);

/// <summary>One finding of the verdict, as the checklist will show it.</summary>
public sealed record ForgeFindingView(string? Id, string Severity, string? Acceptance, string Statement);

/// <summary>One suggestion of the verdict.</summary>
public sealed record ForgeSuggestionView(string? Target, string? Change, string? Reason);

/// <summary>The diagnosis, as <c>verdict.ready</c> carried it.</summary>
public sealed record ForgeVerdictView(
    double Score,
    bool Passing,
    string Judge,
    IReadOnlyList<ForgeFindingView> Findings,
    IReadOnlyList<ForgeSuggestionView> Suggestions)
{
    /// <summary>Wire spelling of the LLM judge.</summary>
    public const string JudgeLlm = "llm";

    /// <summary>Wall time of the last trial, milliseconds; null when unmeasured (W-08).</summary>
    public long? DurationMs { get; init; }

    /// <summary>Tokens the last trial consumed; null when unmeasured (W-08).</summary>
    public long? Tokens { get; init; }

    /// <summary>Cache-served prompt tokens of the last trial — a partition, never additive.</summary>
    public long? CacheHitTokens { get; init; }

    /// <summary>Cache-missed prompt tokens of the last trial.</summary>
    public long? CacheMissTokens { get; init; }
}

/// <summary>
/// One line of the ✔/✘ checklist: a criterion's statement, whether it held — null when the
/// judge was unavailable and honesty demands "judge for yourself" — and the finding's
/// wording when it did not.
/// </summary>
public sealed record ForgeChecklistItem(string Statement, bool? Passed, string? Detail);

/// <summary>What a promotion produced.</summary>
public sealed record ForgePromotion(string Path, string Launcher, string? Install);

/// <summary>An <c>error</c> event.</summary>
public sealed record ForgeErrorInfo(string Code, string Message, bool Recoverable);

/// <summary>
/// The client-side projection of one forge session: feed it the event stream, read the
/// screen. Pure state — no I/O, no threads — so the WPF and terminal fronts share the
/// exact same reading of the protocol (UX study §8: parity by construction).
/// </summary>
public sealed class ForgeSessionModel
{
    private readonly List<ForgeChatMessage> _messages = [];
    private readonly List<ForgeCriterion> _criteria = [];
    private readonly List<string> _files = [];
    private readonly List<string> _validationErrors = [];
    private readonly List<ForgeTaskProgress> _activity = [];
    private readonly List<string> _decisionOptions = [];

    /// <summary>Session slug, once <c>session.started</c> arrived.</summary>
    public string? Slug { get; private set; }

    /// <summary>Absolute session directory (level 3).</summary>
    public string? Directory { get; private set; }

    /// <summary>Render format, <c>yaml</c> or <c>script</c>.</summary>
    public string? Format { get; private set; }

    /// <summary>Whether this run resumed an existing session.</summary>
    public bool Resumed { get; private set; }

    /// <summary>Display name of the solution — the brief's goal, once there is one.</summary>
    public string? Title { get; private set; }

    /// <summary>Wire spelling of the current stage (level 3).</summary>
    public string? Stage { get; private set; }

    /// <summary>Refine cycle underway, 1-based.</summary>
    public int Iteration { get; private set; } = 1;

    /// <summary>The user-facing milestone; failure states keep the last one.</summary>
    public ForgeMilestone Milestone { get; private set; } = ForgeMilestone.Describe;

    /// <summary>The conversation, oldest first.</summary>
    public IReadOnlyList<ForgeChatMessage> Messages => _messages;

    /// <summary>The "success" card: acceptance criteria in the user's words.</summary>
    public IReadOnlyList<ForgeCriterion> Criteria => _criteria;

    /// <summary>The proposal card, once a blueprint was proposed.</summary>
    public ForgeProposal? Proposal { get; private set; }

    /// <summary>
    /// The last <c>blueprint.ready</c> payload's <c>blueprint</c> node, verbatim. The agent
    /// editor mutates this JSON and sends it back over the <c>edit</c> decision — the engine
    /// re-validates everything it receives, so Studio never has to keep it consistent itself.
    /// </summary>
    public string? BlueprintJson { get; private set; }

    /// <summary>
    /// The mounts the blueprint implies (v3 W-04): read chips before write chips,
    /// recomputed on every <c>blueprint.ready</c> — an agent edit updates them.
    /// </summary>
    public IReadOnlyList<ForgeDerivedMount> DerivedMounts { get; private set; } = [];

    /// <summary>Crew files written by the render, session-relative (level 3).</summary>
    public IReadOnlyList<string> Files => _files;

    /// <summary>Last validation outcome; null before the first one.</summary>
    public bool? ValidationOk { get; private set; }

    /// <summary>The validator's sentences, verbatim (level 3).</summary>
    public IReadOnlyList<string> ValidationErrors => _validationErrors;

    /// <summary>1-based number of the running (or last) try.</summary>
    public int? RunNumber { get; private set; }

    /// <summary>Whether a try is currently running.</summary>
    public bool RunInProgress { get; private set; }

    /// <summary>Completed tasks of the current try, in completion order.</summary>
    public IReadOnlyList<ForgeTaskProgress> Activity => _activity;

    /// <summary>Cumulative tokens spent, from <c>cost.updated</c>.</summary>
    public long TokensSpent { get; private set; }

    /// <summary>Remaining token allowance, when the session has one.</summary>
    public long? TokensRemaining { get; private set; }

    /// <summary>The diagnosis, once <c>verdict.ready</c> arrived.</summary>
    public ForgeVerdictView? Verdict { get; private set; }

    /// <summary>Arbitration options while the engine waits; empty otherwise.</summary>
    public IReadOnlyList<string> DecisionOptions => _decisionOptions;

    /// <summary>Whether the engine is waiting on an arbitration.</summary>
    public bool DecisionPending => _decisionOptions.Count > 0;

    /// <summary>The promotion, once one happened.</summary>
    public ForgePromotion? Promotion { get; private set; }

    /// <summary>
    /// Marks a hydrated session as sitting at the <c>--dry</c> pause (saved state: Test,
    /// nothing run yet) — a resume reopens the Composer review without an engine, and the
    /// « Essayer l'équipe » gate reads the same status a live pause would have streamed.
    /// </summary>
    public void MarkPaused() => FinishedStatus = "paused";

    /// <summary>Wire status of <c>session.finished</c>; null while the run is alive.</summary>
    public string? FinishedStatus { get; private set; }

    /// <summary>The last <c>error</c> event, recoverable or not.</summary>
    public ForgeErrorInfo? LastError { get; private set; }

    /// <summary>Echoes the user's own turn into the conversation (the stream never replays it).</summary>
    public void AddUserMessage(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        _messages.Add(new ForgeChatMessage(ForgeChatMessage.User, text));
    }

    /// <summary>
    /// Clears the pending arbitration once the client sent its <c>decision.made</c> — the
    /// stream never echoes it back, and the buttons must not invite a second click while
    /// the engine works toward its next stage.
    /// </summary>
    public void AcknowledgeDecision() => _decisionOptions.Clear();

    /// <summary>Applies one event to the projection. Unknown kinds are ignored here — the client shows them raw at level 3.</summary>
    public void Feed(OrkeonEvent orkeonEvent)
    {
        ArgumentNullException.ThrowIfNull(orkeonEvent);

        switch (orkeonEvent.Kind)
        {
            case ForgeEventKinds.SessionStarted:
                Slug = orkeonEvent.GetString("slug");
                Directory = orkeonEvent.GetString("dir");
                Format = orkeonEvent.GetString("format");
                Resumed = orkeonEvent.GetBool("resumed") ?? false;
                FinishedStatus = null;
                break;

            case ForgeEventKinds.StageEntered:
                Stage = orkeonEvent.GetString("stage");
                Iteration = (int)(orkeonEvent.GetInt64("iteration") ?? Iteration);
                if (ForgeMilestones.FromStage(Stage) is { } milestone)
                    Milestone = milestone;
                _decisionOptions.Clear();
                break;

            case ForgeEventKinds.AssistantMessage:
                if (orkeonEvent.GetString("text") is { } text)
                    _messages.Add(new ForgeChatMessage(ForgeChatMessage.Assistant, text));
                break;

            case ForgeEventKinds.QuestionAsked:
                // A closed question is the assistant taking its turn: it must reach the
                // conversation surface, not just the raw log, while the engine waits on stdin.
                if (orkeonEvent.GetString("text") is { } question)
                    _messages.Add(new ForgeChatMessage(ForgeChatMessage.Assistant, question));
                break;

            case ForgeEventKinds.BriefReady:
                ReadBrief(orkeonEvent);
                break;

            case ForgeEventKinds.BlueprintReady:
                ReadBlueprint(orkeonEvent);
                // A blueprint proves the proposal was reached — the artifact carries the
                // milestone when it seeds a resume, where no stage.entered ever replays.
                if (Milestone < ForgeMilestone.Propose)
                    Milestone = ForgeMilestone.Propose;
                break;

            case ForgeEventKinds.FileWritten:
                if (orkeonEvent.GetString("path") is { } path && !_files.Contains(path, StringComparer.Ordinal))
                    _files.Add(path);
                break;

            case ForgeEventKinds.ValidationResult:
                ValidationOk = orkeonEvent.GetBool("ok");
                _validationErrors.Clear();
                _validationErrors.AddRange(ReadStrings(orkeonEvent.Root, "errors"));
                break;

            case ForgeEventKinds.RunStarted:
                RunNumber = (int)(orkeonEvent.GetInt64("run") ?? 0);
                RunInProgress = true;
                _activity.Clear();
                break;

            case ForgeEventKinds.TaskCompleted:
                _activity.Add(new ForgeTaskProgress(
                    orkeonEvent.GetString("taskId"),
                    orkeonEvent.GetString("agentRole"),
                    orkeonEvent.GetBool("success") ?? false,
                    orkeonEvent.GetInt64("durationMs") ?? 0));
                break;

            case ForgeEventKinds.CostUpdated:
                TokensSpent = orkeonEvent.GetInt64("tokens") ?? TokensSpent;
                TokensRemaining = orkeonEvent.GetInt64("budgetRemaining");
                break;

            case ForgeEventKinds.RunFinished:
                RunInProgress = false;
                break;

            case ForgeEventKinds.VerdictReady:
                ReadVerdict(orkeonEvent);
                if (Milestone < ForgeMilestone.Try)
                    Milestone = ForgeMilestone.Try;
                break;

            case ForgeEventKinds.DecisionNeeded:
                _decisionOptions.Clear();
                _decisionOptions.AddRange(ReadStrings(orkeonEvent.Root, "options"));
                break;

            case ForgeEventKinds.Promoted:
                Promotion = new ForgePromotion(
                    orkeonEvent.GetString("path") ?? "",
                    orkeonEvent.GetString("launcher") ?? "",
                    orkeonEvent.GetString("install"));
                Milestone = ForgeMilestone.Adopt;
                break;

            case ForgeEventKinds.SessionFinished:
                FinishedStatus = orkeonEvent.GetString("status");
                _decisionOptions.Clear();
                // Ready is the engine's ordinary stop — no runner, no stage.entered — and
                // for the user it IS the Adopt milestone: the crew waits to be taken.
                if (string.Equals(FinishedStatus, "ready", StringComparison.Ordinal))
                    Milestone = ForgeMilestone.Adopt;
                break;

            case ForgeEventKinds.Error:
                LastError = new ForgeErrorInfo(
                    orkeonEvent.GetString("code") ?? "",
                    orkeonEvent.GetString("message") ?? "",
                    orkeonEvent.GetBool("recoverable") ?? false);
                break;

            default:
                break;
        }
    }

    /// <summary>
    /// The ✔/✘ checklist of the result card (UX study §4.3): the "success" card's criteria,
    /// re-read against the verdict — same statements, word for word. A criterion with no
    /// finding passes only when a real judge looked at it; a deterministic verdict says
    /// "judge for yourself" (null) instead of inventing a ✔. Findings that name no
    /// criterion (the mechanical ones) are prepended as failed lines.
    /// </summary>
    public IReadOnlyList<ForgeChecklistItem> BuildChecklist()
    {
        if (Verdict is not { } verdict)
            return [];

        var items = new List<ForgeChecklistItem>();
        foreach (var finding in verdict.Findings)
        {
            // No acceptance (the mechanical findings) — and an acceptance that names no
            // current criterion (a refine changed the ids, or an engine typo): both are
            // failures the card must show, never drop.
            var orphaned = string.IsNullOrWhiteSpace(finding.Acceptance)
                || !_criteria.Any(c => string.Equals(c.Id, finding.Acceptance, StringComparison.OrdinalIgnoreCase));
            if (orphaned)
                items.Add(new ForgeChecklistItem(finding.Statement, false, null));
        }

        foreach (var criterion in _criteria)
        {
            var finding = verdict.Findings.FirstOrDefault(f =>
                string.Equals(f.Acceptance, criterion.Id, StringComparison.OrdinalIgnoreCase));
            items.Add(finding is not null
                ? new ForgeChecklistItem(criterion.Statement, false, finding.Statement)
                : new ForgeChecklistItem(
                    criterion.Statement,
                    verdict.Judge == ForgeVerdictView.JudgeLlm ? true : null,
                    null));
        }

        return items;
    }

    private void ReadBrief(OrkeonEvent orkeonEvent)
    {
        if (!orkeonEvent.Root.TryGetProperty("brief", out var brief) || brief.ValueKind != JsonValueKind.Object)
            return;

        if (brief.TryGetProperty("goal", out var goal) && goal.ValueKind == JsonValueKind.String)
            Title = goal.GetString();

        _criteria.Clear();
        if (brief.TryGetProperty("acceptance", out var acceptance) && acceptance.ValueKind == JsonValueKind.Array)
        {
            foreach (var criterion in acceptance.EnumerateArray())
            {
                if (criterion.ValueKind != JsonValueKind.Object)
                    continue;
                _criteria.Add(new ForgeCriterion(
                    ReadString(criterion, "id") ?? "",
                    ReadString(criterion, "statement") ?? "",
                    string.Equals(ReadString(criterion, "kind"), "must", StringComparison.OrdinalIgnoreCase)));
            }
        }
    }

    private void ReadBlueprint(OrkeonEvent orkeonEvent)
    {
        if (!orkeonEvent.Root.TryGetProperty("blueprint", out var blueprint) || blueprint.ValueKind != JsonValueKind.Object)
            return;

        // The crew's short name ("veille-matinale") supersedes the brief's goal sentence
        // as the display title: the adoption slug derives from it, and a goal-length slug
        // makes a 200-character folder name (the owner met one).
        if (blueprint.TryGetProperty("crew", out var crew) && crew.ValueKind == JsonValueKind.Object
            && ReadString(crew, "name") is { Length: > 0 } crewName)
        {
            Title = crewName;
        }

        BlueprintJson = blueprint.GetRawText();

        // agent key → role, so the steps can speak in roles, not keys; the full per-agent
        // view feeds the Composer cards and the agent editor.
        var roles = new Dictionary<string, string>(StringComparer.Ordinal);
        var tools = new List<string>();
        var agentViews = new List<ForgeAgentView>();
        if (blueprint.TryGetProperty("agents", out var agents) && agents.ValueKind == JsonValueKind.Array)
        {
            foreach (var agent in agents.EnumerateArray())
            {
                if (agent.ValueKind != JsonValueKind.Object)
                    continue;
                var agentTools = new List<string>();
                if (agent.TryGetProperty("tools", out var toolsNode) && toolsNode.ValueKind == JsonValueKind.Array)
                {
                    foreach (var tool in toolsNode.EnumerateArray())
                    {
                        if (tool.ValueKind == JsonValueKind.String && tool.GetString() is { } name)
                        {
                            if (!agentTools.Contains(name, StringComparer.Ordinal))
                                agentTools.Add(name);
                            if (!tools.Contains(name, StringComparer.Ordinal))
                                tools.Add(name);
                        }
                    }
                }
                if (ReadString(agent, "key") is { } key && ReadString(agent, "role") is { } role)
                {
                    roles[key] = role;
                    agentViews.Add(new ForgeAgentView(
                        key, role, ReadString(agent, "goal"), ReadString(agent, "backstory"), agentTools));
                }
            }
        }

        var steps = new List<ForgeProposalStep>();
        if (blueprint.TryGetProperty("tasks", out var tasks) && tasks.ValueKind == JsonValueKind.Array)
        {
            foreach (var task in tasks.EnumerateArray())
            {
                if (task.ValueKind != JsonValueKind.Object)
                    continue;
                var agent = ReadString(task, "agent");
                steps.Add(new ForgeProposalStep(
                    ReadString(task, "description") ?? "",
                    agent is not null && roles.TryGetValue(agent, out var role) ? role : agent));
            }
        }

        Proposal = new ForgeProposal(steps, ReadString(blueprint, "rationale"), tools, agentViews);
        DerivedMounts = DeriveMounts(tools, blueprint);
    }

    /// <summary>
    /// «Ils viennent des agents» made literal: any reading tool implies the sandbox's
    /// read mount (<c>/workspace</c>); each task deliverable implies its root as a write
    /// mount (<c>/output/x.md</c> → <c>/output</c>). Nothing is invented beyond what the
    /// trial bench itself mounts.
    /// <para>
    /// The same derivation as <c>ForgePromoter.DeliverableMounts</c> on the CLI side, and it
    /// has to stay the same: these mounts are not only chips — <c>WithDerivedWriteMounts</c>
    /// writes them into an adopted team's sidecar, so a root refused by the CLI's copy and
    /// accepted here produces a team Studio can launch and the runner refuses. The guards
    /// below arrived on the CLI copy alone; <c>ForgeDerivedMountTests</c> now pins the pair.
    /// </para>
    /// </summary>
    private static List<ForgeDerivedMount> DeriveMounts(IReadOnlyList<string> tools, JsonElement blueprint)
    {
        var mounts = new List<ForgeDerivedMount>();

        if (tools.Contains("file_read", StringComparer.Ordinal) || tools.Contains("directory_read", StringComparer.Ordinal))
            mounts.Add(new ForgeDerivedMount("/workspace", IsReadWrite: false));

        if (blueprint.TryGetProperty("tasks", out var tasks) && tasks.ValueKind == JsonValueKind.Array)
        {
            foreach (var task in tasks.EnumerateArray())
            {
                if (task.ValueKind != JsonValueKind.Object
                    || ReadString(task, "deliverable") is not { Length: > 1 } deliverable
                    || deliverable[0] != '/')
                {
                    continue;
                }

                var slash = deliverable.IndexOf('/', 1);
                var root = slash > 1 ? deliverable[..slash] : deliverable;
                if (root.Length <= 1)
                    continue;

                // A deliverable root is a single segment by construction; anything else would
                // put the team's own files outside its folder. The blueprint is LLM-authored,
                // so '..' and '.' are refused explicitly rather than trusted to be absent.
                var folder = root[1..];
                if (folder.Contains('/', StringComparison.Ordinal)
                    || folder.Contains('\\', StringComparison.Ordinal)
                    || folder is "." or "..")
                {
                    continue;
                }

                // A root the runner keeps for itself would make the adopted team unlaunchable:
                // the very --mount Studio spells is refused at start (ADR-008, decision 5).
                if (MountDefinition.IsReservedVirtualPath(root))
                    continue;

                // Deduping only against read-WRITE entries let a read-only /workspace stand
                // beside a read-write one: two chips for one root, and WithDerivedWriteMounts
                // keeps the read-only one, so the chip promising a write was a lie. The CLI
                // deduped on the name alone and lost the write entirely. Both now hold the
                // same rule — one root, one mount, write wins.
                var existing = mounts.FindIndex(m => string.Equals(m.VirtualPath, root, StringComparison.Ordinal));
                if (existing < 0)
                    mounts.Add(new ForgeDerivedMount(root, IsReadWrite: true));
                else if (!mounts[existing].IsReadWrite)
                    mounts[existing] = mounts[existing] with { IsReadWrite = true };
            }
        }

        return mounts;
    }

    private void ReadVerdict(OrkeonEvent orkeonEvent)
    {
        var findings = new List<ForgeFindingView>();
        if (orkeonEvent.Root.TryGetProperty("findings", out var rawFindings) && rawFindings.ValueKind == JsonValueKind.Array)
        {
            foreach (var finding in rawFindings.EnumerateArray())
            {
                if (finding.ValueKind != JsonValueKind.Object)
                    continue;
                findings.Add(new ForgeFindingView(
                    ReadString(finding, "id"),
                    ReadString(finding, "severity") ?? "",
                    ReadString(finding, "acceptance"),
                    ReadString(finding, "statement") ?? ""));
            }
        }

        var suggestions = new List<ForgeSuggestionView>();
        if (orkeonEvent.Root.TryGetProperty("suggestions", out var rawSuggestions) && rawSuggestions.ValueKind == JsonValueKind.Array)
        {
            foreach (var suggestion in rawSuggestions.EnumerateArray())
            {
                if (suggestion.ValueKind != JsonValueKind.Object)
                    continue;
                suggestions.Add(new ForgeSuggestionView(
                    ReadString(suggestion, "target"),
                    ReadString(suggestion, "change"),
                    ReadString(suggestion, "reason")));
            }
        }

        Verdict = new ForgeVerdictView(
            orkeonEvent.GetDouble("score") ?? 0.0,
            orkeonEvent.GetBool("passing") ?? false,
            orkeonEvent.GetString("judge") ?? "",
            findings,
            suggestions)
        {
            // The last trial's own cost (W-08) — absent on older engines, and honest
            // about it: null shows no chip, never a zero.
            DurationMs = orkeonEvent.GetInt64("durationMs"),
            Tokens = orkeonEvent.GetInt64("tokens"),
            CacheHitTokens = orkeonEvent.GetInt64("cacheHitTokens"),
            CacheMissTokens = orkeonEvent.GetInt64("cacheMissTokens"),
        };
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static IEnumerable<string> ReadStrings(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var entry in array.EnumerateArray())
        {
            if (entry.ValueKind == JsonValueKind.String && entry.GetString() is { } text)
                yield return text;
        }
    }
}
