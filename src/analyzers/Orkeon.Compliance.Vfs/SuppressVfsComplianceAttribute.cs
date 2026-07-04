using System;

namespace Orkeon.Compliance.Vfs;

/// <summary>
/// Suppresses <c>ORK-VFS-001..005</c> diagnostics on the annotated element.
/// Used by the Orkeon.Compliance.Vfs analyzer to mark documented exceptions:
/// VFS implementation internals, sandbox bootstrap code, and system binary probing.
/// The <paramref name="reason"/> is mandatory and must describe why the exception is legitimate.
/// </summary>
[AttributeUsage(
    AttributeTargets.Assembly
    | AttributeTargets.Class
    | AttributeTargets.Struct
    | AttributeTargets.Method
    | AttributeTargets.Constructor
    | AttributeTargets.Property
    | AttributeTargets.Field,
    AllowMultiple = false,
    Inherited = false)]
public sealed class SuppressVfsComplianceAttribute : Attribute
{
    public SuppressVfsComplianceAttribute(string reason)
    {
        Reason = reason ?? throw new ArgumentNullException(nameof(reason));
    }

    public string Reason { get; }
}
