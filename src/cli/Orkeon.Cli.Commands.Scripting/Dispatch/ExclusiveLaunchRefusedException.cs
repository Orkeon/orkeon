namespace Orkeon.Cli.Commands.Scripting.Dispatch;

/// <summary>An exclusive launch found another exclusive launch of the same target still running (#239).</summary>
public sealed class ExclusiveLaunchRefusedException : InvalidOperationException
{
    public ExclusiveLaunchRefusedException()
    {
    }

    public ExclusiveLaunchRefusedException(string message) : base(message)
    {
    }

    public ExclusiveLaunchRefusedException(string message, Exception innerException) : base(message, innerException)
    {
    }

    /// <summary>A launch of <paramref name="target"/> refused while the instance <paramref name="ticket"/> runs it.</summary>
    public ExclusiveLaunchRefusedException(string target, string ticket)
        : base($"{target} already running (ticket {ticket})")
    {
        Target = target;
        Ticket = ticket;
    }

    /// <summary>The target of the refused launch (<c>crew:&lt;name&gt;</c>).</summary>
    public string? Target { get; }

    /// <summary>The ticket of the instance that holds the target.</summary>
    public string? Ticket { get; }
}
