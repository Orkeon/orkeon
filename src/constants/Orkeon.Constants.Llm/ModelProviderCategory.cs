namespace Orkeon.Constants.Llm;

/// <summary>
/// What a model provider is called for. Each category has its own contract — a provider that
/// generates text and one that returns typed decisions share no call shape — so the category
/// names a capability, not a vendor: OpenAI and Ollama are in two of them, TypeSafe in one.
/// </summary>
public enum ModelProviderCategory
{
    /// <summary>Generates text, calls tools and streams: <c>ILlmProvider</c>.</summary>
    Generation,

    /// <summary>Turns text into vectors: <c>IEmbeddingProvider</c>.</summary>
    Embedding,

    /// <summary>Reorders retrieval candidates by relevance: <c>IReranker</c>.</summary>
    Reranking,

    /// <summary>
    /// Answers typed questions about a state with calibrated probabilities and a confidence,
    /// and generates no text (a "System One" model): <c>IDecisionModel</c>.
    /// </summary>
    Decision,
}
