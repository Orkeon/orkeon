namespace Orkeon.Domain.Tools;

/// <summary>
/// Interface for typed tool parameters.
/// </summary>
public interface ITypedToolParameters
{
    /// <summary>
    /// Validates the parameters.
    /// </summary>
    bool IsValid();

    /// <summary>
    /// Gets validation errors if any.
    /// </summary>
    IReadOnlyList<string> GetValidationErrors();
}
