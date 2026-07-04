namespace Orkeon.Domain.HumanInput;

/// <summary>
/// Types of human input that can be requested.
/// </summary>
public sealed record HumanInputType
{
    /// <summary>Gets the string value of this input type.</summary>
    public string Value { get; }
    private HumanInputType(string value) => Value = value;

    /// <summary>Free-text input from the user.</summary>
    public static readonly HumanInputType Text = new("Text");
    /// <summary>A yes/no confirmation from the user.</summary>
    public static readonly HumanInputType Confirmation = new("Confirmation");
    /// <summary>Selection from a list of predefined choices.</summary>
    public static readonly HumanInputType Choice = new("Choice");
    /// <summary>A file path provided by the user.</summary>
    public static readonly HumanInputType File = new("File");
    /// <summary>A numeric value provided by the user.</summary>
    public static readonly HumanInputType Number = new("Number");
    /// <summary>A date/time value provided by the user.</summary>
    public static readonly HumanInputType DateTime = new("DateTime");
    /// <summary>A custom input type defined by the caller.</summary>
    public static readonly HumanInputType Custom = new("Custom");
    /// <summary>An explicit approval decision from the user.</summary>
    public static readonly HumanInputType Approval = new("Approval");
    /// <summary>A file upload provided by the user.</summary>
    public static readonly HumanInputType FileUpload = new("FileUpload");

    private static readonly Dictionary<string, HumanInputType> s_all = new(StringComparer.OrdinalIgnoreCase)
    {
        [nameof(Text)] = Text,
        [nameof(Confirmation)] = Confirmation,
        [nameof(Choice)] = Choice,
        [nameof(File)] = File,
        [nameof(Number)] = Number,
        [nameof(DateTime)] = DateTime,
        [nameof(Custom)] = Custom,
        [nameof(Approval)] = Approval,
        [nameof(FileUpload)] = FileUpload,
    };

    /// <summary>Gets all known human input types.</summary>
    public static IReadOnlyCollection<HumanInputType> All => s_all.Values;

    /// <summary>Returns the <see cref="HumanInputType"/> matching <paramref name="value"/>, or throws if unknown.</summary>
    public static HumanInputType From(string value) =>
        s_all.TryGetValue(value, out var s)
            ? s
            : throw new ArgumentException($"Unknown HumanInputType: '{value}'", nameof(value));

    /// <summary>Tries to parse <paramref name="value"/> into a known <see cref="HumanInputType"/>.</summary>
    public static bool TryFrom(string? value, out HumanInputType? result)
    {
        if (value is not null && s_all.TryGetValue(value, out var f)) { result = f; return true; }
        result = null; return false;
    }

    /// <inheritdoc />
    public override string ToString() => Value;
    /// <summary>Implicitly converts a <see cref="HumanInputType"/> to its string value.</summary>
    public static implicit operator string(HumanInputType s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return s.Value;
    }
}
