namespace Orkeon.Compliance.Vfs;

/// <summary>
/// Opts out of <c>ORKCON001</c> (direct <c>System.Console</c> usage) on the annotated element.
/// The <c>reason</c> argument is mandatory and starts with a ratified category:
/// <c>CONSOLE-ADAPTER</c> (an implementation of a console abstraction over the process console)
/// or <c>CONSOLE-SINK</c> (the single point of an executable that hands the real console
/// streams on to the code it runs).
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
public sealed class SuppressConsoleComplianceAttribute : System.Attribute
{
    /// <summary>Initializes the attribute with the mandatory justification text.</summary>
    public SuppressConsoleComplianceAttribute(string reason)
    {
        Reason = reason ?? throw new System.ArgumentNullException(nameof(reason));
    }

    /// <summary>Human-readable justification explaining why the console exception is legitimate.</summary>
    public string Reason { get; }
}
