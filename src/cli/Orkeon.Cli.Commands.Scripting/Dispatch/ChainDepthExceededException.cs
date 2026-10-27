using System.Runtime.Serialization;

namespace Orkeon.Cli.Commands.Scripting.Dispatch;

/// <summary>A chain of async commands reached the configured maximum depth.</summary>
[Serializable]
public sealed class ChainDepthExceededException : InvalidOperationException
{
    private const string DepthKey = "Depth";
    private const string LimitKey = "Limit";
    private const string CommandKey = "Command";

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

    /// <summary>The formatter-based serialization constructor the <see cref="ISerializable"/> pattern asks for.</summary>
    [Obsolete("Formatter-based serialization is obsolete.", DiagnosticId = "SYSLIB0051")]
    private ChainDepthExceededException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
        Depth = info.GetInt32(DepthKey);
        Limit = info.GetInt32(LimitKey);
        Command = info.GetString(CommandKey);
    }

    /// <inheritdoc />
    [Obsolete("Formatter-based serialization is obsolete.", DiagnosticId = "SYSLIB0051")]
    public override void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        base.GetObjectData(info, context);
        info.AddValue(DepthKey, Depth);
        info.AddValue(LimitKey, Limit);
        info.AddValue(CommandKey, Command);
    }
}
