using System.Text.Json;
using Orkeon.Domain.Constants.Serialization;

namespace Orkeon.Domain.Tools.Protocol;

/// <summary>
/// Strongly-typed wrapper around <see cref="ToolCallRequest"/> that deserializes the
/// <see cref="ToolCallRequest.Parameters"/> dictionary into a <typeparamref name="TParameters"/> instance.
/// Provides seamless conversion to/from the non-generic <see cref="ToolCallRequest"/>,
/// preserving full backward compatibility with the existing tool-call pipeline.
/// </summary>
/// <typeparam name="TParameters">
/// The strongly-typed parameters model. Must have a parameterless constructor so that
/// JSON deserialization can create instances.
/// </typeparam>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1000", Justification = "Idiomatic static factory on a generic wrapper type.")]
public record ToolCallRequest<TParameters> where TParameters : class, new()
{
#pragma warning disable S2743 // Static field intentionally per closed generic type (one cache per T)
    private static readonly JsonSerializerOptions SerializerOptions = new()
#pragma warning restore S2743
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        MaxDepth = SerializationDefaults.JsonMaxDepth
    };

    /// <summary>
    /// The underlying non-generic request. All original data is preserved here.
    /// </summary>
    public ToolCallRequest Inner { get; }

    /// <summary>
    /// The strongly-typed parameters deserialized from <see cref="ToolCallRequest.Parameters"/>.
    /// </summary>
    public TParameters TypedParameters { get; }

    /// <summary>Shortcut to <see cref="ToolCallRequest.ToolName"/>.</summary>
    public string ToolName => Inner.ToolName;

    /// <summary>Shortcut to <see cref="ToolCallRequest.Parameters"/>.</summary>
    public Dictionary<string, object?> Parameters => Inner.Parameters;

    /// <summary>Shortcut to <see cref="ToolCallRequest.Context"/>.</summary>
    public string? Context => Inner.Context;

    /// <summary>
    /// Creates a generic request by wrapping an existing non-generic request
    /// and deserializing its parameters into <typeparamref name="TParameters"/>.
    /// </summary>
    public ToolCallRequest(ToolCallRequest inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        Inner = inner;
        TypedParameters = DeserializeParameters(inner.Parameters);
    }

    /// <summary>
    /// Creates a generic request from explicit values.
    /// </summary>
    public ToolCallRequest(string toolName, TParameters typedParameters, string? context = null)
    {
        ArgumentNullException.ThrowIfNull(typedParameters);
        TypedParameters = typedParameters;
        Inner = new ToolCallRequest(toolName, SerializeToDict(typedParameters), context);
    }

    /// <summary>
    /// Converts this generic request back to the non-generic <see cref="ToolCallRequest"/>.
    /// </summary>
    public ToolCallRequest ToToolCallRequest() => Inner;

    /// <summary>
    /// Implicit conversion to the non-generic <see cref="ToolCallRequest"/> so that
    /// generic instances can be passed directly to APIs expecting the base type.
    /// </summary>
    public static implicit operator ToolCallRequest(ToolCallRequest<TParameters> generic)
    {
        ArgumentNullException.ThrowIfNull(generic);
        return generic.Inner;
    }

    /// <summary>
    /// Creates a <see cref="ToolCallRequest{TParameters}"/> from a non-generic request.
    /// </summary>
    public static ToolCallRequest<TParameters> FromToolCallRequest(ToolCallRequest request) =>
        new(request);

    // ── Private helpers ──────────────────────────────────────────────

    private static TParameters DeserializeParameters(Dictionary<string, object?> parameters)
    {
        var json = JsonSerializer.Serialize(parameters, SerializerOptions);
        return JsonSerializer.Deserialize<TParameters>(json, SerializerOptions)
               ?? throw new JsonException($"Failed to deserialize parameters to {typeof(TParameters).Name}");
    }

    private static Dictionary<string, object?> SerializeToDict(TParameters parameters)
    {
        var json = JsonSerializer.Serialize(parameters, SerializerOptions);
        return JsonSerializer.Deserialize<Dictionary<string, object?>>(json, SerializerOptions)
               ?? [];
    }
}
