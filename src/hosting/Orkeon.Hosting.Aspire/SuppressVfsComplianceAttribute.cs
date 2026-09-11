namespace Orkeon.Compliance.Vfs;

/// <summary>
/// The VFS analyzer's opt-out, matched by name (this project references no Orkeon runtime
/// assembly, so it declares its own, the way any consumer of the analyzer package does).
/// </summary>
[AttributeUsage(AttributeTargets.All, Inherited = false)]
internal sealed class SuppressVfsComplianceAttribute(string reason) : Attribute
{
    public string Reason { get; } = reason;
}
