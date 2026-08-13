namespace Orkeon.Studio.Core.Launch;

/// <summary>
/// Stable codes carried by the launch validation messages, alongside the appsettings
/// codes of <c>Orkeon.Studio.Core.Validation.ValidationCodes</c>.
/// </summary>
public static class LaunchCodes
{
    /// <summary>An option was set that the detected target's dialect does not accept.</summary>
    public const string OptionNotApplicable = "STUDIO-LAUNCH-OPTION";

    /// <summary><c>--verbose</c> is outside 0-2.</summary>
    public const string InvalidVerbosity = "STUDIO-LAUNCH-VERBOSE";

    /// <summary>A <c>-V</c> variable has no usable key.</summary>
    public const string InvalidVariable = "STUDIO-LAUNCH-VAR";

    /// <summary>A <c>--mount</c> entry is empty.</summary>
    public const string EmptyMount = "STUDIO-LAUNCH-MOUNT";

    /// <summary>Both <c>--inputs</c> and <c>--inputs-file</c> are set.</summary>
    public const string ConflictingInputs = "STUDIO-LAUNCH-INPUTS";

    /// <summary>
    /// The target is a directory, so the launch relies on directory dispatch — released, and
    /// stated as advice (see <c>RunTargetRequirements.DirectoryRunNotice</c>), never a warning.
    /// </summary>
    public const string DirectoryRunNotice = "STUDIO-LAUNCH-DIRECTORY";
}
