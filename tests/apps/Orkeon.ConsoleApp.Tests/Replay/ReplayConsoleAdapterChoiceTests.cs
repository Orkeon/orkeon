using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.Abstractions.Runners;
using Orkeon.Cli.Commands.Scripting.Dispatch;
using Orkeon.ConsoleApp.Replay;
using Orkeon.Cli.Abstractions.Tests.Doubles;
using Orkeon.ConsoleApp.Tests.Fakes;

namespace Orkeon.ConsoleApp.Tests.Replay;

public sealed class ReplayConsoleAdapterChoiceTests : IDisposable
{
    private readonly CommandInstanceRegistry _registry = new();
    private readonly FakeToolApprovalWatch _approvals = new();
    private readonly ReplayIdleGate _gate;

    public ReplayConsoleAdapterChoiceTests() => _gate = new ReplayIdleGate(() => _registry, () => _approvals);

    public void Dispose() => _gate.Dispose();

    private ReplayConsoleAdapter Over(IConsoleAdapter inner) => new(inner, _gate);

    [Fact]
    public void A_console_with_panels_can_still_choose_behind_the_decorator()
    {
        // Arrange
        var replay = Over(new FakeChoiceConsole());

        // Act
        var canChoose = replay.CanChoose;

        // Assert
        Assert.True(canChoose);
    }

    [Fact]
    public void A_console_without_panels_cannot_choose_behind_the_decorator()
    {
        // Arrange
        var replay = Over(new FakeConsoleAdapter());

        // Act
        var canChoose = replay.CanChoose;

        // Assert
        Assert.False(canChoose);
    }

    [Fact]
    public void A_running_replay_cannot_choose_since_its_answers_are_its_lines()
    {
        // Arrange
        var replay = Over(new FakeChoiceConsole());
        replay.TryStart([new ReplayLine(1, "/help")], closeInputAtEnd: true);

        // Act
        var canChoose = replay.CanChoose;

        // Assert
        Assert.False(canChoose);
    }

    [Fact]
    public async Task A_panel_goes_to_the_console_wrapped()
    {
        // Arrange
        var inner = new FakeChoiceConsole { Answer = new ConsoleChoiceAnswer("a", null) };
        var replay = Over(inner);
        var request = new ConsoleChoiceRequest { Choices = [new("a", "A")] };

        // Act
        var answer = await replay.ChooseAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(("a", request), (answer!.Value, inner.Request));
    }

    [Fact]
    public void A_console_without_palettes_has_none_behind_the_decorator()
    {
        // Arrange
        var replay = Over(new FakeConsoleAdapter());

        // Act
        var hasPalette = replay.HasPalette;

        // Assert
        Assert.False(hasPalette);
    }

    [Fact]
    public void A_block_ending_its_line_adds_no_blank_line_on_a_console_without_blocks()
    {
        // Arrange
        var inner = new FakeConsoleAdapter();
        var replay = Over(inner);

        // Act
        replay.WriteBlock(ConsoleBlockKind.Markdown, "text\n");

        // Assert
        Assert.Equal(["text"], inner.Lines);
    }

    [Fact]
    public async Task A_read_for_a_script_reaches_the_panels_behind_the_decorator()
    {
        // Arrange
        var inner = new FakeChoiceConsole();
        var replay = Over(inner);

        // Act
        await replay.ReadAnswerAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, inner.AnswerReads);
    }
}
