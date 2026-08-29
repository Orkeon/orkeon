namespace Orkeon.Domain.Task.ValueObjects;

/// <summary>
/// How a task's deliverable file is produced and persisted to the file system.
/// </summary>
public enum DeliverableSource
{
    /// <summary>The task has no file-level deliverable (rare — most tasks produce a file).</summary>
    None,

    /// <summary>
    /// Legacy mode: the agent must invoke a <c>file_write</c> tool call explicitly.
    /// Suitable for multi-file or conditional writes.
    /// </summary>
    ToolCall,

    /// <summary>
    /// The framework writes the agent's final assistant message to the declared path.
    /// Reliable for large markdown/text deliverables: the model does not have to
    /// serialize the content inside a tool_call argument.
    /// </summary>
    FinalMessage,

    /// <summary>
    /// The framework invokes the LLM with a grammar / JSON-Schema constraint so the
    /// final message is a valid JSON document, then writes it to the declared path.
    /// </summary>
    StructuredOutput
}
