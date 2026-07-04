namespace Orkeon.Domain.Task.ValueObjects;

/// <summary>
/// Declarative contract describing how a task's deliverable file is produced and where it lands.
/// Supports three production modes (see <see cref="DeliverableSource"/>):
/// legacy <c>tool_call</c>, <c>final_message</c>, and grammar-constrained <c>structured_output</c>.
/// </summary>
public sealed record TaskDeliverable
{
    /// <summary>Virtual path where the deliverable is persisted (e.g. <c>/output/02a_hotspots.md</c>).</summary>
    public required string Path { get; init; }

    /// <summary>How the deliverable content is produced.</summary>
    public required DeliverableSource Source { get; init; }

    /// <summary>Logical format (<c>markdown</c>, <c>json</c>, <c>text</c>). Used for sanitizer hints and telemetry.</summary>
    public string Format { get; init; } = "markdown";

    /// <summary>
    /// Whether to sanitize the persisted content (strip trailing template tokens such as
    /// <c>&lt;eos&gt;</c> and orphaned triple-quote fragments from botched tool_call attempts).
    /// Applicable to <see cref="DeliverableSource.FinalMessage"/>.
    /// </summary>
    public bool Sanitize { get; init; } = true;

    /// <summary>
    /// Virtual path to an external JSON Schema file. Required for
    /// <see cref="DeliverableSource.StructuredOutput"/> when <see cref="SchemaInline"/> is null.
    /// </summary>
    public string? SchemaPath { get; init; }

    /// <summary>
    /// Inline JSON Schema string. Alternative to <see cref="SchemaPath"/>.
    /// </summary>
    public string? SchemaInline { get; init; }

    /// <summary>
    /// Validates field combinations. Called by the YAML loader once a TaskDeliverable is constructed.
    /// </summary>
    /// <exception cref="InvalidOperationException">When the declaration is internally inconsistent.</exception>
    public void Validate()
    {
        if (Source != DeliverableSource.None && string.IsNullOrWhiteSpace(Path))
        {
            throw new InvalidOperationException(
                $"Deliverable with source '{Source}' requires a non-empty Path.");
        }

        if (Source == DeliverableSource.StructuredOutput
            && string.IsNullOrWhiteSpace(SchemaPath)
            && string.IsNullOrWhiteSpace(SchemaInline))
        {
            throw new InvalidOperationException(
                "StructuredOutput deliverable requires SchemaPath or SchemaInline.");
        }
    }
}
