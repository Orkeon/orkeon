using System;

namespace Orkeon.Compliance.Vfs;

/// <summary>
/// Suppresses <c>ORKCON001</c> on the annotated element. The <paramref name="reason"/> is
/// mandatory, starts with a ratified category and says why the element may touch
/// <c>System.Console</c>:
/// <list type="bullet">
/// <item><c>CONSOLE-ADAPTER</c>: an implementation of a console abstraction over the real console;</item>
/// <item><c>CONSOLE-SINK</c>: the single point of an executable that hands the real console streams on.</item>
/// </list>
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
public sealed class SuppressConsoleComplianceAttribute : Attribute
{
    public SuppressConsoleComplianceAttribute(string reason)
    {
        Reason = reason ?? throw new ArgumentNullException(nameof(reason));
    }

    public string Reason { get; }
}
