using System.Collections.Immutable;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Domain.Tools;

/// <summary>
/// Protocol for structured tool calls.
/// </summary>
public record ToolCallProtocol
{
    /// <summary>Gets the version of this protocol.</summary>
    public string Version { get; }
    /// <summary>Gets the serialization format (e.g., "json").</summary>
    public string Format { get; }
    /// <summary>Gets the list of supported tool names.</summary>
    public ImmutableList<string> SupportedTools { get; }
    /// <summary>Gets the options for tool calls using this protocol.</summary>
    public ToolCallOptions Options { get; }

    /// <summary>Initializes a new instance of <see cref="ToolCallProtocol"/>.</summary>
    /// <param name="version">The protocol version.</param>
    /// <param name="format">The serialization format.</param>
    /// <param name="supportedTools">The supported tool names.</param>
    /// <param name="options">The tool call options.</param>
    public ToolCallProtocol(
        string version = "1.0",
        string format = "json",
        IEnumerable<string>? supportedTools = null,
        ToolCallOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(version);
        Version = version;
        ArgumentNullException.ThrowIfNull(format);
        Format = format;
        SupportedTools = supportedTools?.ToImmutableList() ?? [];
        Options = options ?? ToolCallOptions.Empty;
    }

    /// <inheritdoc />
    public virtual bool Equals(ToolCallProtocol? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        return Version == other.Version &&
               Format == other.Format &&
               SupportedTools.SequenceEqual(other.SupportedTools) &&
               Equals(Options, other.Options);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Version);
        hash.Add(Format);
        foreach (var tool in SupportedTools)
        {
            hash.Add(tool);
        }
        hash.Add(Options);
        return hash.ToHashCode();
    }

    /// <summary>Creates a default <see cref="ToolCallProtocol"/> with version 1.0 and JSON format.</summary>
    /// <returns>A default <see cref="ToolCallProtocol"/>.</returns>
    public static ToolCallProtocol Default()
    {
        return new ToolCallProtocol();
    }

    /// <summary>Creates a protocol supporting the given tools.</summary>
    /// <param name="tools">The tool names to support.</param>
    /// <returns>A new <see cref="ToolCallProtocol"/> with the specified tools.</returns>
    public static ToolCallProtocol WithTools(params string[] tools)
    {
        return new ToolCallProtocol(supportedTools: tools);
    }
}

/// <summary>
/// Request for tool call execution.
/// </summary>
public record ToolCallRequest
{
    /// <summary>Gets the unique identifier of this request.</summary>
    public string Id { get; }
    /// <summary>Gets the name of the tool to call.</summary>
    public string ToolName { get; }
    /// <summary>Gets the arguments to pass to the tool.</summary>
    public ToolArguments Arguments { get; }
    /// <summary>Gets the identifier of the caller, or null if not specified.</summary>
    public string? CallerId { get; }
    /// <summary>Gets the timestamp when this request was created.</summary>
    public DateTime RequestedAt { get; }

    /// <summary>Initializes a new instance of <see cref="ToolCallRequest"/>.</summary>
    /// <param name="toolName">The name of the tool to call.</param>
    /// <param name="arguments">The arguments to pass to the tool.</param>
    /// <param name="callerId">The identifier of the caller.</param>
    public ToolCallRequest(
        string toolName,
        ToolArguments? arguments = null,
        string? callerId = null)
    {
        Id = Guid.NewGuid().ToString();
        ArgumentNullException.ThrowIfNull(toolName);
        ToolName = toolName;
        Arguments = arguments ?? ToolArguments.Empty;
        CallerId = callerId;
        RequestedAt = DateTime.UtcNow;
    }
}
