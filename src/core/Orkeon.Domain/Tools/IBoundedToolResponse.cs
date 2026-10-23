namespace Orkeon.Domain.Tools;

/// <summary>
/// Marker implemented by the typed response of a tool that bounds its own output;
/// ToolBase copies it onto ToolCallResponse.AlreadyBounded.
/// </summary>
public interface IBoundedToolResponse
{
    /// <summary>True when the tool already bounded its own output.</summary>
    bool AlreadyBounded { get; }
}
