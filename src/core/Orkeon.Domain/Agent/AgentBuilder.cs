using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Common;
using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.Constants.Agent;
using Orkeon.Domain.Knowledge;
using Orkeon.Domain.Tools;

namespace Orkeon.Domain.Agent;

/// <summary>
/// Fluent builder for creating <see cref="Agent"/> instances.
/// Wraps <see cref="Agent.Create(AgentCreateOptions)"/> with a discoverable, chainable API.
/// </summary>
public sealed class AgentBuilder
{
    private AgentRole? _role;
    private AgentGoal? _goal;
    private AgentBackstory? _backstory;
    private bool _allowDelegation;
    private int _maxIterations = AgentDefaults.MaxIterations;
    private int _maxRpm = AgentDefaults.MaxRequestsPerMinute;
    private bool _verbose;
    private TimeSpan? _maxExecutionTime;
    private bool _cacheEnabled = true;
    private string? _systemTemplate;
    private string? _promptTemplate;
    private string? _responseTemplate;
    private int _maxRetryLimit = AgentDefaults.MaxRetryLimit;
    private ILlmProvider? _llm;
    private readonly List<IBaseTool> _tools = [];
    private ToolAccessPolicy? _toolAccessPolicy;
    private GuardrailsConfig? _guardrails;
    private LlmConfig? _llmConfig;
    private readonly List<KnowledgeAttachment> _knowledgeAttachments = [];

    /// <summary>Sets the agent role from a string value.</summary>
    public AgentBuilder Role(string role)
    {
        _role = AgentRole.From(role);
        return this;
    }

    /// <summary>Sets the agent role from an <see cref="AgentRole"/> value object.</summary>
    public AgentBuilder Role(AgentRole role)
    {
        _role = role;
        return this;
    }

    /// <summary>Sets the agent goal from a string value.</summary>
    public AgentBuilder Goal(string goal)
    {
        _goal = AgentGoal.From(goal);
        return this;
    }

    /// <summary>Sets the agent goal from an <see cref="AgentGoal"/> value object.</summary>
    public AgentBuilder Goal(AgentGoal goal)
    {
        _goal = goal;
        return this;
    }

    /// <summary>Sets the agent backstory from a string value.</summary>
    public AgentBuilder Backstory(string backstory)
    {
        _backstory = AgentBackstory.From(backstory);
        return this;
    }

    /// <summary>Sets the agent backstory from an <see cref="AgentBackstory"/> value object.</summary>
    public AgentBuilder Backstory(AgentBackstory backstory)
    {
        _backstory = backstory;
        return this;
    }

    /// <summary>Adds a single tool to the agent.</summary>
    public AgentBuilder WithTool(IBaseTool tool)
    {
        _tools.Add(tool);
        return this;
    }

    /// <summary>Adds multiple tools to the agent.</summary>
    public AgentBuilder WithTools(params IBaseTool[] tools)
    {
        _tools.AddRange(tools);
        return this;
    }

    /// <summary>Adds multiple tools to the agent from an enumerable.</summary>
    public AgentBuilder WithTools(IEnumerable<IBaseTool> tools)
    {
        _tools.AddRange(tools);
        return this;
    }

    /// <summary>Sets whether the agent is allowed to delegate tasks.</summary>
    public AgentBuilder AllowDelegation(bool allow = true)
    {
        _allowDelegation = allow;
        return this;
    }

    /// <summary>Sets the maximum number of iterations for the agent execution loop.</summary>
    public AgentBuilder MaxIterations(int maxIterations)
    {
        _maxIterations = maxIterations;
        return this;
    }

    /// <summary>Sets the maximum requests per minute for rate-limiting.</summary>
    public AgentBuilder MaxRpm(int maxRpm)
    {
        _maxRpm = maxRpm;
        return this;
    }

    /// <summary>Enables or disables verbose logging.</summary>
    public AgentBuilder Verbose(bool verbose = true)
    {
        _verbose = verbose;
        return this;
    }

    /// <summary>Sets the maximum execution time for tasks.</summary>
    public AgentBuilder MaxExecutionTime(TimeSpan maxExecutionTime)
    {
        _maxExecutionTime = maxExecutionTime;
        return this;
    }

    /// <summary>Enables or disables caching.</summary>
    public AgentBuilder CacheEnabled(bool cacheEnabled = true)
    {
        _cacheEnabled = cacheEnabled;
        return this;
    }

    /// <summary>Sets the system prompt template.</summary>
    public AgentBuilder SystemTemplate(string systemTemplate)
    {
        _systemTemplate = systemTemplate;
        return this;
    }

    /// <summary>Sets the prompt template.</summary>
    public AgentBuilder PromptTemplate(string promptTemplate)
    {
        _promptTemplate = promptTemplate;
        return this;
    }

    /// <summary>Sets the response template.</summary>
    public AgentBuilder ResponseTemplate(string responseTemplate)
    {
        _responseTemplate = responseTemplate;
        return this;
    }

    /// <summary>Sets the maximum retry limit on task execution failure.</summary>
    public AgentBuilder MaxRetryLimit(int maxRetryLimit)
    {
        _maxRetryLimit = maxRetryLimit;
        return this;
    }

    /// <summary>
    /// Sets the provider the agent's turns run on (<see cref="Agent.Llm"/>, CrewAI's <c>llm</c> given as
    /// an object, GAP-34): its tasks and their correction round, its ballot, its work as a hierarchical
    /// manager. The run builds a client over it once per provider instance and meters it as the agent's
    /// work — a provider already metered is read once per call. A task's <c>llm_override</c> profile
    /// still moves that task. It cannot be combined with a host profile
    /// (<c>WithLlmConfig(LlmConfig.OnProfile(name))</c>): <see cref="Build"/> refuses both. A provider
    /// that runs its own tools (<see cref="LlmProviderCapabilities.RunsOwnTools"/> — a Microsoft Agent
    /// Framework agent) leaves the agent no Orkeon tool and no delegation.
    /// </summary>
    public AgentBuilder WithLlm(ILlmProvider llmProvider)
    {
        _llm = llmProvider;
        return this;
    }

    /// <summary>
    /// Sets the per-agent LLM configuration. Honored by the executor as an override on the
    /// crew-level chat client defaults (model, sampling, thinking mode — Experiment 07 friction #7).
    /// </summary>
    public AgentBuilder WithLlmConfig(LlmConfig llmConfig)
    {
        _llmConfig = llmConfig;
        return this;
    }

    /// <summary>
    /// Enables (or disables) the provider's thinking/reasoning mode for this agent and optionally
    /// sets the reasoning-effort hint (<c>"low"</c>/<c>"medium"</c>/<c>"high"</c>/<c>"max"</c>).
    /// Sugar over <see cref="WithLlmConfig"/>: merges into the agent's LLM config without clobbering
    /// other settings, so it composes with <see cref="MaxOutputTokens"/> and an existing config. On
    /// an agent without one it names no model (<see cref="LlmConfig.OnProfile"/>): the agent runs on
    /// the host's model, whatever vendor serves it (GAP-18).
    /// </summary>
    public AgentBuilder Thinking(bool enabled = true, string? effort = null)
    {
        _llmConfig = (_llmConfig ?? LlmConfig.OnProfile()) with
        {
            Thinking = new LlmThinkingConfig { Enabled = enabled, Effort = effort },
        };
        return this;
    }

    /// <summary>
    /// Bounds the maximum number of output tokens the model may generate. Output is the dominant
    /// cost driver on thinking-mode providers (DeepSeek guideline §6.2), so cap it explicitly on
    /// agents that do not need long generations. Sugar over <see cref="WithLlmConfig"/>; on an
    /// agent without a config it names no model, like <see cref="Thinking"/>.
    /// </summary>
    public AgentBuilder MaxOutputTokens(int maxOutputTokens)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxOutputTokens);
        _llmConfig = (_llmConfig ?? LlmConfig.OnProfile()) with { MaxTokens = maxOutputTokens };
        return this;
    }

    /// <summary>
    /// Attaches a knowledge (RAG) collection to the agent with default retrieval options.
    /// Cumulative: call once per collection to attach several collections.
    /// </summary>
    /// <param name="collection">Name of the knowledge collection (required, never blank).</param>
    /// <exception cref="ArgumentException">When <paramref name="collection"/> is blank.</exception>
    public AgentBuilder WithKnowledge(string collection)
    {
        _knowledgeAttachments.Add(KnowledgeAttachment.Create(collection));
        return this;
    }

    /// <summary>
    /// Attaches a knowledge (RAG) collection to the agent with custom retrieval options.
    /// Cumulative: call once per collection to attach several collections.
    /// </summary>
    /// <param name="collection">Name of the knowledge collection (required, never blank).</param>
    /// <param name="configure">Configures TopK / MinScore / Profile / MaxContextTokens.</param>
    /// <example>
    /// <code>
    /// builder.WithKnowledge("produits", opts =>
    /// {
    ///     opts.TopK = 8;
    ///     opts.MinScore = 0.35;
    ///     opts.Profile = "quality";
    /// });
    /// </code>
    /// </example>
    /// <exception cref="ArgumentException">When <paramref name="collection"/> is blank or an option violates an invariant.</exception>
    public AgentBuilder WithKnowledge(string collection, Action<KnowledgeAttachmentOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var options = new KnowledgeAttachmentOptions();
        configure(options);
        _knowledgeAttachments.Add(KnowledgeAttachment.Create(
            collection, options.TopK, options.MinScore, options.Profile, options.MaxContextTokens));
        return this;
    }

    /// <summary>
    /// Attaches a pre-built (already validated) <see cref="KnowledgeAttachment"/>. Cumulative.
    /// </summary>
    public AgentBuilder WithKnowledge(KnowledgeAttachment attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        attachment.Validate();
        _knowledgeAttachments.Add(attachment);
        return this;
    }

    /// <summary>
    /// Sets a whitelist policy that only allows the specified tools.
    /// </summary>
    /// <param name="allowedTools">Tools that are allowed. Can include wildcard patterns like "File*".</param>
    public AgentBuilder WithToolWhitelist(params string[] allowedTools)
    {
        _toolAccessPolicy = ToolAccessPolicy.CreateWhitelist(allowedTools);
        return this;
    }

    /// <summary>
    /// Sets a whitelist policy that only allows the specified tools.
    /// </summary>
    /// <param name="allowedTools">Tools that are allowed. Can include wildcard patterns like "File*".</param>
    public AgentBuilder WithToolWhitelist(IEnumerable<string> allowedTools)
    {
        _toolAccessPolicy = ToolAccessPolicy.CreateWhitelist(allowedTools);
        return this;
    }

    /// <summary>
    /// Sets a blacklist policy that blocks the specified tools.
    /// </summary>
    /// <param name="blockedTools">Tools that are blocked. Can include wildcard patterns like "File*".</param>
    public AgentBuilder WithToolBlacklist(params string[] blockedTools)
    {
        _toolAccessPolicy = ToolAccessPolicy.CreateBlacklist(blockedTools);
        return this;
    }

    /// <summary>
    /// Sets a blacklist policy that blocks the specified tools.
    /// </summary>
    /// <param name="blockedTools">Tools that are blocked. Can include wildcard patterns like "File*".</param>
    public AgentBuilder WithToolBlacklist(IEnumerable<string> blockedTools)
    {
        _toolAccessPolicy = ToolAccessPolicy.CreateBlacklist(blockedTools);
        return this;
    }

    /// <summary>
    /// Sets an unrestricted policy that allows all tools.
    /// </summary>
    public AgentBuilder WithUnrestrictedToolAccess()
    {
        _toolAccessPolicy = ToolAccessPolicy.CreateUnrestricted();
        return this;
    }

    /// <summary>
    /// Sets a custom tool access policy.
    /// </summary>
    public AgentBuilder WithToolAccessPolicy(ToolAccessPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        _toolAccessPolicy = policy;
        return this;
    }

    /// <summary>
    /// Sets a pre-built guardrails configuration.
    /// </summary>
    public AgentBuilder WithGuardrails(GuardrailsConfig guardrails)
    {
        ArgumentNullException.ThrowIfNull(guardrails);
        _guardrails = guardrails;
        return this;
    }

    /// <summary>
    /// Configures guardrails using a fluent builder action.
    /// </summary>
    /// <example>
    /// <code>
    /// builder.WithGuardrails(g => g
    ///     .UsePreset(GuardrailPresets.Analysis)
    ///     .AddRule("Custom rule")
    ///     .WhenUsing("file_write", "Only write reports"));
    /// </code>
    /// </example>
    public AgentBuilder WithGuardrails(Action<GuardrailsBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var gb = new GuardrailsBuilder();
        configure(gb);
        _guardrails = gb.Build();
        return this;
    }

    /// <summary>
    /// Builds and returns a new <see cref="Agent"/> instance.
    /// </summary>
    /// <exception cref="BuilderValidationException">
    /// Thrown when <see cref="Role(string)"/> or <see cref="Goal(string)"/> have not been set, when
    /// <see cref="WithLlm"/> comes with a host profile, or when a provider that runs its own tools comes
    /// with <see cref="WithTool"/>, <see cref="WithTools(IBaseTool[])"/> or <see cref="AllowDelegation"/>.
    /// </exception>
    public Agent Build()
    {
        if (_role is null)
            throw new BuilderValidationException("Agent", "Role is required.");

        if (_goal is null)
            throw new BuilderValidationException("Agent", "Goal is required.");

        if (_llm is not null)
            ValidateOwnProvider(_llm);

        return Agent.Create(new AgentCreateOptions
        {
            Role = _role,
            Goal = _goal,
            Backstory = _backstory,
            AllowDelegation = _allowDelegation,
            MaxIterations = _maxIterations,
            MaxRpm = _maxRpm,
            Verbose = _verbose,
            MaxExecutionTime = _maxExecutionTime,
            CacheEnabled = _cacheEnabled,
            SystemTemplate = _systemTemplate,
            PromptTemplate = _promptTemplate,
            ResponseTemplate = _responseTemplate,
            MaxRetryLimit = _maxRetryLimit,
            Llm = _llm,
            Tools = _tools,
            ToolAccessPolicy = _toolAccessPolicy,
            Guardrails = _guardrails,
            LlmConfig = _llmConfig,
            KnowledgeAttachments = _knowledgeAttachments
        });
    }

    /// <summary>
    /// The agent's own provider against the rest of the builder (GAP-34), in the builder's words: a
    /// host profile besides it, or tools and delegation on a provider that runs its own tools.
    /// </summary>
    private void ValidateOwnProvider(ILlmProvider llm)
    {
        if (!AgentLlmRules.NamesNoProfile(_llmConfig?.Profile))
        {
            throw new BuilderValidationException(
                "Agent",
                $".WithLlm(provider) — or .WithAgentFrameworkAgent(agent) — sets the provider '{llm.Name}' the agent runs on, " +
                $"and .WithLlmConfig names the host profile '{_llmConfig!.Profile!.Trim()}': an agent runs on one or the other. " +
                "Keep the provider and leave the profile out (LlmConfig.OnProfile()), or keep the profile and drop .WithLlm.");
        }

        if (!llm.Capabilities.RunsOwnTools)
            return;

        var tools = _tools.Select(tool => tool.Name).ToList();
        if (_allowDelegation)
            tools.AddRange(AgentLlmRules.DelegationTools);
        if (tools.Count > 0)
        {
            throw new BuilderValidationException(
                "Agent",
                AgentLlmRules.OwnToolsRefusal(
                    $"The agent '{_role!.Value}' (.WithTool, .WithTools or .AllowDelegation)", llm.Name, tools));
        }
    }
}
