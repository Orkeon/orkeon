using static Orkeon.Application.Constants.Rag.RagDefaults;
using static Orkeon.Domain.Constants.Memory.SearchDefaults;

namespace Orkeon.Application.Rag;

/// <summary>
/// Combined options for all stages of the RAG pipeline.
/// </summary>
public class RagOptions
{
    /// <summary>
    /// Options for the retrieval stage.
    /// </summary>
    public RetrievalOptions Retrieval { get; set; } = new();

    /// <summary>
    /// Options for the context augmentation stage.
    /// </summary>
    public AugmentationOptions Augmentation { get; set; } = new();

    /// <summary>
    /// Options for the LLM generation stage.
    /// </summary>
    public GenerationOptions Generation { get; set; } = new();
}

/// <summary>
/// Options controlling how chunks are retrieved from knowledge sources.
/// </summary>
public class RetrievalOptions
{
    /// <summary>
    /// Maximum number of chunks to retrieve.
    /// </summary>
    public int TopK { get; set; } = DefaultTopK;

    /// <summary>
    /// Minimum relevance score threshold (0.0 to 1.0). Chunks below this are filtered out.
    /// </summary>
    public float MinRelevanceScore { get; set; } = (float)DefaultSimilarityThreshold;

    /// <summary>
    /// Whether to use hybrid search combining semantic and keyword matching.
    /// </summary>
    public bool UseHybridSearch { get; set; }

    /// <summary>
    /// Weight for semantic similarity in hybrid search (0.0 to 1.0).
    /// </summary>
    public float SemanticWeight { get; set; } = (float)DefaultVectorWeight;

    /// <summary>
    /// Weight for keyword matching in hybrid search (0.0 to 1.0).
    /// </summary>
    public float KeywordWeight { get; set; } = DefaultKeywordWeight;

    /// <summary>
    /// Optional filter to restrict retrieval to specific sources.
    /// </summary>
    public IReadOnlyList<string>? SourceFilter { get; init; }

    /// <summary>
    /// Optional metadata filters to apply during retrieval.
    /// </summary>
    public Dictionary<string, object>? MetadataFilter { get; init; }
}

/// <summary>
/// Options controlling how retrieved chunks are combined into a prompt.
/// </summary>
public class AugmentationOptions
{
    /// <summary>
    /// Maximum number of chunks to include in the prompt.
    /// </summary>
    public int MaxChunksInPrompt { get; set; } = DefaultMaxChunksInPrompt;

    /// <summary>
    /// Maximum estimated token budget for context.
    /// </summary>
    public int MaxContextTokens { get; set; } = DefaultMaxContextTokens;

    /// <summary>
    /// Template for the user prompt. Use {context} and {question} placeholders.
    /// </summary>
    public string PromptTemplate { get; set; } = DefaultPromptTemplate;

    /// <summary>
    /// Whether to include source references in the context.
    /// </summary>
    public bool IncludeSourceReferences { get; set; } = true;

    /// <summary>
    /// Whether to deduplicate chunks with identical content.
    /// </summary>
    public bool DeduplicateChunks { get; set; } = true;

    /// <summary>
    /// Default prompt template used when none is specified.
    /// </summary>
    public const string DefaultPromptTemplate = """
        Answer the following question based on the provided context.
        If the context doesn't contain enough information to answer, say so clearly.

        Context:
        {context}

        Question: {question}

        Answer:
        """;
}

/// <summary>
/// Options controlling LLM generation behavior.
/// </summary>
public class GenerationOptions
{
    /// <summary>
    /// Temperature for LLM generation. Lower values produce more deterministic output.
    /// </summary>
    public float Temperature { get; set; } = Orkeon.Application.Constants.Rag.RagDefaults.DefaultTemperature;

    /// <summary>
    /// Maximum tokens in the generated response.
    /// </summary>
    public int MaxTokens { get; set; } = Orkeon.Application.Constants.Rag.RagDefaults.DefaultMaxTokens;

    /// <summary>
    /// Optional custom system prompt. Overrides the default system prompt from augmentation.
    /// </summary>
    public string? SystemPrompt { get; set; }
}

/// <summary>
/// Configuration options for the RAG pipeline, typically bound from application settings.
/// </summary>
public class RagPipelineOptions
{
    /// <summary>
    /// Whether the RAG pipeline is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Default retrieval options.
    /// </summary>
    public RetrievalOptions DefaultRetrieval { get; set; } = new();

    /// <summary>
    /// Default augmentation options.
    /// </summary>
    public AugmentationOptions DefaultAugmentation { get; set; } = new();

    /// <summary>
    /// Default generation options.
    /// </summary>
    public GenerationOptions DefaultGeneration { get; set; } = new();
}
