namespace Orkeon.Infrastructure.HumanInput;

/// <summary>Headless answer policy.</summary>
public enum HumanInputHeadlessPolicy
{
    /// <summary>Answer with the default.</summary>
    Default,
    /// <summary>Refuse.</summary>
    Refuse,
}

/// <summary>Options of the headless human input provider.</summary>
public sealed class HumanInputOptions
{
    /// <summary>Gets or sets the headless policy.</summary>
    public HumanInputHeadlessPolicy HeadlessPolicy { get; set; } = HumanInputHeadlessPolicy.Default;
}

/// <summary>Raised when the headless policy refuses a request.</summary>
public sealed class HumanInputRefusedException : InvalidOperationException
{
    /// <summary>Initializes a new instance.</summary>
    public HumanInputRefusedException(string message) : base(message) { }

    /// <summary>Initializes a new instance.</summary>
    public HumanInputRefusedException() { }

    /// <summary>Initializes a new instance.</summary>
    public HumanInputRefusedException(string message, Exception innerException) : base(message, innerException) { }
}
