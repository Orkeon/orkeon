namespace Orkeon.Rag.Abstractions.Options;

/// <summary>
/// Maximal Marginal Relevance (MMR) diversification options (RAG-05/C2, opt-in).
/// Bound from <c>Orkeon:Rag:Retrieval:Mmr</c>; consumed at the fusion/dedup stage
/// of the staged pipeline.
/// </summary>
/// <remarks>
/// MMR re-orders the fused candidates by trading relevance against redundancy:
/// each pick maximises <c>λ·relevance − (1 − λ)·max-similarity-to-already-picked</c>
/// (Carbonell &amp; Goldstein 1998). Disabled by default — candidates keep their
/// fused order.
/// </remarks>
public sealed class MmrOptions
{
    /// <summary>
    /// Default λ trade-off (<c>0.7</c> — relevance-leaning: 1.0 is pure relevance
    /// order, 0.0 is pure diversity).
    /// </summary>
    public const double DefaultLambda = 0.7;

    /// <summary>Whether MMR diversification runs at the fusion/dedup stage. Off by default.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Relevance/diversity trade-off in <c>[0, 1]</c>: <c>1</c> keeps the pure
    /// relevance order, <c>0</c> maximises diversity. Defaults to
    /// <see cref="DefaultLambda"/>.
    /// </summary>
    public double Lambda { get; set; } = DefaultLambda;
}
