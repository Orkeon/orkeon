namespace Orkeon.Analysis.Core.Structural;

#pragma warning disable CA1064 // internal by design: callers see it through the tool's refusal
internal sealed class UnsupportedRuleKeyException : Exception
#pragma warning restore CA1064
{
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
}
