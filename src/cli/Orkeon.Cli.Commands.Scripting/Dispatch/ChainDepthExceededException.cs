using System.Runtime.Serialization;

namespace Orkeon.Cli.Commands.Scripting.Dispatch;

/// <summary>A chain of async commands reached the configured maximum depth.</summary>
[Serializable]
public sealed class ChainDepthExceededException : InvalidOperationException
{
    private const string DepthKey = "Depth";
    private const string LimitKey = "Limit";

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

    /// <summary>The formatter-based serialization constructor the <see cref="ISerializable"/> pattern asks for.</summary>
    [Obsolete("Formatter-based serialization is obsolete.", DiagnosticId = "SYSLIB0051")]
    private ChainDepthExceededException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
        Depth = info.GetInt32(DepthKey);
        Limit = info.GetInt32(LimitKey);
    }

    /// <inheritdoc />
    [Obsolete("Formatter-based serialization is obsolete.", DiagnosticId = "SYSLIB0051")]
    public override void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        base.GetObjectData(info, context);
        info.AddValue(DepthKey, Depth);
        info.AddValue(LimitKey, Limit);
    }
}
