namespace Orkeon.Domain.Constants.HumanInput;

/// <summary>
/// Default values for human input context configuration.
/// Centralises magic strings used across the human input system.
/// </summary>
public static class HumanInputDefaults
{
    /// <summary>Input type identifier for free-text input.</summary>
    public const string TextInputType = "text";

    /// <summary>Input type identifier for multiple-choice input.</summary>
    public const string ChoiceInputType = "choice";

    /// <summary>Input type identifier for yes/no confirmation input.</summary>
    public const string ConfirmationInputType = "confirmation";

    /// <summary>
    /// Metadata key that — when set on <see cref="Orkeon.Domain.HumanInput.HumanInputContext.Metadata"/>
    /// — instructs the human-input provider to expose an inline edit affordance
    /// on the file at the given virtual path. Providers that cannot edit ignore it.
    /// </summary>
    /// <remarks>
    /// Generic across runners: path semantics belong to the calling experiment,
    /// not the provider. The provider only reads the virtual path and routes
    /// I/O through <c>IFileSystemService</c>.
    /// </remarks>
    public const string EditFilePathMetadataKey = "edit_file_path";
}
