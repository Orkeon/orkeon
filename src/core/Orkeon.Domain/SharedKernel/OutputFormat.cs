namespace Orkeon.Domain.SharedKernel;

/// <summary>
/// Output formats for task results.
/// </summary>
public enum OutputFormat
{
    /// <summary>
    /// Plain text output.
    /// </summary>
    Text,

    /// <summary>
    /// JSON formatted output.
    /// </summary>
    Json,

    /// <summary>
    /// Markdown formatted output.
    /// </summary>
    Markdown,

    /// <summary>
    /// YAML formatted output.
    /// </summary>
    Yaml,

    /// <summary>
    /// CSV formatted output.
    /// </summary>
    Csv,

    /// <summary>
    /// XML formatted output.
    /// </summary>
    Xml,

    /// <summary>
    /// Custom format defined by schema.
    /// </summary>
    Custom
}
