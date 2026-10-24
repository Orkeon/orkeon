namespace Orkeon.Cli.Commands.Scripting.Dispatch;

/// <summary>A chain of async commands reached the configured maximum depth.</summary>
public sealed class ChainDepthExceededException : InvalidOperationException
{
    public ChainDepthExceededException()
    {
    }

    public ChainDepthExceededException(string message) : base(message)
    {
    }

    public ChainDepthExceededException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public ChainDepthExceededException(int depth, int limit)
        : base($"Async command chain refused at depth {depth}, limit {limit}.")
    {
        Depth = depth;
        Limit = limit;
    }

    public int Depth { get; }

    public int Limit { get; }
}
