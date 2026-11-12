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

    /// <summary>A launch of <paramref name="command"/> refused at <paramref name="depth"/>, <paramref name="limit"/> being <c>MaxChainDepth</c>.</summary>
    public ChainDepthExceededException(int depth, int limit, string command)
        : base($"chain depth {depth} reached (MaxChainDepth {limit}, /{command})")
    {
        Depth = depth;
        Limit = limit;
        Command = command;
    }

    /// <summary>The depth of the chain the refused launch would have extended.</summary>
    public int Depth { get; }

    /// <summary>The limit that applied (<c>Orkeon:Cli:ScriptCommands:MaxChainDepth</c>).</summary>
    public int Limit { get; }

    /// <summary>The command whose launch was refused.</summary>
    public string? Command { get; }
}
