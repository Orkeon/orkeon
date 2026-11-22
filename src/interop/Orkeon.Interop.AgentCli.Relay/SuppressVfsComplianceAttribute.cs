namespace Orkeon.Compliance.Vfs;

/// <summary>
/// The VFS analyzer's opt-out, matched by name (the relay references no Orkeon runtime assembly, so it
/// declares its own, the way any consumer of the analyzer package does).
/// </summary>
[AttributeUsage(AttributeTargets.All, Inherited = false)]
internal sealed class SuppressVfsComplianceAttribute : Attribute
{
    /// <summary>Initializes the opt-out with its reason.</summary>
    /// <param name="reason">Why the member is out of the VFS's scope.</param>
    public SuppressVfsComplianceAttribute(string reason) => Reason = reason;

    /// <summary>Gets why the member is out of the VFS's scope.</summary>
    public string Reason { get; }
}
