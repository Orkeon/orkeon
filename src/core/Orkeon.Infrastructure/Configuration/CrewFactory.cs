using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orkeon.Application.EventHub;
using Orkeon.Application.Interfaces;
using Orkeon.Domain.Common;
using Orkeon.Domain.Configuration;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Crew;
using Orkeon.Domain.Knowledge;
using Orkeon.Domain.Task;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainCrew = Orkeon.Domain.Crew.Crew;

namespace Orkeon.Infrastructure.Configuration;

/// <summary>
/// Factory for creating domain Crew objects from configuration.
/// </summary>
public partial class CrewFactory : ICrewFactory
{
    private readonly ICrewDefinitionLoader _loader;
    private readonly IToolRegistry _toolRegistry;
    private readonly ILogger<CrewFactory> _logger;
    private readonly ICrewRepository _crewRepository;
    private readonly IAgentRepository _agentRepository;
    private readonly ITaskRepository _taskRepository;
    private readonly bool _strictTools;
    private readonly bool _prepareRagCollections;
    private readonly bool _warnOnDeliverableTools;
    private readonly Orkeon.Rag.Abstractions.Interfaces.IRagCollectionsBootstrapper? _ragBootstrapper;
    private readonly ICrewLinkRegistry? _linkRegistry;
    private readonly Orkeon.Application.Interfaces.Ports.ILlmProfileRegistry? _llmProfiles;
    private readonly Orkeon.Application.Crew.Execution.DecisionCircuit? _decisionCircuit;
    private readonly Action<string>? _onWithheldTool;
    private readonly Action<string>? _onDecisionWarning;
    private readonly IReadOnlyCollection<string>? _decisionProfiles;
    private readonly System.Collections.Immutable.ImmutableList<string> _routeLadder;
    private readonly IReadOnlyList<string> _effortLevels;

    /// <summary>Initializes a new instance of <see cref="CrewFactory"/>.</summary>
    /// <param name="loader">The crew definition loader.</param>
    /// <param name="toolRegistry">The tool registry.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="crewRepository">The crew repository for persisting created crews.</param>
    /// <param name="agentRepository">The agent repository for persisting created agents.</param>
    /// <param name="taskRepository">The task repository for persisting created tasks.</param>
    /// <param name="options">
    /// Factory options; when omitted, tool resolution is lenient (missing tools are logged and skipped).
    /// See <see cref="CrewFactoryOptions.StrictTools"/>.
    /// </param>
    /// <param name="ragBootstrapper">
    /// Optional RAG collections bootstrapper (registered by <c>AddOrkeonRag</c>): when present
    /// and <see cref="CrewFactoryOptions.PrepareRagCollections"/> is enabled, the collections
    /// declared by the configuration's <c>rag:</c> block are ingested at crew creation.
    /// </param>
    /// <param name="linkRegistry">
    /// Optional EventHub link registry (registered by <c>AddOrkeonEventHubAcl</c>): the crew's
    /// <c>links:</c> block is read from YAML long before the crew has an identity, so the
    /// authorizations are handed over here, once the <see cref="CrewId"/> exists.
    /// </param>
    /// <param name="llmProfiles">
    /// The host's named LLM profiles (registered by <c>AddOrkeonInfrastructure</c>): an agent or a
    /// task naming a profile the host does not offer fails the load with the list of known ones
    /// (GAP-17). Null offers the default profile alone.
    /// </param>
    /// <param name="decisionCircuit">
    /// The host's decision circuit (registered by <c>AddOrkeonInfrastructure</c>): off, a crew naming
    /// <c>system_one_decide</c> is told the tool is withheld and why, rather than that it is unknown
    /// (#433). Null reports it as any missing tool.
    /// </param>
    /// <param name="decisionProfiles">
    /// The host's decision profiles (<c>Orkeon:Decision:Profiles</c>): a crew with <c>decision.mode: none</c>
    /// naming one is refused, and a <c>required</c> one naming none, with no decision task and no
    /// <c>system_one_decide</c>, is warned that it asks System One nothing (#429). Null leaves both rules aside.
    /// </param>
    /// <param name="route">
    /// The ladder an agent on <c>llm.profile: auto</c> is routed among (<c>Orkeon:Decision:Route:Ladder</c>, #434):
    /// a crew naming <c>auto</c> fails its load when the host declares no ladder or the ladder names a profile the
    /// host does not offer. Null declares none.
    /// </param>
    /// <param name="effort">
    /// The levels System One picks among on <c>thinking: { effort: auto }</c> (<c>Orkeon:Decision:Effort:Levels</c>,
    /// #439): a crew whose <c>min</c>/<c>max</c> bounds leave none of them fails its load. Null is the default levels.
    /// </param>
#pragma warning disable S107 // DI-composed factory: one optional collaborator per opt-in subsystem
    public CrewFactory(
        ICrewDefinitionLoader loader,
        IToolRegistry toolRegistry,
        ILogger<CrewFactory> logger,
        ICrewRepository crewRepository,
        IAgentRepository agentRepository,
        ITaskRepository taskRepository,
        IOptions<CrewFactoryOptions>? options = null,
        Orkeon.Rag.Abstractions.Interfaces.IRagCollectionsBootstrapper? ragBootstrapper = null,
        ICrewLinkRegistry? linkRegistry = null,
        Orkeon.Application.Interfaces.Ports.ILlmProfileRegistry? llmProfiles = null,
        Orkeon.Application.Crew.Execution.DecisionCircuit? decisionCircuit = null,
        IOptions<Orkeon.Infrastructure.LLMs.Decision.DecisionProfilesOptions>? decisionProfiles = null,
        IOptions<Orkeon.Application.Crew.Routing.AgentRouteOptions>? route = null,
        IOptions<Orkeon.Application.Crew.Routing.AgentEffortOptions>? effort = null)
#pragma warning restore S107
    {
        ArgumentNullException.ThrowIfNull(loader);
        _loader = loader;
        ArgumentNullException.ThrowIfNull(toolRegistry);
        _toolRegistry = toolRegistry;
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        ArgumentNullException.ThrowIfNull(crewRepository);
        _crewRepository = crewRepository;
        ArgumentNullException.ThrowIfNull(agentRepository);
        _agentRepository = agentRepository;
        ArgumentNullException.ThrowIfNull(taskRepository);
        _taskRepository = taskRepository;
        _strictTools = options?.Value.StrictTools ?? false;
        _prepareRagCollections = options?.Value.PrepareRagCollections ?? true;
        _warnOnDeliverableTools = options?.Value.WarnOnDeliverableTools ?? true;
        _ragBootstrapper = ragBootstrapper;
        _linkRegistry = linkRegistry;
        _llmProfiles = llmProfiles;
        _decisionCircuit = decisionCircuit;
        _onWithheldTool = options?.Value.OnWithheldTool;
        _onDecisionWarning = options?.Value.OnDecisionWarning;
        _decisionProfiles = decisionProfiles?.Value.Profiles.Keys.ToList();
        _routeLadder = [.. route?.Value.Ladder ?? []];
        _effortLevels = effort is null
            ? Orkeon.Application.Crew.Routing.AgentEffortOptions.DefaultLevels
            : Orkeon.Application.Crew.Routing.AgentEffortOptions.AllowedLevels(effort.Value);
    }

    /// <inheritdoc />
    public Task<DomainCrew> CreateFromConfigAsync(
        CrewConfiguration config,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        return CreateFromConfigCoreAsync(config, ct);
    }

    private async Task<DomainCrew> CreateFromConfigCoreAsync(
        CrewConfiguration config,
        CancellationToken ct)
    {
        // The decision.mode rules first (#429): a configuration handed straight to the factory is refused in the
        // one spelling every loader uses, not among the validator's errors.
        ValidateDecisionMode(config);
        ValidateConfiguration(config);
        ValidateLlmProfiles(config);

        await PrepareRagCollectionsAsync(config, ct).ConfigureAwait(false);
        WarnOnKnowledgeWithoutSubsystem(config);

        LogCreatingCrewWithAgentsAnd(config.Name, config.Agents.Count, config.Tasks.Count);

        // A required crew with the circuit off loads, and its run is refused before the first task with the
        // typed error (#429): a withheld system_one_decide must not fail the load first.
        var refuseWithheld = config.DecisionMode != Orkeon.Domain.Crew.CrewDecisionMode.Required;
        var agentMap = await CreateAgentsAsync(config.Agents, config.Rag?.DefaultProfile, refuseWithheld, ct).ConfigureAwait(false);
        // A without: relay names its agent by key (#430): resolved here to the agent built for that key.
        // A script may give two agents one name: a relay then names the first of them.
        var agentIdByKey = config.Agents
            .Where(a => a.Key is not null && agentMap.ContainsKey(a.Id))
            .GroupBy(a => a.Key!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => agentMap[g.First().Id].Id, StringComparer.Ordinal);
        var taskMap = await CreateTasksAsync(config.Tasks, agentMap, agentIdByKey, refuseWithheld, ct).ConfigureAwait(false);

        SetupTaskDependencies(config.Tasks, taskMap);
        AddTaskContextData(config.Tasks, taskMap);

        var crew = AssembleCrew(config, agentMap, taskMap);
        WarnOnDeliverablesWithoutWriter(config, crew, agentMap, taskMap);

        // Persist all aggregates so the orchestrator can retrieve them by id
        foreach (var agent in agentMap.Values)
            await _agentRepository.AddAsync(agent, ct).ConfigureAwait(false);
        foreach (var task in taskMap.Values)
            await _taskRepository.AddAsync(task, ct).ConfigureAwait(false);
        await _crewRepository.AddAsync(crew, ct).ConfigureAwait(false);

        RegisterLinks(config, crew.Id);

        LogSuccessfullyCreatedCrewWithId(config.Name, crew.Id);

        return crew;
    }

    /// <summary>
    /// Hands the crew's identity and <c>links:</c> declarations to the ACL (HUB-03). Every
    /// crew registers — links name crews by <c>name:</c> while messages carry ids, so a crew
    /// that declared nothing must still be resolvable as somebody else's target. A
    /// configuration that declares links while the ACL is absent gets a warning rather than
    /// silence: the author wrote an authorization, and a door nobody guards is worth saying
    /// out loud.
    /// </summary>
    private void RegisterLinks(CrewConfiguration config, CrewId crewId)
    {
        if (_linkRegistry is null)
        {
            if (config.Links is not null)
                LogLinksDeclaredButAclMissing(config.Name);
            return;
        }

        _linkRegistry.Register(crewId, config.Name, config.Links);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Crew '{CrewName}' declares a links: block but the EventHub ACL is not registered — call AddOrkeonEventHubAcl(); the declared authorizations are not enforced.")]
    private partial void LogLinksDeclaredButAclMissing(string crewName);

    /// <summary>
    /// A task whose <c>tool_call</c> deliverable no agent that may run it can write ends without its
    /// file, and the run said nothing until #392: the load warns, once per task, with the fixes; so it does
    /// for a decision task that declares a deliverable, which nothing ever writes. YAML
    /// and <c>.ork.ts</c> crews both come through here; <c>orkeon run --validate</c> turns the log off
    /// (<see cref="CrewFactoryOptions.WarnOnDeliverableTools"/>) and prints the same findings itself
    /// (<see cref="Orkeon.Application.Crew.DeliverableToolCheck"/>).
    /// </summary>
    private void WarnOnDeliverablesWithoutWriter(
        CrewConfiguration config, DomainCrew crew, Dictionary<string, DomainAgent> agentMap, Dictionary<string, CrewTask> taskMap)
    {
        if (!_warnOnDeliverableTools)
            return;

        var findings = Orkeon.Application.Crew.DeliverableToolCheck.Check(
            taskMap.Values, agentMap.Values, crew.ProcessType, crew.ManagerAgentId);
        foreach (var finding in findings)
            LogDeliverableWithoutWriter(config.Name, finding.Message);
    }

    // The sentence is DeliverableToolFinding.Message, which names the task, the agents and the path.
    [LoggerMessage(Level = LogLevel.Warning, Message = "Crew '{CrewName}': {DeliverableWarning}")]
    private partial void LogDeliverableWithoutWriter(string crewName, string deliverableWarning);

    /// <summary>
    /// Ingests the collections declared by the configuration's <c>rag:</c> block (RAG-03/C3).
    /// No-op when the block is absent/empty or <see cref="CrewFactoryOptions.PrepareRagCollections"/>
    /// is disabled; a declared block without the RAG subsystem registered logs a warning
    /// instead of failing (the agents' attachments will then search an empty index).
    /// </summary>
    private async System.Threading.Tasks.Task PrepareRagCollectionsAsync(CrewConfiguration config, CancellationToken ct)
    {
        if (!_prepareRagCollections || config.Rag is not { Collections.Count: > 0 } ragConfig)
            return;

        if (_ragBootstrapper is null)
        {
            LogRagDeclaredButSubsystemMissing(config.Name);
            return;
        }

        await _ragBootstrapper.PrepareAsync(ragConfig, ct).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Crew '{CrewName}' declares a rag: block but the RAG subsystem is not registered — call AddOrkeonRag(configuration); declared collections were not ingested.")]
    private partial void LogRagDeclaredButSubsystemMissing(string crewName);

    /// <summary>
    /// An agent's <c>knowledge:</c> attachments are read at execution by the knowledge
    /// augmenter, which only the RAG subsystem registers. Without it they are skipped — the
    /// agent answers ungrounded — so the load says so, once per agent (GAP-02). The
    /// bootstrapper stands for the subsystem: <c>AddOrkeonRag</c> registers both.
    /// </summary>
    private void WarnOnKnowledgeWithoutSubsystem(CrewConfiguration config)
    {
        if (_ragBootstrapper is not null)
            return;

        foreach (var agent in config.Agents.Where(a => a.KnowledgeAttachments.Count > 0))
        {
            LogKnowledgeDeclaredButSubsystemMissing(
                config.Name,
                agent.Role,
                string.Join(", ", agent.KnowledgeAttachments.Select(a => a.Collection)));
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Crew '{CrewName}': agent '{AgentRole}' declares knowledge: ({Collections}) but the RAG subsystem is not registered — call AddOrkeonRag(configuration); no knowledge context will be injected into its prompts.")]
    private partial void LogKnowledgeDeclaredButSubsystemMissing(string crewName, string agentRole, string collections);

    /// <summary>
    /// Every profile the crew names — an agent's <c>llm.profile</c>, a task's
    /// <c>llm_override.profile</c> — must be one the host offers, checked before anything is
    /// built or ingested (GAP-17). Like an unknown tool, a typo fails the load and the message
    /// lists what exists; a crew never silently falls back to another vendor.
    /// </summary>
    private void ValidateLlmProfiles(CrewConfiguration config)
    {
        var unrouted = UnroutedAgents(config);
        foreach (var agent in config.Agents)
        {
            // A model beside auto would run on every rung System One picks (#434): the rung's own model must.
            if (agent.LlmConfig is { } llm && AgentBuilder.AutoProfileModel(llm) is { } model)
                throw new InvalidOperationException(AgentBuilder.AutoProfileModelRefusal(
                    $"Crew '{config.Name}': agent '{agent.Key ?? agent.Role}' (its llm: block, or the crew's)", model));

            if (unrouted.Contains(agent.Id) && Orkeon.Application.Interfaces.Ports.LlmProfiles.IsAuto(agent.LlmConfig?.Profile))
                continue;
            EnsureProfile(agent.LlmConfig?.Profile, $"Crew '{config.Name}': agent '{agent.Role}'");
        }

        foreach (var task in config.Tasks)
            EnsureProfile(task.LlmOverride?.Profile, $"Crew '{config.Name}': task '{Shorten(task.Description)}'");

        EnsureEffortAutoSupported(config);
    }

    /// <summary>
    /// An agent or a task whose thinking leaves something to System One (<c>effort: auto</c>, <c>enabled: auto</c>,
    /// #439) must leave it a level: bounds <c>min</c>/<c>max</c> that keep none of the host's levels refuse the load,
    /// whatever the profiles. It must also run on a profile whose model thinks: one whose thinking support is
    /// <c>None</c> has no level to pick, and the load says so. On <c>llm.profile: auto</c>, only when every rung of the
    /// ladder and the default are <c>None</c>. A provider that cannot tell is trusted.
    /// </summary>
    private void EnsureEffortAutoSupported(CrewConfiguration config)
    {
        var agents = config.Agents.ToDictionary(a => a.Id);
        foreach (var agent in config.Agents.Where(a => a.LlmConfig?.Thinking is { HasAuto: true }))
        {
            var where = $"Crew '{config.Name}': agent '{agent.Key ?? agent.Role}' declares thinking: {{ {AutoMarker(agent.LlmConfig!.Thinking!)} }}";
            EnsureLevelsLeft(agent.LlmConfig.Thinking!, where);
            if (_llmProfiles is not null)
                EnsureThinks(agent.LlmConfig.Profile, NamedModel(agent.LlmConfig), where);
        }

        foreach (var task in config.Tasks.Where(t => t.LlmOverride?.Thinking is { HasAuto: true }))
        {
            var where = $"Crew '{config.Name}': task '{Shorten(task.Description)}' declares thinking: {{ {AutoMarker(task.LlmOverride!.Thinking!)} }} in its llm_override";
            EnsureLevelsLeft(task.LlmOverride.Thinking!, where);
            if (_llmProfiles is null)
                continue;
            var agent = task.AssignedAgentId is { } id && agents.TryGetValue(id, out var assigned) ? assigned : null;
            // A task naming its own profile runs on that profile's model; else on its agent's.
            var ownProfile = !string.IsNullOrWhiteSpace(task.LlmOverride.Profile);
            EnsureThinks(ownProfile ? task.LlmOverride.Profile : agent?.LlmConfig?.Profile, ownProfile ? null : NamedModel(agent?.LlmConfig), where);
        }
    }

    /// <summary>What a thinking leaves to System One, as the crew writes it (#439).</summary>
    private static string AutoMarker(LlmThinkingConfig thinking) => thinking.AutoEffort switch
    {
        { IsBounded: true } bounded => $"effort: {{ auto: true, {bounded.DescribeBounds()} }}",
        not null => "effort: auto",
        _ => "enabled: auto",
    };

    private void EnsureLevelsLeft(LlmThinkingConfig thinking, string where)
    {
        if (thinking.AutoEffort is not { IsBounded: true } bounded || bounded.Allowed(_effortLevels).Count > 0)
            return;

        throw new InvalidOperationException(
            $"{where}, and its bounds keep none of the levels the host offers ({string.Join(", ", _effortLevels)}, " +
            $"{Orkeon.Application.Crew.Routing.AgentEffortOptions.LevelsKey}): widen min/max, or add a level inside them to the host's levels.");
    }

    private static string? NamedModel(LlmConfig? config) => string.IsNullOrWhiteSpace(config?.Model) ? null : config.Model;

    private void EnsureThinks(string? profile, string? model, string where)
    {
        List<string> candidates = Orkeon.Application.Interfaces.Ports.LlmProfiles.IsAuto(profile)
            ? [.. _routeLadder, Orkeon.Application.Interfaces.Ports.LlmProfiles.Default]
            : [string.IsNullOrWhiteSpace(profile) ? Orkeon.Application.Interfaces.Ports.LlmProfiles.Default : profile];
        if (candidates.Any(name => Thinks(name, model)))
            return;

        throw new InvalidOperationException(
            $"{where}, and the model of {(candidates.Count == 1 ? $"its profile '{candidates[0]}'" : $"every profile System One may pick ({string.Join(", ", candidates)})")} " +
            "does not think (thinking support None): System One has nothing to pick. Remove the auto, or run it on a profile whose model thinks.");
    }

    private bool Thinks(string name, string? model)
    {
        var provider = _llmProfiles!.Resolve(name).Provider;
#pragma warning disable ORKEXP005 // The reasoning capabilities are experimental; the load reads them to refuse a pick with no level.
        var reasoning = provider.DescribeReasoning(model ?? provider.BaseConfig?.Model);
#pragma warning restore ORKEXP005
        return reasoning is null || reasoning.Thinking != ThinkingSupport.None;
    }

    /// <summary>
    /// The agents no task is assigned to that the crew names as its manager or as a decision relay
    /// (<c>without.agent</c>): one on <c>llm.profile: auto</c> runs on the default profile, System One routes only an
    /// agent's tasks (#434), so it is left out of the <c>auto</c> checks.
    /// </summary>
    private static HashSet<AgentId> UnroutedAgents(CrewConfiguration config)
    {
        var relays = config.Tasks.Select(t => t.Decision?.Without?.Agent).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var assigned = config.Tasks.Select(t => t.AssignedAgentId).OfType<AgentId>().ToHashSet();
        return config.Agents
            .Where(a => !assigned.Contains(a.Id)
                && (a.Id.Equals(config.ManagerAgentId) || relays.Contains(a.Key ?? a.Role)))
            .Select(a => a.Id)
            .ToHashSet();
    }

    /// <summary>
    /// A profile the host offers, or <c>auto</c> (#434) on a host whose ladder holds profiles it offers, each of
    /// them checked as if the crew named it.
    /// </summary>
    private void EnsureProfile(string? profile, string where)
    {
        if (!Orkeon.Application.Interfaces.Ports.LlmProfiles.IsAuto(profile))
        {
            Orkeon.Application.Interfaces.Ports.LlmProfiles.EnsureKnown(_llmProfiles, profile, where);
            return;
        }

        if (_routeLadder.Count == 0)
            throw new InvalidOperationException(
                $"{where} names the LLM profile '{Orkeon.Application.Interfaces.Ports.LlmProfiles.Auto}', and this host declares no " +
                $"{Orkeon.Application.Interfaces.Ports.LlmProfiles.RouteLadderKey}: list there the profiles System One may pick, " +
                "cheapest first, or name a profile.");

        foreach (var rung in _routeLadder)
            Orkeon.Application.Interfaces.Ports.LlmProfiles.EnsureKnown(
                _llmProfiles, rung, $"{where} names '{Orkeon.Application.Interfaces.Ports.LlmProfiles.Auto}', and " +
                Orkeon.Application.Interfaces.Ports.LlmProfiles.RouteLadderKey);
    }

    /// <summary>
    /// The rules of the crew's <c>decision.mode</c> with what only the host knows, its decision profiles (#429):
    /// a refusal fails the load, a warning goes to <see cref="CrewFactoryOptions.OnDecisionWarning"/> when set,
    /// to the log otherwise.
    /// </summary>
    private void ValidateDecisionMode(CrewConfiguration config)
    {
        var profiles = _decisionProfiles;
        // An agent on llm.profile: auto asks System One for its profile, as a decision profile does (#434), but
        // not a manager or a relay with no task of its own: it runs on the default profile.
        // Without the host's decision profiles, auto is still known: System One picks it.
        var uses = config.SystemOneUses(p => (profiles is not null && profiles.Contains(p, StringComparer.OrdinalIgnoreCase))
            || Orkeon.Application.Interfaces.Ports.LlmProfiles.IsAuto(p));
        var unroutedIds = UnroutedAgents(config);
        var unrouted = config.Agents.Where(a => unroutedIds.Contains(a.Id))
            .Select(a => $"agent '{a.Key ?? a.Role}'").ToHashSet(StringComparer.Ordinal);
        if (uses.DecisionProfiles is { } named && unrouted.Count > 0)
            uses = uses with
            {
                DecisionProfiles = named
                    .Where(u => !(unrouted.Contains(u.Holder) && Orkeon.Application.Interfaces.Ports.LlmProfiles.IsAuto(u.Profile)))
                    .ToList(),
            };
        var findings = Orkeon.Domain.Task.DecisionTaskRules.CheckMode(
            config.DecisionMode,
            config.Process,
            config.DecisionDeclarations(),
            uses,
            RelayNames(config));
        if (findings.Refusals.Count > 0)
            throw new InvalidOperationException(
                $"Crew '{config.Name}': {Orkeon.Domain.Task.DecisionTaskRules.ModeRefusal(findings.Refusals)}");

        foreach (var warning in findings.Warnings)
        {
            if (_onDecisionWarning is not null)
                _onDecisionWarning(warning);
            else
                LogCrewDefinitionWarning(warning);
        }
    }

    // The names a without.agent may give (#430), as the loader checks them: an agent's key, or the role of an
    // agent without one, which a crew built in code is matched on at run time.
    private static List<string> RelayNames(CrewConfiguration config) =>
        [.. config.Agents.Select(a => a.Key ?? a.Role).Distinct(StringComparer.Ordinal)];

    private static string Shorten(string text) => text.Length <= 60 ? text : string.Concat(text.AsSpan(0, 57), "...");

    private void ValidateConfiguration(CrewConfiguration config)
    {
        var validation = _loader.Validate(config);
        if (!validation.IsValid)
        {
            var errorMessages = string.Join("; ", validation.Errors);
            throw new InvalidOperationException($"Invalid crew configuration: {errorMessages}");
        }

        foreach (var warning in validation.Warnings)
        {
            LogCrewDefinitionWarning(warning);
        }
    }

    /// <param name="agentConfigs">The agents to create.</param>
    /// <param name="defaultKnowledgeProfile">
    /// The crew's <c>rag.defaults.profile</c>: copied onto every knowledge attachment that
    /// names no profile of its own (GAP-02). <c>null</c> leaves those to the host default.
    /// </param>
    /// <param name="refuseWithheld">Whether a withheld tool fails a strict load (#429: not for a required crew).</param>
    /// <param name="ct">Cancellation token.</param>
    private async Task<Dictionary<string, DomainAgent>> CreateAgentsAsync(
        IReadOnlyList<AgentConfiguration> agentConfigs, string? defaultKnowledgeProfile, bool refuseWithheld, CancellationToken ct)
    {
        var agentMap = new Dictionary<string, DomainAgent>();

        foreach (var agentConfig in agentConfigs)
        {
            ct.ThrowIfCancellationRequested();
            var resolvedTools = await ResolveToolsAsync(agentConfig.Tools, refuseWithheld, ct).ConfigureAwait(false);

            var agent = BuildAgent(agentConfig, resolvedTools, defaultKnowledgeProfile);

            agentMap[agentConfig.Id] = agent;

            LogCreatedAgentWithId(agentConfig.Role, agent.Id);
        }

        return agentMap;
    }

    /// <summary>One agent as its configuration declares it, its tools already resolved.</summary>
    private static DomainAgent BuildAgent(
        AgentConfiguration agentConfig, List<IBaseTool> resolvedTools, string? defaultKnowledgeProfile)
    {
        var builder = new AgentBuilder()
            .Role(AgentRole.From(agentConfig.Role))
            .Goal(AgentGoal.From(agentConfig.Goal))
            .AllowDelegation(agentConfig.AllowDelegation)
            .MaxIterations(agentConfig.MaxIterations)
            .Verbose(agentConfig.Verbose);

        // As declared: the validation refused zero and less (GAP-38); none is no limit of its own.
        if (agentConfig.MaxRpm is { } maxRpm)
            builder.MaxRpm(maxRpm);
        if (agentConfig.MaxExecutionTime is { } maxExecutionTime)
            builder.MaxExecutionTime(maxExecutionTime);

        if (!string.IsNullOrWhiteSpace(agentConfig.Backstory))
            builder.Backstory(agentConfig.Backstory);
        if (agentConfig.PermissionMode is { } permissionMode)
            builder.PermissionMode(permissionMode);
        if (!string.IsNullOrWhiteSpace(agentConfig.WorkingRoot))
            builder.WorkingRoot(agentConfig.WorkingRoot);
        if (agentConfig.FileSystem.Count > 0)
            builder.FileSystem([.. agentConfig.FileSystem]);
        builder.AnnounceMounts(agentConfig.AnnounceMounts);
        builder.Compaction(agentConfig.Compaction);
        builder.Skills(agentConfig.Skills);
        if (resolvedTools.Count > 0)
            builder.WithTools(resolvedTools);
        // The agent's llm block — profile, model, sampling, thinking, response format, cache.
        // It stopped here until GAP-17: parsed from YAML and .ork.ts, mapped onto the
        // configuration, and never set on the agent, so every agent of a loaded crew ran on
        // the host's settings whatever its llm block said.
        if (agentConfig.LlmConfig is not null)
            builder.WithLlmConfig(agentConfig.LlmConfig);
        foreach (var attachment in agentConfig.KnowledgeAttachments)
            builder.WithKnowledge(WithDefaultProfile(attachment, defaultKnowledgeProfile));
        // The agent's guardrails, rendered before its task's in every prompt. They stopped here
        // until GAP-42: mapped from YAML onto the configuration and never set on the agent, so
        // the model read the task's guardrails alone.
        if (agentConfig.Guardrails is not null)
            builder.WithGuardrails(agentConfig.Guardrails);
        ApplyTemplates(builder, agentConfig);

        return builder.Build();
    }

    /// <summary>A knowledge attachment naming no profile takes the crew's default, when it has one (GAP-02).</summary>
    private static KnowledgeAttachment WithDefaultProfile(KnowledgeAttachment attachment, string? defaultKnowledgeProfile)
    {
        if (attachment.Profile is null && defaultKnowledgeProfile is not null)
            return attachment with { Profile = defaultKnowledgeProfile };
        return attachment;
    }

    /// <summary>
    /// The templates no YAML key writes, which a host sets on a configuration built or
    /// adjusted in code (config with { … }): lost here without a word until GAP-42.
    /// PromptTemplate reaches an attribute nothing reads (GAP-43).
    /// </summary>
    private static void ApplyTemplates(AgentBuilder builder, AgentConfiguration agentConfig)
    {
        if (agentConfig.SystemTemplate is not null)
            builder.SystemTemplate(agentConfig.SystemTemplate);
        if (agentConfig.PromptTemplate is not null)
            builder.PromptTemplate(agentConfig.PromptTemplate);
        if (agentConfig.ResponseTemplate is not null)
            builder.ResponseTemplate(agentConfig.ResponseTemplate);
    }

    /// <summary>
    /// Builds the tasks. A task's <c>tools:</c> are resolved exactly like an agent's — same
    /// registry, same strict check — and travel on the task, which adds them to its agent's
    /// belt for that task only (GAP-07).
    /// </summary>
    private async Task<Dictionary<string, CrewTask>> CreateTasksAsync(
        IReadOnlyList<TaskConfiguration> taskConfigs, Dictionary<string, DomainAgent> agentMap,
        IReadOnlyDictionary<string, AgentId> agentIdByKey, bool refuseWithheld, CancellationToken ct)
    {
        var created = new Dictionary<string, CrewTask>();
        var idByName = new Dictionary<string, TaskId>(StringComparer.Ordinal);

        // Decision tasks first: a condition names its decision task and needs its id.
        foreach (var taskConfig in taskConfigs.Where(t => t.Decision is not null).Concat(taskConfigs.Where(t => t.Decision is null)))
        {
            ct.ThrowIfCancellationRequested();
            var resolvedTools = await ResolveToolsAsync(taskConfig.Tools, refuseWithheld, ct).ConfigureAwait(false);

            var task = BuildTask(taskConfig, resolvedTools, idByName, agentMap, agentIdByKey);

            created[taskConfig.Id] = task;
            if (taskConfig.Decision is not null)
                idByName[taskConfig.Key ?? taskConfig.Description] = task.Id;

            LogCreatedTaskWithDomainId(taskConfig.Id, task.Id);
        }

        var taskMap = new Dictionary<string, CrewTask>();
        foreach (var id in taskConfigs.Select(taskConfig => taskConfig.Id))
            taskMap[id] = created[id];

        return taskMap;
    }

    private static CrewTask BuildTask(
        TaskConfiguration taskConfig, IReadOnlyList<IBaseTool> resolvedTools,
        Dictionary<string, TaskId> idByName, Dictionary<string, DomainAgent> agentMap,
        IReadOnlyDictionary<string, AgentId> agentIdByKey)
    {
        var builder = new CrewTaskBuilder()
            .Description(TaskDescription.From(taskConfig.Description))
            .Async(taskConfig.AsyncExecution)
            .Retry(taskConfig.Retry)
            .HumanInput(taskConfig.HumanInput)
            .WithTools(resolvedTools)
            .WithKey(taskConfig.Key);

        // A decision task may declare no expected output, as the validator allows: the builder gives it one (#476).
        if (taskConfig.Decision is null || !string.IsNullOrWhiteSpace(taskConfig.ExpectedOutput))
            builder.ExpectedOutput(taskConfig.ExpectedOutput);

        if (taskConfig.Decision is { } decision)
        {
            // The relay's agent, by the id of the agent its key names; a key no agent has was refused at load.
            builder.WithDecision(decision.Without is { } relay && agentIdByKey.TryGetValue(relay.Agent, out var relayAgentId)
                ? decision.WithRelay(relay.ResolvedTo(relayAgentId))
                : decision);
        }

        if (taskConfig.Condition is not null)
        {
            if (idByName.TryGetValue(taskConfig.Condition.DecisionTask, out var decisionId))
                builder.When(decisionId, taskConfig.Condition);
            else
                builder.When(taskConfig.Condition);
        }

        if (taskConfig.AssignedAgentId is not null
            && agentMap.TryGetValue(taskConfig.AssignedAgentId.ToString(), out var assignedAgent))
        {
            builder.AssignTo(assignedAgent);
        }

        var task = builder.Build();

        if (taskConfig.Deliverable is not null)
            task.SetDeliverable(taskConfig.Deliverable);

        if (taskConfig.LlmOverride is not null)
            task.SetLlmOverride(taskConfig.LlmOverride);

        if (taskConfig.Guardrails is not null)
            task.SetGuardrails(taskConfig.Guardrails);

        return task;
    }

    private static void SetupTaskDependencies(
        IReadOnlyList<TaskConfiguration> taskConfigs, Dictionary<string, CrewTask> taskMap)
    {
        foreach (var taskConfig in taskConfigs)
        {
            if (taskConfig.Dependencies.Count == 0 || !taskMap.TryGetValue(taskConfig.Id, out var task))
                continue;

            foreach (var depId in taskConfig.Dependencies)
            {
                if (taskMap.TryGetValue(depId, out var depTask))
                    task.AddDependency(depTask.Id);
            }
        }
    }

    private static void AddTaskContextData(
        IReadOnlyList<TaskConfiguration> taskConfigs, Dictionary<string, CrewTask> taskMap)
    {
        foreach (var taskConfig in taskConfigs)
        {
            if (taskConfig.Context.Count == 0 || !taskMap.TryGetValue(taskConfig.Id, out var task))
                continue;

            foreach (var kvp in taskConfig.Context)
            {
                task.AddContext(kvp.Key, kvp.Value);
            }
        }
    }

    private static DomainCrew AssembleCrew(
        CrewConfiguration config,
        Dictionary<string, DomainAgent> agentMap,
        Dictionary<string, CrewTask> taskMap)
    {
        var builder = new CrewBuilder()
            .Goal(config.Goal)
            .Process(config.Process)
            .Verbose(config.Verbose)
            .Planning(config.Planning)
            .EnableMemory(config.Memory)
            .WithAgents(agentMap.Values)
            .WithTasks(taskMap.Values);

        // The crew's maxRpm bounds the model requests of all its agents and its manager (GAP-38).
        if (config.MaxRpm is { } maxRpm)
            builder.MaxRpm(maxRpm);

        // The budget one autonomous run of the crew spends (#218).
        if (config.Budget is not null)
            builder.Budget(config.Budget);

        // Whether the crew needs System One: a required crew is refused before its first task when the
        // decision circuit is off (#429).
        builder.DecisionMode(config.DecisionMode);

        // The graph config is a crew-definition setting that must survive to execution time —
        // the GraphProcessStrategy reads it off the domain crew (P2-O-01).
        if (config.GraphConfig is not null)
            builder.WithGraphConfig(config.GraphConfig);

        // The declared memory provider must survive to kickoff, where it is resolved to a concrete
        // IMemoryProvider (P2-O-02). Dropped here previously, so the choice never reached the run.
        if (!string.IsNullOrWhiteSpace(config.MemoryProvider))
            builder.WithMemoryProvider(config.MemoryProvider);

        // So must the crew's name: it is the scope of the crew's long-term memory in that shared
        // provider, recorded at kickoff next to the type (GAP-20).
        if (!string.IsNullOrWhiteSpace(config.Name))
            builder.Name(config.Name);

        // The manager, as configured: the validation refused it in a mode that has none (GAP-33), and
        // the builder would too. A consensual crew's manager is the arbiter of ManagerDecision (GAP-04).
        if (config.ManagerAgentId is not null
            && agentMap.TryGetValue(config.ManagerAgentId.ToString(), out var managerAgent))
        {
            builder.WithManager(managerAgent);
        }

        return builder.Build();
    }

    /// <inheritdoc />
    public Task<DomainCrew> CreateFromFileAsync(
        string yamlFilePath,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yamlFilePath);

        return CreateFromFileCoreAsync(yamlFilePath, ct);

        async Task<DomainCrew> CreateFromFileCoreAsync(string yamlFilePath, CancellationToken ct)
        {
            var config = await _loader.LoadFromFileAsync(yamlFilePath, ct).ConfigureAwait(false);
            return await CreateFromConfigAsync(config, ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public Task<DomainCrew> CreateFromDirectoryAsync(
        string directoryPath,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);

        return CreateFromDirectoryCoreAsync(directoryPath, ct);

        async Task<DomainCrew> CreateFromDirectoryCoreAsync(string directoryPath, CancellationToken ct)
        {
            var config = await _loader.LoadFromDirectoryAsync(directoryPath, ct).ConfigureAwait(false);
            return await CreateFromConfigAsync(config, ct).ConfigureAwait(false);
        }
    }

    private async Task<List<IBaseTool>> ResolveToolsAsync(
        IReadOnlyList<string> toolNames, bool refuseWithheld, CancellationToken ct)
    {
        var tools = new List<IBaseTool>();
        if (toolNames == null || toolNames.Count == 0)
            return tools;

        List<string>? missing = null;
        List<string>? withheld = null;
        foreach (var toolName in toolNames)
        {
            if (WithheldReason(toolName) is { } reason)
            {
                var message = $"tool '{toolName}' withheld: {reason}";
                if (_onWithheldTool is not null)
                    _onWithheldTool(message);
                else if (_strictTools && refuseWithheld)
                    (withheld ??= []).Add(message);
                else
                    LogToolWithheld(toolName, reason);
                continue;
            }

            var tool = await _toolRegistry.GetToolByNameAsync(toolName, ct).ConfigureAwait(false);
            if (tool is not null)
            {
                tools.Add(tool);
            }
            else
            {
                missing ??= [];
                missing.Add(toolName);
                if (!_strictTools)
                    LogToolNotFoundInRegistry(toolName);
            }
        }

        if (withheld is { Count: > 0 })
            throw new InvalidOperationException($"Crew configuration references {string.Join("; ", withheld)}.");
        if (_strictTools && missing is { Count: > 0 })
            throw await BuildMissingToolsExceptionAsync(missing, ct).ConfigureAwait(false);

        return tools;
    }

    /// <summary>
    /// Why the host withholds a tool a crew names, or null when it offers it: <c>system_one_decide</c>
    /// with the decision circuit off (#433), whatever the cause - the switch, the settings, no provider.
    /// </summary>
    private string? WithheldReason(string toolName) =>
        _decisionCircuit is { Enabled: false } circuit
        && string.Equals(toolName, Orkeon.Infrastructure.Tools.SystemOneDecideTool.ToolName, StringComparison.OrdinalIgnoreCase)
            ? circuit.OffReason
            : null;

    private async Task<InvalidOperationException> BuildMissingToolsExceptionAsync(IReadOnlyList<string> missing, CancellationToken ct)
    {
        var available = await _toolRegistry.GetAllToolsAsync(ct).ConfigureAwait(false);
        var availableNames = available.Count > 0
            ? string.Join(", ", available.Select(t => t.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            : "(none registered)";
        return new InvalidOperationException(
            $"Crew configuration references unknown tool(s): {string.Join(", ", missing)}. " +
            $"Available tools: {availableNames}.");
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Creating crew '{CrewName}' with {AgentCount} agents and {TaskCount} tasks")]
    private partial void LogCreatingCrewWithAgentsAnd(object crewName, int agentCount, int taskCount);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Successfully created crew '{CrewName}' with id {CrewId}")]
    private partial void LogSuccessfullyCreatedCrewWithId(object crewName, object crewId);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Crew definition warning: {Warning}")]
    private partial void LogCrewDefinitionWarning(object warning);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Created agent '{AgentRole}' with id {AgentId}")]
    private partial void LogCreatedAgentWithId(object agentRole, object agentId);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Created task '{TaskId}' with domain id {DomainTaskId}")]
    private partial void LogCreatedTaskWithDomainId(object taskId, object domainTaskId);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Tool '{ToolName}' not found in registry; skipping.")]
    private partial void LogToolNotFoundInRegistry(object toolName);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Tool '{ToolName}' withheld: {Reason}; skipping.")]
    private partial void LogToolWithheld(string toolName, string reason);

}
