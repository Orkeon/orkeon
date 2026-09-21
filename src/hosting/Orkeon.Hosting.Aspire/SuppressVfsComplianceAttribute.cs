namespace Orkeon.Compliance.Vfs;

/// <summary>
/// The VFS analyzer's opt-out, matched by name (this project references no Orkeon runtime
/// assembly, so it declares its own, the way any consumer of the analyzer package does).
/// </summary>
[AttributeUsage(AttributeTargets.All, Inherited = false)]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S3604:Member initializer values should not be redundant",
    Justification = "False positive on a primary constructor: each initializer IS the capture of the parameter into the member, no constructor assigns it, and removing it would leave the member unset.")]
internal sealed class SuppressVfsComplianceAttribute(string reason) : Attribute
{
    public string Reason { get; } = reason;
}
