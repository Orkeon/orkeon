namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// A <see cref="TextReader"/> whose source a test sets after building the object under test:
/// every read goes to <see cref="Inner"/>, an empty reader until set.
/// </summary>
public sealed class FakeTextReader : TextReader
{
    /// <summary>The reader every read is forwarded to.</summary>
    public TextReader Inner { get; set; } = new StringReader(string.Empty);

    /// <inheritdoc />
    public override int Peek() => Inner.Peek();

    /// <inheritdoc />
    public override int Read() => Inner.Read();

    /// <inheritdoc />
    public override string? ReadLine() => Inner.ReadLine();

    /// <inheritdoc />
    public override string ReadToEnd() => Inner.ReadToEnd();
}
