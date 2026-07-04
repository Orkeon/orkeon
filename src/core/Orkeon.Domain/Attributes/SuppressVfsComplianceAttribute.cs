namespace Orkeon.Compliance.Vfs;

/// <summary>
/// Opts out of <c>ORKVFS001..005</c> diagnostics on the annotated element.
/// Used to mark documented exceptions to the VFS-only principle:
/// VFS implementation internals, sandbox bootstrap code, system binary probing,
/// and transitional <c>[Obsolete]</c> members pending removal.
/// The <c>reason</c> argument is mandatory and must describe why the exception is legitimate.
/// </summary>
[System.AttributeUsage(
    System.AttributeTargets.Assembly
    | System.AttributeTargets.Class
    | System.AttributeTargets.Struct
    | System.AttributeTargets.Method
    | System.AttributeTargets.Constructor
    | System.AttributeTargets.Property
    | System.AttributeTargets.Field,
    AllowMultiple = false,
    Inherited = false)]
public sealed class SuppressVfsComplianceAttribute : System.Attribute
{
    /// <summary>Initializes the attribute with the mandatory justification text.</summary>
    public SuppressVfsComplianceAttribute(string reason)
    {
        Reason = reason ?? throw new System.ArgumentNullException(nameof(reason));
    }

    /// <summary>Human-readable justification explaining why the VFS compliance exception is legitimate.</summary>
    public string Reason { get; }
}
