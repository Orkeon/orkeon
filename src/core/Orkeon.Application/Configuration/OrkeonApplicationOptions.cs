using Orkeon.Application.Constants.Configuration;
using static Orkeon.Domain.Constants.Llm.EmbeddingDefaults;
using static Orkeon.Domain.Constants.Llm.LlmDefaults;
using Orkeon.Domain.Constants.Task;
using Orkeon.Domain.Constants.Agent;
using Orkeon.Domain.Constants.Llm;

namespace Orkeon.Application.Configuration;

/// <summary>
/// Options for configuring the Orkeon Application layer.
/// </summary>
public class OrkeonApplicationOptions
{
    /// <summary>
    /// Type of crew repository to use (Yaml or Json).
    /// </summary>
    public RepositoryType CrewRepositoryType { get; set; } = RepositoryType.Yaml;

    /// <summary>
    /// Path to crew configuration files.
    /// </summary>
    public string CrewsPath { get; set; } = PathDefaults.DefaultCrewsPath;

    /// <summary>
    /// Default max iterations for agents.
    /// </summary>
    public int DefaultMaxIterations { get; set; } = AgentDefaults.MaxIterations;

    /// <summary>
    /// Gets or sets the dimension for embedding vectors.
    /// Default is <see cref="EmbeddingDefaults.LocalDimension"/> (384) for compatibility with sentence-transformers models.
    /// </summary>
    public int EmbeddingDimension { get; set; } = LocalDimension;

    /// <summary>
    /// Type of embedding provider to use ("Simple", "OpenAI", "AzureOpenAI").
    /// Provider configuration is handled by Infrastructure layer.
    /// </summary>
    public string EmbeddingProvider { get; set; } = FallbackProvider;

    /// <summary>
    /// OpenAI API key for embeddings (if using OpenAI provider).
    /// </summary>
    public string? OpenAIApiKey { get; set; }

    /// <summary>
    /// OpenAI embedding model name (default: text-embedding-ada-002).
    /// </summary>
    public string OpenAIEmbeddingModel { get; set; } = DefaultOpenAIModel;

    /// <summary>
    /// Azure OpenAI endpoint (if using Azure OpenAI).
    /// </summary>
    public Uri? AzureOpenAIEndpoint { get; set; }

    /// <summary>
    /// Azure OpenAI deployment name (if using Azure OpenAI).
    /// </summary>
    public string? AzureOpenAIDeploymentName { get; set; }

    /// <summary>
    /// Gets or sets whether to enable debug logging.
    /// </summary>
    public bool EnableDebugLogging { get; set; }

    /// <summary>
    /// Gets or sets the default timeout for operations.
    /// </summary>
    public TimeSpan DefaultTimeout { get; set; } = TaskDefaults.DefaultOperationTimeout;

    /// <summary>
    /// Gets or sets the strategy used to select the best agent for a task.
    /// <para>
    /// <see cref="AgentSelectionStrategyKind.Embedding"/> uses semantic similarity.
    /// The default <c>IEmbeddingService</c> adapts the <c>IEmbeddingProvider</c> port,
    /// whose resolution is semantic-first (local BGE via <c>AddOrkeonLocalEmbeddings()</c>,
    /// else the <c>Orkeon:Embeddings</c> remote configuration, else fail-fast at first use).
    /// </para>
    /// </summary>
    public AgentSelectionStrategyKind AgentSelectionStrategy { get; set; } = AgentSelectionStrategyKind.FirstFit;
}

/// <summary>
/// Strategy used to select the best agent for a task.
/// </summary>
public enum AgentSelectionStrategyKind
{
    /// <summary>
    /// Selects the first available agent (safe fallback, no semantic ranking).
    /// </summary>
    FirstFit,

    /// <summary>
    /// Selects the agent whose profile embedding is most similar to the task
    /// (semantic selection — requires a real embedding provider).
    /// </summary>
    Embedding,

    /// <summary>
    /// Selects the agent whose role/goal/backstory keywords best match the task
    /// (lexical Jaccard similarity, no embedding provider required).
    /// </summary>
    Skill
}

/// <summary>
/// Repository type for crew configurations.
/// </summary>
public enum RepositoryType
{
    /// <summary>
    /// YAML file repository.
    /// </summary>
    Yaml,

    /// <summary>
    /// JSON file repository.
    /// </summary>
    Json
}
