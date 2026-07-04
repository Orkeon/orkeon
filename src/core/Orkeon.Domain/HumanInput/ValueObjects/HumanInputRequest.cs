using Orkeon.Domain.Common;

namespace Orkeon.Domain.HumanInput.ValueObjects;

/// <summary>
/// Represents a request for human input.
/// </summary>
public sealed record HumanInputRequest : ValueObjectRecord
{
    /// <summary>Gets the prompt text displayed to the human.</summary>
    public string Prompt { get; init; }
    /// <summary>Gets the type of input expected.</summary>
    public HumanInputType InputType { get; init; }
    /// <summary>Gets the default value if no input is provided.</summary>
    public string? DefaultValue { get; init; }
    /// <summary>Gets the available choices for <see cref="HumanInputType.Choice"/> requests.</summary>
    public IReadOnlyList<string>? Options { get; init; }
    /// <summary>Gets the timeout duration before the request expires.</summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>
    /// Creates a new <see cref="HumanInputRequest"/> instance.
    /// </summary>
    public static HumanInputRequest Create(
        string prompt,
        HumanInputType inputType,
        string? defaultValue = null,
        string[]? options = null,
        TimeSpan? timeout = null)
        => new(prompt, inputType, defaultValue, options, timeout);

    /// <summary>Initializes a new <see cref="HumanInputRequest"/>.</summary>
    /// <param name="prompt">The prompt text to display.</param>
    /// <param name="inputType">The type of input expected.</param>
    /// <param name="defaultValue">The default value if no input is provided.</param>
    /// <param name="options">The available choices for choice-type requests.</param>
    /// <param name="timeout">The timeout duration for the request.</param>
    private HumanInputRequest(
        string prompt,
        HumanInputType inputType,
        string? defaultValue = null,
        string[]? options = null,
        TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        Prompt = prompt;
        InputType = inputType;
        DefaultValue = defaultValue;
        Options = options;
        Timeout = timeout;

        if (inputType == HumanInputType.Choice && (options is null || options.Length == 0))
            throw new ArgumentException("Options must be non-null and non-empty when InputType is Choice.", nameof(options));
    }

    /// <summary>Deconstructs the request into its components.</summary>
    /// <param name="prompt">The prompt text.</param>
    /// <param name="inputType">The type of input.</param>
    /// <param name="defaultValue">The default value.</param>
    /// <param name="options">The available choices.</param>
    /// <param name="timeout">The timeout duration.</param>
    public void Deconstruct(
        out string prompt,
        out HumanInputType inputType,
        out string? defaultValue,
        out IReadOnlyList<string>? options,
        out TimeSpan? timeout)
    {
        prompt = Prompt;
        inputType = InputType;
        defaultValue = DefaultValue;
        options = Options;
        timeout = Timeout;
    }

    /// <inheritdoc />
    public bool Equals(HumanInputRequest? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        return Prompt == other.Prompt &&
               InputType == other.InputType &&
               DefaultValue == other.DefaultValue &&
               Timeout == other.Timeout &&
               OptionsEqual(Options, other.Options);
    }

    private static bool OptionsEqual(IReadOnlyList<string>? a, IReadOnlyList<string>? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return a is null && b is null;
        if (a.Count != b.Count) return false;

        for (int i = 0; i < a.Count; i++)
        {
            if (a[i] != b[i]) return false;
        }

        return true;
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Prompt);
        hash.Add(InputType);
        hash.Add(DefaultValue);
        hash.Add(Timeout);

        if (Options != null)
        {
            foreach (var option in Options)
            {
                hash.Add(option);
            }
        }

        return hash.ToHashCode();
    }
}
