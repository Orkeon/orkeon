namespace Orkeon.Domain.SharedKernel.ValueObjects;

/// <summary>How a call reaches the model: <c>Auto</c>, <c>Buffer</c> or <c>Stream</c>.</summary>
public sealed record LlmCallMode
{
    private LlmCallMode(string name) => Name = name;

    /// <summary>The provider's own choice.</summary>
    public static LlmCallMode Auto { get; } = new("Auto");

    /// <summary>One buffered answer.</summary>
    public static LlmCallMode Buffer { get; } = new("Buffer");

    /// <summary>A streamed answer.</summary>
    public static LlmCallMode Stream { get; } = new("Stream");

    /// <summary>The spelling written in settings.</summary>
    public string Name { get; }

    /// <summary>Reads a mode, case ignored; Auto when absent or empty.</summary>
    /// <param name="value">The written value.</param>
    /// <returns>The mode.</returns>
    public static LlmCallMode Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Auto;

        var name = value.Trim();
        return Array.Find(new[] { Auto, Buffer, Stream }, mode => string.Equals(mode.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"'{value}' is not a call mode: use Auto, Buffer or Stream.", nameof(value));
    }

    /// <summary>Whether the call streams on the wire.</summary>
    public bool StreamsOnTheWire => this != Buffer;

    /// <inheritdoc />
    public override string ToString() => Name;
}
