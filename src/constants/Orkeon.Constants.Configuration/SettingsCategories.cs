namespace Orkeon.Constants.Configuration;

/// <summary>One category of settings: what an operator is trying to do, not how a key is spelt.</summary>
public sealed class SettingsCategory
{
    /// <summary>Creates a category.</summary>
    /// <param name="id">The stable identifier, lower case with hyphens (<c>rate-and-budgets</c>).</param>
    /// <param name="title">The English title (<c>Rate and budgets</c>).</param>
    public SettingsCategory(string id, string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        Id = id;
        Title = title;
    }

    /// <summary>The stable identifier, lower case with hyphens (<c>rate-and-budgets</c>).</summary>
    public string Id { get; }

    /// <summary>The English title (<c>Rate and budgets</c>).</summary>
    public string Title { get; }
}

/// <summary>
/// The categories the settings are filed under, by use: the model-call limits, the tool-call limits
/// and the token budget sit side by side, which neither their spelling (<c>RateLimiting</c> at the
/// root, <c>Orkeon:CostTracking</c> under a prefix) nor the project that reads them gives.
/// <para>
/// A section belongs to one category. A section a component starts to read is added to
/// <see cref="SettingsSections.Known"/> when it sits under a container, and here in every case, the
/// configuration root included: a test refuses a declared section this table does not file.
/// </para>
/// </summary>
public static class SettingsCategories
{
    /// <summary>The model: the default profile, the named ones, what a call costs, how it is logged.</summary>
    public const string Models = "models";

    /// <summary>How fast and how much: calls per minute, concurrent calls, token budgets.</summary>
    public const string RateAndBudgets = "rate-and-budgets";

    /// <summary>Where memory lives: the provider, the vector stores, the embeddings.</summary>
    public const string MemoryAndVectors = "memory-and-vectors";

    /// <summary>Retrieval-augmented generation: profiles, ingestion, retrieval, the web fallback.</summary>
    public const string Rag = "rag";

    /// <summary>What a run may touch on disk and where code runs: mounts, the sandboxes, path rules.</summary>
    public const string FilesAndSandbox = "files-and-sandbox";

    /// <summary>What is screened, audited, encrypted and gated.</summary>
    public const string Security = "security";

    /// <summary>The tools an agent is given: shell, e-mail, MCP servers, the code index, web search.</summary>
    public const string Tools = "tools";

    /// <summary>How a crew is built, votes and keeps its state.</summary>
    public const string OrchestrationAndPersistence = "orchestration-and-persistence";

    /// <summary>The scripting sandbox and the interactive console.</summary>
    public const string ScriptsAndConsole = "scripts-and-console";

    /// <summary>The service host daemon and the agent-to-agent server.</summary>
    public const string ServiceHostAndA2A = "service-host-and-a2a";

    /// <summary>Logs, traces and metrics.</summary>
    public const string Observability = "observability";

    /// <summary>The eleven categories, in the order every listing follows.</summary>
    public static IReadOnlyList<SettingsCategory> All { get; } =
    [
        new(Models, "Models"),
        new(RateAndBudgets, "Rate and budgets"),
        new(MemoryAndVectors, "Memory and vectors"),
        new(Rag, "RAG"),
        new(FilesAndSandbox, "Files and sandbox"),
        new(Security, "Security"),
        new(Tools, "Tools"),
        new(OrchestrationAndPersistence, "Orchestration and persistence"),
        new(ScriptsAndConsole, "Scripts and console"),
        new(ServiceHostAndA2A, "Service host and A2A"),
        new(Observability, "Observability"),
    ];

    /// <summary>
    /// The category of every section Orkeon reads, by the section's full path. A section declared
    /// below another (<c>Orkeon:Rag:Hybrid</c>) follows the one above it unless it has a line of its own.
    /// </summary>
    public static IReadOnlyDictionary<string, string> BySection { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Llm"] = Models,
            ["LlmLogging"] = Models,
            ["Orkeon:CostTracking"] = Models,
            ["Orkeon:TokenCounter"] = Models,
            ["Evaluation"] = Models,

            ["RateLimiting"] = RateAndBudgets,
            ["ToolRateLimiting"] = RateAndBudgets,
            ["TokenBudget"] = RateAndBudgets,

            ["Memory"] = MemoryAndVectors,
            ["Orkeon:Redis"] = MemoryAndVectors,
            ["Orkeon:Sqlite"] = MemoryAndVectors,
            ["Orkeon:ChromaDb"] = MemoryAndVectors,
            ["Orkeon:Pinecone"] = MemoryAndVectors,
            ["Orkeon:LanceDb"] = MemoryAndVectors,
            ["Orkeon:CrewMemory"] = MemoryAndVectors,
            ["Orkeon:CognitiveMemory"] = MemoryAndVectors,
            ["Orkeon:Encryption"] = MemoryAndVectors,
            ["Orkeon:Embeddings"] = MemoryAndVectors,
            ["Orkeon:EmbeddingCache"] = MemoryAndVectors,
            ["Orkeon:VectorSearch"] = MemoryAndVectors,

            ["Orkeon:Rag"] = Rag,

            ["Orkeon:FileSystem"] = FilesAndSandbox,
            ["Orkeon:Sandbox"] = FilesAndSandbox,
            ["PathSecurity"] = FilesAndSandbox,
            ["Orkeon:CodeSandbox"] = FilesAndSandbox,

            ["Security:Audit"] = Security,
            ["Security:Url"] = Security,
            ["Security:Vault"] = Security,
            ["Security:Prompt"] = Security,
            ["Security:ToolResults"] = Security,
            ["Orkeon:Guardian"] = Security,
            ["Orkeon:Security:PermissionGate"] = Security,
            ["Orkeon:Dlp"] = Security,
            ["Secrets"] = Security,

            ["Orkeon:Tools:Shell"] = Tools,
            ["Orkeon:Tools:Email"] = Tools,
            ["MCP"] = Tools,
            ["RaggableTree"] = Tools,
            ["BRAVE_API_KEY"] = Tools,
            ["Orkeon:MultiModal"] = Tools,
            ["Plugins"] = Tools,

            ["Orkeon:CrewFactory"] = OrchestrationAndPersistence,
            ["Orkeon:Consensus"] = OrchestrationAndPersistence,
            ["Orkeon:ExecutionState"] = OrchestrationAndPersistence,
            ["Orkeon:Checkpointing"] = OrchestrationAndPersistence,

            ["Orkeon:Scripting"] = ScriptsAndConsole,
            ["Orkeon:Cli"] = ScriptsAndConsole,

            ["Orkeon:Host"] = ServiceHostAndA2A,
            ["A2A"] = ServiceHostAndA2A,

            ["Logging"] = Observability,
            ["Telemetry"] = Observability,
            ["Orkeon:Monitoring"] = Observability,
        };

    /// <summary>
    /// The category of the section at <paramref name="sectionPath"/> — its own line of
    /// <see cref="BySection"/>, or that of the closest section above it —, or null when the table
    /// files neither.
    /// </summary>
    /// <param name="sectionPath">A section's full path, such as <c>Orkeon:Rag:Hybrid</c>.</param>
    public static string? Of(string sectionPath)
    {
        ArgumentNullException.ThrowIfNull(sectionPath);
        for (var candidate = sectionPath; candidate.Length > 0;)
        {
            if (BySection.TryGetValue(candidate, out var category))
                return category;

            var cut = candidate.LastIndexOf(':');
            candidate = cut < 0 ? string.Empty : candidate[..cut];
        }

        return null;
    }
}
