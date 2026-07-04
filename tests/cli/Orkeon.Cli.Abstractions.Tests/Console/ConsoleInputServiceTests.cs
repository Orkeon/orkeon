using Orkeon.Cli.Abstractions.Console;

namespace Orkeon.Cli.Abstractions.Tests.Console;

public sealed class ConsoleInputServiceTests
{
    private static readonly string[] ChoicesAC = ["a", "c"];
    private static readonly string[] ChoicesBC = ["b", "c"];

    private static ConsoleInputService Service(ScriptedConsoleAdapter adapter) => new(adapter);

    // ─── GetMenuChoice ───

    [Fact]
    public void GetMenuChoice_valid_first_attempt_returns_choice()
    {
        var console = new ScriptedConsoleAdapter().QueueLine("2");

        var result = Service(console).GetMenuChoice(1, 3);

        Assert.Equal(2, result);
    }

    [Fact]
    public void GetMenuChoice_accepts_min_and_max_bounds()
    {
        var consoleMin = new ScriptedConsoleAdapter().QueueLine("1");
        var consoleMax = new ScriptedConsoleAdapter().QueueLine("5");

        Assert.Equal(1, Service(consoleMin).GetMenuChoice(1, 5));
        Assert.Equal(5, Service(consoleMax).GetMenuChoice(1, 5));
    }

    [Fact]
    public void GetMenuChoice_reprompts_on_non_numeric_then_returns_valid()
    {
        var console = new ScriptedConsoleAdapter().QueueLines("abc", "3");

        var result = Service(console).GetMenuChoice(1, 5);

        Assert.Equal(3, result);
        Assert.Contains("Invalid choice", console.Output, StringComparison.Ordinal);
        Assert.Contains("between 1 and 5", console.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void GetMenuChoice_reprompts_on_out_of_range_below_and_above()
    {
        var console = new ScriptedConsoleAdapter().QueueLines("0", "9", "4");

        var result = Service(console).GetMenuChoice(1, 5);

        Assert.Equal(4, result);
    }

    [Fact]
    public void GetMenuChoice_reprompts_on_null_line()
    {
        var console = new ScriptedConsoleAdapter().QueueLines(null, "2");

        var result = Service(console).GetMenuChoice(1, 5);

        Assert.Equal(2, result);
    }

    // ─── GetRequiredString ───

    [Fact]
    public void GetRequiredString_returns_trimmed_value()
    {
        var console = new ScriptedConsoleAdapter().QueueLine("  hello  ");

        var result = Service(console).GetRequiredString("Name: ");

        Assert.Equal("hello", result);
        Assert.Contains("Name: ", console.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void GetRequiredString_reprompts_on_empty_and_whitespace()
    {
        var console = new ScriptedConsoleAdapter().QueueLines("", "   ", "value");

        var result = Service(console).GetRequiredString("Field: ");

        Assert.Equal("value", result);
        Assert.Contains("This field is required", console.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void GetRequiredString_reprompts_on_null()
    {
        var console = new ScriptedConsoleAdapter().QueueLines(null, "ok");

        var result = Service(console).GetRequiredString("Field: ");

        Assert.Equal("ok", result);
    }

    // ─── GetOptionalString ───

    [Fact]
    public void GetOptionalString_returns_trimmed_value()
    {
        var console = new ScriptedConsoleAdapter().QueueLine("  data ");

        var result = Service(console).GetOptionalString("Opt: ");

        Assert.Equal("data", result);
    }

    [Fact]
    public void GetOptionalString_returns_null_on_empty()
    {
        var console = new ScriptedConsoleAdapter().QueueLine("   ");

        var result = Service(console).GetOptionalString("Opt: ");

        Assert.Null(result);
    }

    [Fact]
    public void GetOptionalString_returns_null_on_null_line()
    {
        var console = new ScriptedConsoleAdapter().QueueLine(null);

        var result = Service(console).GetOptionalString("Opt: ");

        Assert.Null(result);
    }

    // ─── GetYesNo ───

    [Fact]
    public void GetYesNo_returns_true_on_Y()
    {
        var console = new ScriptedConsoleAdapter().QueueKey(ConsoleKey.Y);

        var result = Service(console).GetYesNo("Continue?");

        Assert.True(result);
        Assert.Contains("Continue? (y/n): ", console.Output, StringComparison.Ordinal);
        Assert.Contains("Yes", console.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void GetYesNo_returns_false_on_N()
    {
        var console = new ScriptedConsoleAdapter().QueueKey(ConsoleKey.N);

        var result = Service(console).GetYesNo("Continue?");

        Assert.False(result);
        Assert.Contains("No", console.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void GetYesNo_ignores_unrelated_keys_until_valid()
    {
        var console = new ScriptedConsoleAdapter()
            .QueueKey(ConsoleKey.A)
            .QueueKey(ConsoleKey.Spacebar)
            .QueueKey(ConsoleKey.Y);

        var result = Service(console).GetYesNo("OK?");

        Assert.True(result);
        Assert.Equal(3, console.ReadKeyCount);
        Assert.True(console.LastReadKeyIntercept);
    }

    // ─── GetMultipleChoices ───

    [Fact]
    public void GetMultipleChoices_empty_input_returns_empty_list()
    {
        var console = new ScriptedConsoleAdapter().QueueLine("");
        var options = new List<string> { "a", "b", "c" };

        var result = Service(console).GetMultipleChoices("Pick:", options);

        Assert.Empty(result);
        Assert.Contains("1. a", console.Output, StringComparison.Ordinal);
        Assert.Contains("Available options:", console.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void GetMultipleChoices_null_input_returns_empty_list()
    {
        var console = new ScriptedConsoleAdapter().QueueLine(null);
        var options = new List<string> { "a", "b" };

        var result = Service(console).GetMultipleChoices("Pick:", options);

        Assert.Empty(result);
    }

    [Fact]
    public void GetMultipleChoices_parses_comma_separated_indices()
    {
        var console = new ScriptedConsoleAdapter().QueueLine("1,3");
        var options = new List<string> { "a", "b", "c" };

        var result = Service(console).GetMultipleChoices("Pick:", options);

        Assert.Equal(ChoicesAC, result);
    }

    [Fact]
    public void GetMultipleChoices_ignores_out_of_range_and_non_numeric()
    {
        var console = new ScriptedConsoleAdapter().QueueLine("0, 2, 99, foo, 3");
        var options = new List<string> { "a", "b", "c" };

        var result = Service(console).GetMultipleChoices("Pick:", options);

        Assert.Equal(ChoicesBC, result);
    }

    // ─── WaitForKey ───

    [Fact]
    public void WaitForKey_default_message_reads_key_with_intercept()
    {
        var console = new ScriptedConsoleAdapter().QueueKey(ConsoleKey.Enter);

        Service(console).WaitForKey();

        Assert.Contains("Press any key to continue...", console.Output, StringComparison.Ordinal);
        Assert.Equal(1, console.ReadKeyCount);
        Assert.True(console.LastReadKeyIntercept);
    }

    [Fact]
    public void WaitForKey_custom_message_is_written()
    {
        var console = new ScriptedConsoleAdapter().QueueKey(ConsoleKey.Spacebar);

        Service(console).WaitForKey("Done, hit a key");

        Assert.Contains("Done, hit a key", console.Output, StringComparison.Ordinal);
    }
}
