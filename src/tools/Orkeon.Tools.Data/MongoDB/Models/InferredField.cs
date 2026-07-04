namespace Orkeon.Tools.Data.MongoDB.Models;

/// <summary>
/// Represents a field inferred from sampling MongoDB documents.
/// </summary>
public record InferredField
{
    /// <summary>Gets the field name.</summary>
    public string Name { get; init; } = "";
    /// <summary>Gets the BSON type of the field.</summary>
    public string BsonType { get; init; } = "";
    /// <summary>Gets the frequency of occurrence across sampled documents (0.0–1.0).</summary>
    public double Frequency { get; init; }
    /// <summary>Gets whether this field is indexed.</summary>
    public bool IsIndexed { get; init; }
    /// <summary>Gets sample values observed for this field.</summary>
    public IReadOnlyList<string> SampleValues { get; init; } = [];
}
