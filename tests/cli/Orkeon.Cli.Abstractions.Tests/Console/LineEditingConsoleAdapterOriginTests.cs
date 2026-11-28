using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.Abstractions.Console.LineEditing;
using Orkeon.Cli.Abstractions.Runners;
using Orkeon.Cli.Abstractions.Tests.Doubles;

namespace Orkeon.Cli.Abstractions.Tests.Console;

/// <summary>
/// The line-editing console states a line was typed only when it read it from the keyboard of a real terminal, through
/// its editor; a line read from a redirected input is not (#491).
/// </summary>
public sealed class LineEditingConsoleAdapterOriginTests
{
    [Fact]
    public void ALineReadThroughTheEditor_IsTyped()
    {
        var adapter = new LineEditingConsoleAdapter(
            new LineEditor(new FakeLineEditorConsole(FakeLineEditorConsole.Typing("! ls"))),
            isInputRedirected: () => false,
            readRedirected: () => throw new InvalidOperationException("The input is not redirected."));

        Assert.Equal("! ls", adapter.ReadLine());
        Assert.True(((IReplLineOrigin)adapter).LastLineTyped);
    }

    [Fact]
    public void ALineReadFromARedirectedInput_IsNotTyped()
    {
        var adapter = new LineEditingConsoleAdapter(
            new LineEditor(new FakeLineEditorConsole([])),
            isInputRedirected: () => true,
            readRedirected: () => "! rm -rf x");

        Assert.Equal("! rm -rf x", adapter.ReadLine());
        Assert.False(((IReplLineOrigin)adapter).LastLineTyped);
    }

    [Fact]
    public void BeforeAnyRead_NoLineIsTyped()
    {
        var adapter = new LineEditingConsoleAdapter(
            new LineEditor(new FakeLineEditorConsole([])), isInputRedirected: () => false, readRedirected: () => null);

        Assert.False(((IReplLineOrigin)adapter).LastLineTyped);
    }
}
