namespace Orkeon.Analysis.Core.Structural;

public sealed class UnsupportedRuleKeyException : Exception
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
