using Jint;
using Jint.Native;
using Orkeon.Cli.Commands.Scripting.Runtime;
using Orkeon.Cli.Commands.Scripting.Tests.Fixtures;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Cli.Commands.Scripting.Tests.Runtime;

/// <summary>
/// Unit-level coverage for <see cref="PromptDispatcher"/> using a scripted console and a
/// throwaway Jint engine for <see cref="JsValue"/> construction.
/// </summary>
public sealed class PromptDispatcherTests : IDisposable
{
    private static readonly string[] AbcChoices = ["a", "b", "c"];
    private static readonly string[] AbChoices = ["a", "b"];

    // Built the way the script host builds it: PromptDispatcher reads 'choices' off the spec,
    // and how a CLR array crosses into JS is exactly what Orkeon's interop policy decides.
    private readonly Engine _engine = new(opt => { Orkeon.Scripting.JsEngineFactory.ApplyInteropPolicy(opt); opt.Constraints.PromiseTimeout = HangGuards.EnginePromise; });

    public void Dispose() => _engine.Dispose();

    private JsValue Spec(object literal) => JsValue.FromObject(_engine, literal);

    [Fact]
    public async Task Text_prompt_returns_user_input()
    {
        var console = new ScriptedTestConsole();
        console.EnqueueLine("alice");

        var result = await PromptDispatcher.RunAsync(_engine, console,
            Spec(new { type = "text", message = "Your name?" }),
            CancellationToken.None);

        Assert.True(result.IsString());
        Assert.Equal("alice", result.AsString());
        Assert.Contains("Your name? > ", console.Output);
    }

    [Fact]
    public async Task Text_prompt_shows_default_in_brackets()
    {
        var console = new ScriptedTestConsole();
        console.EnqueueLine(""); // user accepts default

        var result = await PromptDispatcher.RunAsync(_engine, console,
            Spec(new { type = "text", message = "Name?", @default = "alice" }),
            CancellationToken.None);

        Assert.True(result.IsString());
        Assert.Equal("", result.AsString()); // raw user input — defaults are advisory in text
        Assert.Contains("Name? [alice] > ", console.Output);
    }

    [Theory]
    [InlineData("y", true)]
    [InlineData("Y", true)]
    [InlineData("yes", true)]
    [InlineData("n", false)]
    [InlineData("no", false)]
    public async Task Confirm_prompt_parses_yes_no(string input, bool expected)
    {
        var console = new ScriptedTestConsole();
        console.EnqueueLine(input);

        var result = await PromptDispatcher.RunAsync(_engine, console,
            Spec(new { type = "confirm", message = "OK?" }),
            CancellationToken.None);

        Assert.True(result.IsBoolean());
        Assert.Equal(expected, result.AsBoolean());
    }

    [Fact]
    public async Task Confirm_prompt_uses_default_on_empty_input()
    {
        var console = new ScriptedTestConsole();
        console.EnqueueLine("");

        var result = await PromptDispatcher.RunAsync(_engine, console,
            Spec(new { type = "confirm", message = "Proceed?", @default = true }),
            CancellationToken.None);

        Assert.True(result.AsBoolean());
        Assert.Contains("Proceed? (Y/n) > ", console.Output);
    }

    [Fact]
    public async Task Select_prompt_accepts_numeric_index()
    {
        var console = new ScriptedTestConsole();
        console.EnqueueLine("1");

        var result = await PromptDispatcher.RunAsync(_engine, console,
            Spec(new { type = "select", message = "Pick:", choices = AbcChoices }),
            CancellationToken.None);

        Assert.Equal("b", result.AsString());
        Assert.Contains("[0] a", console.Output);
        Assert.Contains("[1] b", console.Output);
    }

    [Fact]
    public async Task Select_prompt_accepts_direct_choice_match()
    {
        var console = new ScriptedTestConsole();
        console.EnqueueLine("c");

        var result = await PromptDispatcher.RunAsync(_engine, console,
            Spec(new { type = "select", message = "Pick:", choices = AbcChoices }),
            CancellationToken.None);

        Assert.Equal("c", result.AsString());
    }

    [Fact]
    public async Task Select_prompt_falls_back_to_default_on_empty()
    {
        var console = new ScriptedTestConsole();
        console.EnqueueLine("");

        var result = await PromptDispatcher.RunAsync(_engine, console,
            Spec(new { type = "select", message = "Pick:", choices = AbcChoices, @default = "b" }),
            CancellationToken.None);

        Assert.Equal("b", result.AsString());
    }

    [Fact]
    public async Task Select_prompt_throws_on_invalid_choice()
    {
        var console = new ScriptedTestConsole();
        console.EnqueueLine("zzz");
        await Assert.ThrowsAsync<InvalidOperationException>(() => PromptDispatcher.RunAsync(_engine, console,
            Spec(new { type = "select", message = "Pick:", choices = AbChoices }),
            CancellationToken.None));
    }

    [Fact]
    public async Task Select_prompt_refuses_a_spec_without_choices()
    {
        // Arrange
        var console = new ScriptedTestConsole();
        var spec = Spec(new { type = "select", message = "Pick:", choices = Array.Empty<string>() });

        // Act
        var error = await Assert.ThrowsAsync<ArgumentException>(() => PromptDispatcher.RunAsync(_engine, console, spec, CancellationToken.None));

        // Assert
        Assert.Contains("non-empty array", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Select_prompt_throws_on_an_empty_line_without_a_default(string line)
    {
        // Arrange
        var console = new ScriptedTestConsole();
        console.EnqueueLine(line);
        var spec = Spec(new { type = "select", message = "Pick:", choices = AbChoices });

        // Act
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => PromptDispatcher.RunAsync(_engine, console, spec, CancellationToken.None));

        // Assert
        Assert.Contains("no selection and no default", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Select_prompt_throws_on_an_empty_line_when_the_default_names_no_choice()
    {
        // Arrange
        var console = new ScriptedTestConsole();
        console.EnqueueLine("");
        var spec = Spec(new { type = "select", message = "Pick:", choices = AbChoices, @default = "zzz" });

        // Act
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => PromptDispatcher.RunAsync(_engine, console, spec, CancellationToken.None));

        // Assert
        Assert.Contains("no selection and no default", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Select_prompt_throws_on_an_index_past_the_last_choice()
    {
        // Arrange
        var console = new ScriptedTestConsole();
        console.EnqueueLine("2");
        var spec = Spec(new { type = "select", message = "Pick:", choices = AbChoices });

        // Act
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => PromptDispatcher.RunAsync(_engine, console, spec, CancellationToken.None));

        // Assert
        Assert.Contains("'2' is not a valid choice", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Password_prompt_returns_user_input()
    {
        var console = new ScriptedTestConsole();
        console.EnqueueLine("secret");

        var result = await PromptDispatcher.RunAsync(_engine, console,
            Spec(new { type = "password", message = "Password:" }),
            CancellationToken.None);

        Assert.Equal("secret", result.AsString());
    }

    [Fact]
    public async Task Cancellation_token_aborts_pending_read()
    {
        var console = new ScriptedTestConsole();
        using var cts = new CancellationTokenSource();
        var task = PromptDispatcher.RunAsync(_engine, console,
            Spec(new { type = "text", message = "Hang?" }), cts.Token);

        // Deterministic wait (R5.6): the read is pending once the prompt has been
        // written to the console — no fixed delay.
        await Polling.WaitUntilAsync(() => console.Output.Contains("Hang?", StringComparison.Ordinal));
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }
}
