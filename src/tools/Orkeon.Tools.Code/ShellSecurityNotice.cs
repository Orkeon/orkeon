namespace Orkeon.Tools.Code;

/// <summary>Holds the once-per-host flag of the interpreter opt-in security line.</summary>
internal sealed class ShellSecurityNotice
{
    private int _claimed;

    /// <summary>True for the first caller only.</summary>
    public bool TryClaim() => Interlocked.Exchange(ref _claimed, 1) == 0;
}
