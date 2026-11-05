using Jint;
using Jint.Native;
using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.Commands.Scripting.Runtime;
using Orkeon.Cli.Commands.Scripting.Tests.Doubles;
using Orkeon.Cli.Commands.Scripting.Tests.Fixtures;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Cli.Commands.Scripting.Tests.Runtime;

/// <summary><see cref="PromptDispatcher"/> on a console with panels, and object choices on one without.</summary>
public sealed class PromptDispatcherPanelTests : IDisposable
{
    private readonly Engine _engine = new(opt =>
    {
        Orkeon.Scripting.JsEngineFactory.ApplyInteropPolicy(opt);
        opt.Constraints.PromiseTimeout = HangGuards.EnginePromise;
    });

    public void Dispose() => _engine.Dispose();

    private JsValue Spec(string literal) => _engine.Evaluate($"({literal})");

    private Task<JsValue> Run(IConsoleAdapter console, string literal)
        => PromptDispatcher.RunAsync(_engine, console, Spec(literal), CancellationToken.None);

    [Fact]
    public async Task A_select_opens_a_panel_with_what_the_spec_describes()
    {
        // Arrange
        var console = new FakeChoiceConsole { Answer = new ConsoleChoiceAnswer("b", null) };

        // Act
        await Run(console, "{type:'select',message:'Which?',title:'T',description:'D',keys:'K',cancellable:false,default:'b',choices:['a','b']}");

        // Assert
        var request = console.Request!;
        Assert.Equal(("T", "D", "K", "Which?", false, "b"), (request.Title, request.Description, request.Keys, request.Question, request.Cancellable, request.DefaultValue));
    }

    [Fact]
    public async Task An_object_choice_keeps_its_fields()
    {
        // Arrange
        var console = new FakeChoiceConsole { Answer = new ConsoleChoiceAnswer("b", null) };

        // Act
        await Run(console, "{type:'select',message:'m',choices:[{value:'b',label:'Bee',description:'d',hint:'h',current:true}]}");

        // Assert
        var expected = new ConsoleChoice("b", "Bee") { Description = "d", Hint = "h", IsCurrent = true };
        Assert.Equal(expected, console.Request!.Choices[0]);
    }

    [Fact]
    public async Task A_string_choice_is_its_own_value_and_label()
    {
        // Arrange
        var console = new FakeChoiceConsole { Answer = new ConsoleChoiceAnswer("a", null) };

        // Act
        await Run(console, "{type:'select',message:'m',choices:['a']}");

        // Assert
        Assert.Equal(new ConsoleChoice("a", "a"), console.Request!.Choices[0]);
    }

    [Fact]
    public async Task A_select_answers_the_value_chosen()
    {
        // Arrange
        var console = new FakeChoiceConsole { Answer = new ConsoleChoiceAnswer("b", null) };

        // Act
        var result = await Run(console, "{type:'select',message:'m',choices:['a','b']}");

        // Assert
        Assert.Equal("b", result.AsString());
    }

    [Fact]
    public async Task A_select_left_with_Esc_answers_null()
    {
        // Arrange
        var console = new FakeChoiceConsole { Answer = null };

        // Act
        var result = await Run(console, "{type:'select',message:'m',choices:['a']}");

        // Assert
        Assert.True(result.IsNull());
    }

    [Fact]
    public async Task A_detailed_select_answers_the_value_and_the_control()
    {
        // Arrange
        var console = new FakeChoiceConsole { Answer = new ConsoleChoiceAnswer("a", "high") };

        // Act
        var result = await Run(console, "{type:'select',message:'m',detailed:true,choices:['a'],control:{label:'effort',values:['low','high']}}");

        // Assert
        Assert.Equal(("a", "high"), (result.AsObject().Get("value").AsString(), result.AsObject().Get("control").AsString()));
    }

    [Fact]
    public async Task A_control_reaches_the_panel_on_its_value()
    {
        // Arrange
        var console = new FakeChoiceConsole { Answer = new ConsoleChoiceAnswer("a", "high") };

        // Act
        await Run(console, "{type:'select',message:'m',choices:['a'],control:{label:'effort',values:['low','high'],value:'high'}}");

        // Assert
        var control = console.Request!.Control!;
        Assert.Equal(("effort", "high", 2), (control.Label, control.Value, control.Values.Count));
    }

    [Fact]
    public async Task A_confirm_answers_true_for_yes()
    {
        // Arrange
        var console = new FakeChoiceConsole { Answer = new ConsoleChoiceAnswer("yes", null) };

        // Act
        var result = await Run(console, "{type:'confirm',message:'Sure?',default:false}");

        // Assert
        Assert.True(result.AsBoolean());
    }

    [Fact]
    public async Task A_confirm_left_with_Esc_answers_its_default()
    {
        // Arrange
        var console = new FakeChoiceConsole { Answer = null };

        // Act
        var result = await Run(console, "{type:'confirm',message:'Sure?',default:true}");

        // Assert
        Assert.True(result.AsBoolean());
    }

    [Fact]
    public async Task A_confirm_opens_on_its_default()
    {
        // Arrange
        var console = new FakeChoiceConsole { Answer = null };

        // Act
        await Run(console, "{type:'confirm',message:'Sure?',default:false}");

        // Assert
        Assert.Equal("no", console.Request!.DefaultValue);
    }

    [Fact]
    public async Task A_text_prompt_uses_the_read_that_cancels_on_a_console_with_panels()
    {
        // Arrange
        var console = new FakeChoiceConsole { Line = "hello" };

        // Act
        await Run(console, "{type:'text',message:'m'}");

        // Assert
        Assert.Equal((1, 0), (console.CancellableReads, console.BlockingReads));
    }

    [Fact]
    public async Task A_cancelled_panel_is_seen_as_a_cancellation()
    {
        // Arrange
        var console = new FakeChoiceConsole { Answer = null };
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var spec = Spec("{type:'select',message:'m',choices:['a']}");

        // Act
        var error = await Record.ExceptionAsync(() => PromptDispatcher.RunAsync(_engine, console, spec, cts.Token));

        // Assert
        Assert.IsType<OperationCanceledException>(error, exactMatch: false);
    }

    [Fact]
    public async Task A_console_that_cannot_ask_a_panel_right_now_gets_the_typed_lines()
    {
        // Arrange
        var console = new FakeChoiceConsole { CanChoose = false, Line = "0" };

        // Act
        await Run(console, "{type:'select',message:'m',choices:['a']}");

        // Assert
        Assert.Equal((null, 1), (console.Request, console.BlockingReads));
    }

    [Fact]
    public async Task A_console_without_panels_shows_labels_and_answers_the_value()
    {
        // Arrange
        var console = new ScriptedTestConsole();
        console.EnqueueLine("1");

        // Act
        var result = await Run(console, "{type:'select',message:'m',choices:[{value:'a',label:'Ay'},{value:'b',label:'Bee',description:'second'}]}");

        // Assert
        Assert.Equal("b", result.AsString());
        Assert.Contains("  [1] Bee  second", console.Output);
    }

    [Fact]
    public async Task A_console_without_panels_takes_a_label_for_an_answer()
    {
        // Arrange
        var console = new ScriptedTestConsole();
        console.EnqueueLine("bee");

        // Act
        var result = await Run(console, "{type:'select',message:'m',choices:[{value:'b',label:'Bee'}]}");

        // Assert
        Assert.Equal("b", result.AsString());
    }

    [Fact]
    public async Task A_detailed_select_on_typed_lines_answers_the_control_it_opened_on()
    {
        // Arrange
        var console = new ScriptedTestConsole();
        console.EnqueueLine("0");

        // Act
        var result = await Run(console, "{type:'select',message:'m',detailed:true,choices:['a'],control:{label:'e',values:['low','high'],value:'high'}}");

        // Assert
        Assert.Equal("high", result.AsObject().Get("control").AsString());
    }

    [Fact]
    public async Task A_choice_object_without_a_value_is_refused()
    {
        // Arrange
        var console = new FakeChoiceConsole();

        // Act
        var error = await Record.ExceptionAsync(() => Run(console, "{type:'select',message:'m',choices:[{label:'x'}]}"));

        // Assert
        Assert.IsType<ArgumentException>(error);
    }
}
