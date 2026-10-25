using System.Runtime.Serialization;

namespace Orkeon.Analysis.Core.Structural;

[Serializable]
public sealed class UnsupportedRuleKeyException : Exception
{
    private const string KeyKey = "Key";

    public UnsupportedRuleKeyException()
    {
    }

    public UnsupportedRuleKeyException(string key)
        : base($"Unsupported rule key '{key}'. Supported keys: {RuleParser.SupportedKeys}.")
    {
        Key = key;
    }

    public UnsupportedRuleKeyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public string Key { get; } = string.Empty;

    [Obsolete("Formatter-based serialization is obsolete.", DiagnosticId = "SYSLIB0051")]
    private UnsupportedRuleKeyException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
        Key = info.GetString(KeyKey) ?? string.Empty;
    }

    /// <inheritdoc />
    [Obsolete("Formatter-based serialization is obsolete.", DiagnosticId = "SYSLIB0051")]
    public override void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        base.GetObjectData(info, context);
        info.AddValue(KeyKey, Key);
    }
}
