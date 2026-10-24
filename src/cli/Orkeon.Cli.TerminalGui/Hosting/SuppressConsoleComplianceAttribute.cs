namespace Orkeon.Compliance.Vfs;

/// <summary>
/// The console analyzer's opt-out, matched by name (this project references no Orkeon runtime
/// assembly carrying it, so it declares its own, as Orkeon.Cli.Abstractions does).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Property, Inherited = false)]
internal sealed class SuppressConsoleComplianceAttribute : Attribute
{
    public SuppressConsoleComplianceAttribute(string reason) => Reason = reason;

    public string Reason { get; }
}
