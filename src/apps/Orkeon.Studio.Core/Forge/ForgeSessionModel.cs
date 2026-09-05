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
/// <summary>A mount the blueprint implies, and who implied it.</summary>
/// <param name="VirtualPath">The name the agents address.</param>
/// <param name="IsReadWrite">Whether anything writes there.</param>
/// <param name="Agents">
/// The roles that read or write it — PROVENANCE, never permission. The runtime mounts one flat
/// list per host, so no agent is confined to its own folder; this only answers «why does this
/// mount exist». The blueprint carries it per agent and per task, and the derivation used to
/// throw it away by flattening every agent's tools into one union.
/// </param>
public sealed record ForgeDerivedMount(
    string VirtualPath, bool IsReadWrite, IReadOnlyList<string>? Agents = null);

/// <summary>One completed task of the running try.</summary>
public sealed record ForgeTaskProgress(string? TaskId, string? AgentRole, bool Success, long DurationMs);

/// <summary>One finding of the verdict, as the checklist will show it.</summary>
/// <param name="Id">The finding's own identifier, when the judge gave one.</param>
/// <param name="Severity">How much it matters — blocking, or not.</param>
/// <param name="Acceptance">The criterion it answers, when it answers one.</param>
/// <param name="Statement">What is wrong, in the judge's words.</param>
/// <param name="Evidence">
/// What the judge saw — the run's own error or output for a mechanical finding. It travels on
/// the wire and used to be dropped here, which is why a crashed trial had a statement and
/// nothing to back it.
/// </param>
public sealed record ForgeFindingView(
    string? Id, string Severity, string? Acceptance, string Statement, string? Evidence = null)
{
    /// <summary>Wire spelling of a finding that must not be waved through.</summary>
    public const string SeverityBlocking = "blocking";

    /// <summary>Whether this finding blocks — a minor one must not look the same.</summary>
    public bool IsBlocking =>
        string.Equals(Severity, SeverityBlocking, StringComparison.OrdinalIgnoreCase);
}

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

    /// <summary>The ascending half of the last trial; null when unmeasured.</summary>
    public long? PromptTokens { get; init; }

    /// <summary>The descending half of the last trial; null when unmeasured.</summary>
    public long? CompletionTokens { get; init; }

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

    /// <summary>
    /// Which build of the engine answered, from <c>session.started</c>.
    /// <para>
    /// Null means the engine did not say — which is itself the news: every build from
    /// 1.0.0-rc.2 on announces itself, so silence here means the binary the launcher found
    /// predates the field. A screen that shows a figure the engine never sent, or fails to
    /// show one it did, is diagnosed from this line first.
    /// </para>
    /// </summary>
    public string? EngineVersion { get; private set; }

    /// <summary>Cumulative tokens spent, from <c>cost.updated</c>.</summary>
    public long TokensSpent { get; private set; }

    /// <summary>
    /// The ascending half of <see cref="TokensSpent"/> — everything sent to the models.
    /// Zero while nothing has been spent; the engine reports both halves together.
    /// </summary>
    public long PromptTokens { get; private set; }

    /// <summary>The descending half — everything the models sent back.</summary>
    public long CompletionTokens { get; private set; }

    /// <summary>
    /// How much of <see cref="TokensSpent"/> the engine had to approximate, because a
    /// provider returned no usage of its own. Nonzero means the figures on screen are
    /// estimates and must be shown as such.
    /// </summary>
    public long EstimatedTokens { get; private set; }

    /// <summary>Whether any part of the meter is an approximation rather than a report.</summary>
    public bool TokensAreEstimated => EstimatedTokens > 0;

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
    /// try-the-team gate reads the same status a live pause would have streamed.
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
                EngineVersion = orkeonEvent.GetString("engine");
                FinishedStatus = null;
                break;

            case ForgeEventKinds.StageEntered:
                Stage = orkeonEvent.GetString("stage");
                Iteration = (int)(orkeonEvent.GetInt64("iteration") ?? Iteration);
                if (ForgeMilestones.FromStage(Stage) is { } milestone)
                    Milestone = milestone;
                _decisionOptions.Clear();
                break;

            // A closed question is the assistant taking its turn: it must reach the
            // conversation surface, not just the raw log, while the engine waits on stdin --
            // the very bubble an ordinary assistant message produces.
            case ForgeEventKinds.AssistantMessage:
            case ForgeEventKinds.QuestionAsked:
                AddAssistantMessage(orkeonEvent);
                break;

            case ForgeEventKinds.BriefReady:
                ReadBrief(orkeonEvent);
                break;

            case ForgeEventKinds.BlueprintReady:
                ReadBlueprint(orkeonEvent);
                // A blueprint proves the proposal was reached — the artifact carries the
                // milestone when it seeds a resume, where no stage.entered ever replays.
                RaiseMilestone(ForgeMilestone.Propose);
                break;

            case ForgeEventKinds.FileWritten:
                RecordFile(orkeonEvent.GetString("path"));
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
                // The split is cumulative like the total, and absent from an older
                // session's stream — keeping the previous reading beats zeroing a meter.
                PromptTokens = orkeonEvent.GetInt64("promptTokens") ?? PromptTokens;
                CompletionTokens = orkeonEvent.GetInt64("completionTokens") ?? CompletionTokens;
                EstimatedTokens = orkeonEvent.GetInt64("estimatedTokens") ?? EstimatedTokens;
                TokensRemaining = orkeonEvent.GetInt64("budgetRemaining");
                break;

            case ForgeEventKinds.RunFinished:
                RunInProgress = false;
                break;

            case ForgeEventKinds.VerdictReady:
                ReadVerdict(orkeonEvent);
                RaiseMilestone(ForgeMilestone.Try);
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

    /// <summary>Turns an event's <c>text</c> into an assistant bubble; a textless event says nothing.</summary>
    private void AddAssistantMessage(OrkeonEvent orkeonEvent)
    {
        if (orkeonEvent.GetString("text") is { } text)
            _messages.Add(new ForgeChatMessage(ForgeChatMessage.Assistant, text));
    }

    /// <summary>Records one rendered crew file, once: a re-render repeats the paths it rewrites.</summary>
    private void RecordFile(string? path)
    {
        if (path is not null && !_files.Contains(path, StringComparer.Ordinal))
            _files.Add(path);
    }

    /// <summary>
    /// Moves the milestone forward, never backward: an artifact that proves a stage was
    /// reached must not rewind a screen the stream already carried further.
    /// </summary>
    private void RaiseMilestone(ForgeMilestone milestone)
    {
        if (Milestone < milestone)
            Milestone = milestone;
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
                items.Add(new ForgeChecklistItem(finding.Statement, false, finding.Evidence));
        }

        foreach (var criterion in _criteria)
        {
            var finding = verdict.Findings.FirstOrDefault(f =>
                string.Equals(f.Acceptance, criterion.Id, StringComparison.OrdinalIgnoreCase));
            items.Add(BuildChecklistItem(criterion, finding, verdict.Judge));
        }

        return items;
    }

    /// <summary>
    /// One line of the checklist. A finding fails the criterion and says why, with the
    /// judge's own evidence appended when it carried some. No finding passes it only when a
    /// real judge looked: a deterministic verdict says "judge for yourself" (null) rather
    /// than inventing a ✔.
    /// </summary>
    private static ForgeChecklistItem BuildChecklistItem(
        ForgeCriterion criterion, ForgeFindingView? finding, string judge)
    {
        if (finding is null)
        {
            bool? passed = string.Equals(judge, ForgeVerdictView.JudgeLlm, StringComparison.Ordinal)
                ? true
                : null;
            return new ForgeChecklistItem(criterion.Statement, passed, null);
        }

        var detail = finding.Evidence is { Length: > 0 } evidence
            ? $"{finding.Statement} — {evidence}"
            : finding.Statement;
        return new ForgeChecklistItem(criterion.Statement, false, detail);
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

        ReadCrewTitle(blueprint);
        BlueprintJson = blueprint.GetRawText();

        // agent key → role, so the steps can speak in roles, not keys; the full per-agent
        // view feeds the Composer cards and the agent editor.
        var roles = new Dictionary<string, string>(StringComparer.Ordinal);
        var tools = new List<string>();
        var agentViews = ReadAgents(blueprint, roles, tools);

        Proposal = new ForgeProposal(
            ReadSteps(blueprint, roles), ReadString(blueprint, "rationale"), tools, agentViews);
        DerivedMounts = DeriveMounts(agentViews, roles, blueprint);
    }

    /// <summary>
    /// The crew's short name ("veille-matinale") supersedes the brief's goal sentence as the
    /// display title: the adoption slug derives from it, and a goal-length slug makes a
    /// 200-character folder name (the owner met one).
    /// </summary>
    private void ReadCrewTitle(JsonElement blueprint)
    {
        if (blueprint.TryGetProperty("crew", out var crew) && crew.ValueKind == JsonValueKind.Object
            && ReadString(crew, "name") is { Length: > 0 } crewName)
        {
            Title = crewName;
        }
    }

    /// <summary>
    /// The Composer's agent cards, in wire order. Fills <paramref name="roles"/> (agent key →
    /// role, what the steps speak in) and <paramref name="crewTools"/> (the union the proposal
    /// card lists) along the way — an agent missing its key or role still contributes its
    /// tools to that union, exactly as the single pass used to.
    /// </summary>
    private static List<ForgeAgentView> ReadAgents(
        JsonElement blueprint, Dictionary<string, string> roles, List<string> crewTools)
    {
        var agentViews = new List<ForgeAgentView>();
        if (!blueprint.TryGetProperty("agents", out var agents) || agents.ValueKind != JsonValueKind.Array)
            return agentViews;

        foreach (var agent in agents.EnumerateArray())
        {
            if (agent.ValueKind != JsonValueKind.Object)
                continue;

            var agentTools = ReadAgentTools(agent, crewTools);
            if (ReadString(agent, "key") is { } key && ReadString(agent, "role") is { } role)
            {
                roles[key] = role;
                agentViews.Add(new ForgeAgentView(
                    key, role, ReadString(agent, "goal"), ReadString(agent, "backstory"), agentTools));
            }
        }

        return agentViews;
    }

    /// <summary>One agent's own tools, deduplicated, and folded into the crew-wide <paramref name="crewTools"/>.</summary>
    private static List<string> ReadAgentTools(JsonElement agent, List<string> crewTools)
    {
        var agentTools = new List<string>();
        if (!agent.TryGetProperty("tools", out var toolsNode) || toolsNode.ValueKind != JsonValueKind.Array)
            return agentTools;

        foreach (var tool in toolsNode.EnumerateArray())
        {
            if (tool.ValueKind != JsonValueKind.String || tool.GetString() is not { } name)
                continue;

            if (!agentTools.Contains(name, StringComparer.Ordinal))
                agentTools.Add(name);
            if (!crewTools.Contains(name, StringComparer.Ordinal))
                crewTools.Add(name);
        }

        return agentTools;
    }

    /// <summary>The numbered steps of the proposal, each told under its agent's role.</summary>
    private static List<ForgeProposalStep> ReadSteps(JsonElement blueprint, Dictionary<string, string> roles)
    {
        var steps = new List<ForgeProposalStep>();
        if (!blueprint.TryGetProperty("tasks", out var tasks) || tasks.ValueKind != JsonValueKind.Array)
            return steps;

        foreach (var task in tasks.EnumerateArray())
        {
            if (task.ValueKind != JsonValueKind.Object)
                continue;

            var agent = ReadString(task, "agent");
            steps.Add(new ForgeProposalStep(ReadString(task, "description") ?? "", ResolveRole(roles, agent)));
        }

        return steps;
    }

    /// <summary>The role behind an agent key; the key itself when the blueprint declares no such agent.</summary>
    private static string? ResolveRole(Dictionary<string, string> roles, string? agentKey) =>
        agentKey is not null && roles.TryGetValue(agentKey, out var role) ? role : agentKey;

    /// <summary>
    /// The rule that they come from the agents, made literal: any reading tool implies the sandbox's
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
    private static List<ForgeDerivedMount> DeriveMounts(
        IReadOnlyList<ForgeAgentView> agents,
        Dictionary<string, string> roles,
        JsonElement blueprint)
    {
        var mounts = new List<ForgeDerivedMount>();

        // Per agent, not over the union: the union answered «somebody reads» and the screen
        // could only repeat it. Named, it answers «who», which is the question asked.
        var readers = agents
            .Where(a => a.Tools.Contains("file_read", StringComparer.Ordinal)
                     || a.Tools.Contains("directory_read", StringComparer.Ordinal))
            .Select(a => a.Role)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (readers.Count > 0)
            mounts.Add(new ForgeDerivedMount("/workspace", IsReadWrite: false, readers));

        if (!blueprint.TryGetProperty("tasks", out var tasks) || tasks.ValueKind != JsonValueKind.Array)
            return mounts;

        foreach (var task in tasks.EnumerateArray())
        {
            if (task.ValueKind != JsonValueKind.Object || DeliverableRoot(task) is not { } root)
                continue;

            // The task's own agent — sitting in the same JsonElement as the deliverable
            // and, until W-04, never read.
            AddWriteMount(mounts, root, WriterRole(task, roles));
        }

        return mounts;
    }

    /// <summary>
    /// The mountable root a task deliverable implies (<c>/output/report.md</c> →
    /// <c>/output</c>), or null when nothing may be mounted for it. The blueprint is
    /// LLM-authored, so a root that is not a single segment — <c>'.'</c>, <c>'..'</c>, a
    /// nested or backslashed path — is refused rather than trusted to be absent: anything
    /// else would put the team's own files outside its folder. A root the runner keeps for
    /// itself is refused too, since the very --mount Studio spells would then be rejected at
    /// start (ADR-008, decision 5), making the adopted team unlaunchable.
    /// </summary>
    private static string? DeliverableRoot(JsonElement task)
    {
        if (ReadString(task, "deliverable") is not { Length: > 1 } deliverable || deliverable[0] != '/')
            return null;

        var slash = deliverable.IndexOf('/', 1);
        var root = slash > 1 ? deliverable[..slash] : deliverable;
        if (root.Length <= 1)
            return null;

        var folder = root[1..];
        if (folder.Contains('/', StringComparison.Ordinal)
            || folder.Contains('\\', StringComparison.Ordinal)
            || folder is "." or "..")
        {
            return null;
        }

        return MountDefinition.IsReservedVirtualPath(root) ? null : root;
    }

    /// <summary>The role writing a task's deliverable, when the blueprint declares that agent.</summary>
    private static string? WriterRole(JsonElement task, Dictionary<string, string> roles) =>
        ReadString(task, "agent") is { } key && roles.TryGetValue(key, out var role) ? role : null;

    /// <summary>
    /// One root, one mount, write wins. Deduping only against read-WRITE entries let a
    /// read-only /workspace stand beside a read-write one: two chips for one root, and
    /// WithDerivedWriteMounts keeps the read-only one, so the chip promising a write was a
    /// lie. The CLI deduped on the name alone and lost the write entirely. Both now hold
    /// this same rule.
    /// </summary>
    private static void AddWriteMount(List<ForgeDerivedMount> mounts, string root, string? writer)
    {
        var existing = mounts.FindIndex(m => string.Equals(m.VirtualPath, root, StringComparison.Ordinal));
        if (existing < 0)
        {
            mounts.Add(new ForgeDerivedMount(root, IsReadWrite: true, writer is null ? [] : [writer]));
            return;
        }

        var merged = Merge(mounts[existing].Agents, writer);
        mounts[existing] = mounts[existing] with { IsReadWrite = true, Agents = merged };
    }

    /// <summary>Adds a role to a mount's provenance, once.</summary>
    private static List<string> Merge(IReadOnlyList<string>? existing, string? role)
    {
        var merged = existing is null ? [] : new List<string>(existing);
        if (role is { Length: > 0 } && !merged.Contains(role, StringComparer.Ordinal))
            merged.Add(role);

        return merged;
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
                    ReadString(finding, "statement") ?? "",
                    ReadString(finding, "evidence")));
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
            PromptTokens = orkeonEvent.GetInt64("promptTokens"),
            CompletionTokens = orkeonEvent.GetInt64("completionTokens"),
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
