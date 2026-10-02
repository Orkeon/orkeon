namespace Orkeon.Application.Memory;

/// <summary>
/// The bounds of what a crew recalls before each task (GAP-30), bound to the
/// <see cref="SectionName"/> section by <c>AddOrkeonInfrastructure()</c>. A crew with
/// <c>memory: true</c> recalls at most <see cref="RecallLimit"/> of its memories, those whose cosine
/// similarity to the task reaches <see cref="MinScore"/>, cut to <see cref="MaxChars"/> characters in
/// all.
/// </summary>
public sealed class CrewMemoryOptions
{
    /// <summary>The configuration section: <c>Orkeon:CrewMemory</c>.</summary>
    public const string SectionName = "Orkeon:CrewMemory";

    /// <summary>
    /// How many memories a task recalls at most. 5 by default; 0 recalls nothing (the crew still
    /// stores what its tasks produce).
    /// </summary>
    public int RecallLimit { get; set; } = 5;

    /// <summary>
    /// The least cosine similarity between a task and a memory for the memory to be recalled.
    /// 0.6 by default, measured on the local embedding model (BGE-micro-v2), on which the same task
    /// of an earlier run scores 0.72 and above and an unrelated English task 0.53 and below. Its
    /// scale is the embedder's: another model needs its own measure.
    /// </summary>
    public float MinScore { get; set; } = 0.6f;

    /// <summary>
    /// How many characters of memory content a task's prompt receives at most, all recalled
    /// memories together. 4,000 by default; the last memory that does not fit is cut.
    /// </summary>
    public int MaxChars { get; set; } = 4_000;
}
