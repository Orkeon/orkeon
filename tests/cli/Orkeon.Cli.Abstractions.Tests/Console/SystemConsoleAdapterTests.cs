using Orkeon.Cli.Abstractions.Console;

namespace Orkeon.Cli.Abstractions.Tests.Console;

/// <summary>
/// Verifies <see cref="SystemConsoleAdapter"/> delegates to <see cref="System.Console"/>
/// by redirecting the standard streams. ReadKey and Clear are exercised in the
/// indirect-error paths only because they require an interactive terminal.
/// </summary>
[Collection("SystemConsole")]
public sealed class SystemConsoleAdapterTests
{
    [Fact]
    public void Write_delegates_to_console_out()
    {
        var adapter = new SystemConsoleAdapter();
        var original = System.Console.Out;
        using var buffer = new StringWriter();
        System.Console.SetOut(buffer);
        try
        {
            adapter.Write("hello");
        }
        finally
        {
            System.Console.SetOut(original);
        }

        Assert.Equal("hello", buffer.ToString());
    }

    [Fact]
    public void WriteLine_delegates_to_console_out_with_newline()
    {
        var adapter = new SystemConsoleAdapter();
        var original = System.Console.Out;
        using var buffer = new StringWriter();
        System.Console.SetOut(buffer);
        try
        {
            adapter.WriteLine("line");
        }
        finally
        {
            System.Console.SetOut(original);
        }

        Assert.Equal("line" + Environment.NewLine, buffer.ToString());
    }

    [Fact]
    public void ReadLine_delegates_to_console_in()
    {
        var adapter = new SystemConsoleAdapter();
        var original = System.Console.In;
        System.Console.SetIn(new StringReader("typed input\n"));
        try
        {
            var result = adapter.ReadLine();
            Assert.Equal("typed input", result);
        }
        finally
        {
            System.Console.SetIn(original);
        }
    }

    [Fact]
    public void ReadLine_returns_null_at_end_of_stream()
    {
        var adapter = new SystemConsoleAdapter();
        var original = System.Console.In;
        System.Console.SetIn(new StringReader(""));
        try
        {
            var result = adapter.ReadLine();
            Assert.Null(result);
        }
        finally
        {
            System.Console.SetIn(original);
        }
    }

    [Fact]
    public void ReadKey_propagates_when_input_is_redirected()
    {
        // Console.ReadKey only THROWS when input is redirected (CI / non-interactive
        // host) — there it fails fast with InvalidOperationException and we assert the
        // adapter propagates it. With a real console attached (e.g. an interactive
        // Windows test host) ReadKey would instead BLOCK forever waiting for a real
        // keypress, so we must not invoke it there. Guarding on IsInputRedirected keeps
        // this deterministic on every platform (same spirit as the Clear test below).
        if (!System.Console.IsInputRedirected)
            return;

        var adapter = new SystemConsoleAdapter();

        Assert.ThrowsAny<Exception>(() => adapter.ReadKey(true));
    }

    [Fact]
    public void Clear_either_succeeds_or_throws_io_when_no_terminal()
    {
        var adapter = new SystemConsoleAdapter();

        // Clear() targets the terminal escape sequence. On a CI host without a
        // TTY it throws IOException; with redirection it may succeed. Either way
        // the delegating call is exercised.
        var exception = Record.Exception(adapter.Clear);

        Assert.True(exception is null or IOException);
    }
}
