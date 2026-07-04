using System.Collections.ObjectModel;
using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Infrastructure.CostTracking;

/// <summary>
/// Configuration options for cost tracking and budget management.
/// Bind to "Orkeon:CostTracking" configuration section.
/// </summary>
public class CostTrackingOptions
{
    /// <summary>
    /// Whether cost tracking is enabled (default: true).
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Default budget limit applied to all crews unless overridden.
    /// </summary>
    public BudgetLimit? DefaultCrewBudget { get; set; }

    /// <summary>
    /// Custom model pricings to override or supplement built-in defaults.
    /// </summary>
    public Collection<ModelPricing> CustomPricings { get; } = [];
}

/// <summary>
/// Configuration options for token counting approximation.
/// Bind to "Orkeon:TokenCounter" configuration section.
/// </summary>
public class TokenCounterOptions
{
    /// <summary>
    /// Average number of characters per token (default: 3.5).
    /// </summary>
    public float CharsPerToken { get; set; } = 3.5f;

    /// <summary>
    /// Number of overhead tokens per message in chat format (default: 4).
    /// </summary>
    public int TokensPerMessage { get; set; } = 4;

    /// <summary>
    /// Number of overhead tokens for the reply priming (default: 3).
    /// </summary>
    public int TokensPerReply { get; set; } = 3;

    /// <summary>
    /// Number of special tokens overhead (e.g., BOS/EOS) per request (default: 2).
    /// </summary>
    public int SpecialTokenOverhead { get; set; } = 2;
}
