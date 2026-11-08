using System.Globalization;
using System.Text.Json.Serialization;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Memory;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.Common;
using Orkeon.Domain.Memory;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Infrastructure.Tools;

/// <summary>Typed request for <see cref="MemoryRecallTool"/>.</summary>
public sealed class MemoryRecallRequest
{
    /// <summary>What to look for in the crew's memory.</summary>
    [JsonPropertyName("query")]
    [FieldSchema(Description = "What to look for in what this crew remembers, in a few words.", Example = "the deployment decision", IsRequired = true)]
    public string Query { get; set; } = string.Empty;

    /// <summary>The most memories to return.</summary>
    [JsonPropertyName("limit")]
    [FieldSchema(Description = "The most memories to return (default 5).", Example = "5", IsRequired = false)]
    public int Limit { get; set; } = 5;
}

/// <summary>One memory <see cref="MemoryRecallTool"/> returns.</summary>
public sealed class RecalledMemoryEntry
{
    /// <summary>What was remembered.</summary>
    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    /// <summary>Composite score: similarity, recency and importance together.</summary>
    [JsonPropertyName("score")]
    public float Score { get; set; }

    /// <summary>When it was stored (round-trip UTC).</summary>
    [JsonPropertyName("stored_at")]
    public string StoredAt { get; set; } = string.Empty;
}

/// <summary>Typed response for <see cref="MemoryRecallTool"/>.</summary>
public sealed class MemoryRecallResponse
{
    /// <summary>Whether the recall ran.</summary>
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    /// <summary>The memories, best first.</summary>
    [JsonPropertyName("memories")]
    public IReadOnlyList<RecalledMemoryEntry> Memories { get; set; } = [];

    /// <summary>Failure reason when not successful.</summary>
    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

/// <summary>
/// Recalls what the crew of the call remembers, ranked by composite score (FWK-04). Held only by a
/// crew with <c>memory: true</c> when the host wires the cognitive memory; distinct from
/// <c>memory_store</c>, the typed notes of the operator. The crew is the one the run meters its calls under, and
/// the tool answers only for a crew that remembers (<c>memory: true</c>): a script's <c>act</c> that lists it
/// under a crew with <c>memory: false</c> reads nothing.
/// </summary>
public sealed class MemoryRecallTool : ToolBase<MemoryRecallRequest, MemoryRecallResponse>
{
    private const int DefaultLimit = 5;
    private const int MaxLimit = 20;

    private readonly ICognitiveMemoryService _memory;
    private readonly CrewMemoryOptions _options;
    private readonly CrewMemoryProviderRegistry? _registry;

    /// <summary>Creates the tool over the host's cognitive memory.</summary>
    /// <param name="memory">The cognitive memory.</param>
    /// <param name="options">The recall floors (<c>Orkeon:CrewMemory</c>); the defaults when null.</param>
    /// <param name="registry">What each crew declared about its memory: the tool refuses a crew that does not remember. Null checks nothing.</param>
    public MemoryRecallTool(
        ICognitiveMemoryService memory,
        Microsoft.Extensions.Options.IOptions<CrewMemoryOptions>? options = null,
        CrewMemoryProviderRegistry? registry = null)
    {
        _memory = memory ?? throw new ArgumentNullException(nameof(memory));
        _options = options?.Value ?? new CrewMemoryOptions();
        _registry = registry;
    }

    /// <inheritdoc />
    public override string Name => "memory_recall";

    /// <inheritdoc />
    public override string Description =>
        "Recall what this crew remembers about a subject: its memories ranked by relevance, recency and importance.";

    /// <inheritdoc />
    public override ToolAccess Access => ToolAccess.Read;

    /// <inheritdoc />
    protected override async Task<MemoryRecallResponse> ExecuteTypedAsync(
        MemoryRecallRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Query))
            return new MemoryRecallResponse { Error = "query is required." };
        if (CurrentCrew() is not { } crew)
            return new MemoryRecallResponse { Error = "memory_recall runs inside a crew's run only." };
        if (_registry is not null && !_registry.IsMemoryEnabled(crew))
            return new MemoryRecallResponse { Error = "memory_recall answers only for a crew that remembers (memory: true)." };

        var limit = Math.Clamp(request.Limit <= 0 ? DefaultLimit : request.Limit, 1, MaxLimit);
        var found = await _memory.RecallAsync(crew, request.Query, new RecallOptions
            {
                TopK = limit,
                MinScore = _options.MinCompositeScore,
                MinSemanticScore = _options.MinSemanticScore,
            }, cancellationToken)
            .ConfigureAwait(false);

        return new MemoryRecallResponse
        {
            Success = true,
            Memories =
            [
                .. found.Take(limit).Select(hit => new RecalledMemoryEntry
                {
                    Content = hit.Item.Content,
                    Score = hit.CompositeScore,
                    StoredAt = hit.Item.Timestamp.ToString("O", CultureInfo.InvariantCulture),
                }),
            ],
        };
    }

    /// <summary>The crew the run meters its calls under, or null outside a run.</summary>
    private static CrewId? CurrentCrew()
    {
        var id = LlmUsageScope.Current.CrewId;
        if (string.IsNullOrEmpty(id))
            return null;
        try
        {
            return CrewId.Parse(id);
        }
        catch (FormatException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
