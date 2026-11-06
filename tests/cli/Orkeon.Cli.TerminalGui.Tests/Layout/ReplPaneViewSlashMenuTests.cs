using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.Abstractions.Runners;
using Orkeon.Cli.TerminalGui.Hosting;
using Orkeon.Cli.TerminalGui.Layout;
using Orkeon.Cli.TerminalGui.Tests.Doubles;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Xunit;

namespace Orkeon.Cli.TerminalGui.Tests.Layout;

public sealed class ReplPaneViewSlashMenuTests
{
    private static readonly ReplCommandEntry[] Commands =
    [
        new("help", "Shows the commands"),
        new("model", "Picks a model"),
        new("mode", "Picks a mode"),
    ];

    private static (ReplPaneView Pane, FakeCommandCatalog Catalog) CreatePane()
    {
        var pane = new ReplPaneView(new TerminalGuiOptions(), InlineDispatcher.Instance);
        var catalog = new FakeCommandCatalog { Entries = Commands };
        pane.ConfigureCompletion(catalog);
        return (pane, catalog);
    }

    [Fact]
    public void A_slash_opens_the_menu()
    {
        // Arrange
        var (pane, _) = CreatePane();
        using var owned = pane;

        // Act
        pane.CurrentInput = "/";

        // Assert
        Assert.Equal(3, pane.SlashMenuRows);
    }

    [Fact]
    public void Typing_narrows_the_menu()
    {
        // Arrange
        var (pane, _) = CreatePane();
        using var owned = pane;
        pane.CurrentInput = "/";

        // Act
        pane.CurrentInput = "/mod";

        // Assert
        Assert.Equal(2, pane.SlashMenuRows);
    }

    [Fact]
    public void The_commands_are_listed_once_for_a_menu_however_long_the_typing()
    {
        // Arrange
        var (pane, catalog) = CreatePane();
        using var owned = pane;
        pane.CurrentInput = "/";

        // Act
        pane.CurrentInput = "/m";

        // Assert
        Assert.Equal(1, catalog.ListCalls);
    }

    public static TheoryData<string> TextsWithoutAMenu => new() { "hello", "/model gpt", "/zzz", "/a\nb" };

    [Theory]
    [MemberData(nameof(TextsWithoutAMenu))]
    public void Other_text_leaves_the_menu_closed(string text)
    {
        // Arrange
        var (pane, _) = CreatePane();
        using var owned = pane;

        // Act
        pane.CurrentInput = text;

        // Assert
        Assert.Null(pane.OpenSlashMenu);
    }

    [Fact]
    public void A_source_without_a_catalog_opens_no_menu()
    {
        // Arrange
        using var pane = new ReplPaneView(new TerminalGuiOptions(), InlineDispatcher.Instance);

        // Act
        pane.CurrentInput = "/";

        // Assert
        Assert.Null(pane.OpenSlashMenu);
    }

    [Fact]
    public void Down_moves_the_selection_and_leaves_the_text()
    {
        // Arrange
        var (pane, _) = CreatePane();
        using var owned = pane;
        pane.CurrentInput = "/";

        // Act
        pane.RaiseKeyDown(Key.CursorDown);

        // Assert
        Assert.Equal(("mode", "/"), (pane.OpenSlashMenu!.SelectedName, pane.CurrentInput));
    }

    [Fact]
    public void Tab_fills_the_command_in_and_closes_the_menu()
    {
        // Arrange
        var (pane, _) = CreatePane();
        using var owned = pane;
        pane.CurrentInput = "/mod";

        // Act
        pane.RaiseKeyDown(Key.Tab);

        // Assert
        Assert.Equal(("/mode ", null), (pane.CurrentInput, pane.OpenSlashMenu));
    }

    [Fact]
    public async Task Enter_runs_the_command_selected()
    {
        // Arrange
        var (pane, _) = CreatePane();
        using var owned = pane;
        var read = pane.ReadLineAsync(CancellationToken.None);
        pane.CurrentInput = "/mod";
        pane.RaiseKeyDown(Key.CursorDown);

        // Act
        pane.RaiseKeyDown(Key.Enter);

        // Assert
        Assert.Equal("/model", await read);
    }

    [Fact]
    public async Task Enter_on_text_the_menu_does_not_know_submits_it_as_typed()
    {
        // Arrange
        var (pane, _) = CreatePane();
        using var owned = pane;
        var read = pane.ReadLineAsync(CancellationToken.None);
        pane.CurrentInput = "/zzz";

        // Act
        pane.RaiseKeyDown(Key.Enter);

        // Assert
        Assert.Equal("/zzz", await read);
    }

    [Fact]
    public void Enter_empties_the_field_and_closes_the_menu()
    {
        // Arrange
        var (pane, _) = CreatePane();
        using var owned = pane;
        _ = pane.ReadLineAsync(CancellationToken.None);
        pane.CurrentInput = "/";

        // Act
        pane.RaiseKeyDown(Key.Enter);

        // Assert
        Assert.Equal((string.Empty, null), (pane.CurrentInput, pane.OpenSlashMenu));
    }

    [Fact]
    public void Esc_closes_the_menu_and_keeps_the_text()
    {
        // Arrange
        var (pane, _) = CreatePane();
        using var owned = pane;
        pane.CurrentInput = "/mod";

        // Act
        pane.RaiseKeyDown(Key.Esc);

        // Assert
        Assert.Equal((null, "/mod"), (pane.OpenSlashMenu, pane.CurrentInput));
    }

    [Fact]
    public void Esc_on_the_menu_does_not_arm_the_interrupt()
    {
        // Arrange
        var (pane, _) = CreatePane();
        using var owned = pane;
        var interrupts = 0;
        pane.InterruptRequested += (_, _) => interrupts++;
        pane.CurrentInput = "/";
        pane.RaiseKeyDown(Key.Esc);

        // Act
        pane.RaiseKeyDown(Key.Esc);

        // Assert
        Assert.Equal(0, interrupts);
    }

    [Fact]
    public void A_menu_left_with_Esc_stays_closed_while_the_command_is_typed()
    {
        // Arrange
        var (pane, _) = CreatePane();
        using var owned = pane;
        pane.CurrentInput = "/";
        pane.RaiseKeyDown(Key.Esc);

        // Act
        pane.CurrentInput = "/m";

        // Assert
        Assert.Null(pane.OpenSlashMenu);
    }

    [Fact]
    public void A_new_slash_after_a_cleared_field_opens_the_menu_again()
    {
        // Arrange
        var (pane, _) = CreatePane();
        using var owned = pane;
        pane.CurrentInput = "/";
        pane.RaiseKeyDown(Key.Esc);
        pane.CurrentInput = string.Empty;

        // Act
        pane.CurrentInput = "/";

        // Assert
        Assert.NotNull(pane.OpenSlashMenu);
    }

    [Fact]
    public async Task A_line_recalled_from_the_history_opens_no_menu()
    {
        // Arrange
        var (pane, _) = CreatePane();
        using var owned = pane;
        var read = pane.ReadLineAsync(CancellationToken.None);
        pane.CurrentInput = "/help ";
        pane.RaiseKeyDown(Key.Enter);
        await read;

        // Act
        pane.RaiseKeyDown(Key.CursorUp);

        // Assert
        Assert.Equal(("/help ", null), (pane.CurrentInput, pane.OpenSlashMenu));
    }

    [Fact]
    public void A_panel_opening_closes_the_menu()
    {
        // Arrange
        var (pane, _) = CreatePane();
        using var owned = pane;
        pane.CurrentInput = "/";

        // Act
        _ = pane.ChooseAsync(new ConsoleChoiceRequest { Choices = [new("a", "A")] }, CancellationToken.None);

        // Assert
        Assert.Null(pane.OpenSlashMenu);
    }

    [Fact]
    public void A_question_handed_to_the_prompt_opens_the_menu_on_its_slash()
    {
        // Arrange
        var (pane, _) = CreatePane();
        using var owned = pane;
        var pending = new List<PendingToolApproval> { new("run_command", "{}", null, null) { Id = 1 } };
        pane.BindIntegration(new TuiIntegration { PendingApprovals = () => pending });
        pane.RaiseApprovalsChanged();

        // Act
        pane.RaiseKeyDown(new Key('/'));

        // Assert
        Assert.NotNull(pane.OpenSlashMenu);
    }

    [Fact]
    public void The_menu_opens_again_after_a_dismissed_command_was_submitted()
    {
        // Arrange
        var (pane, _) = CreatePane();
        using var owned = pane;
        pane.CurrentInput = "/";
        pane.RaiseKeyDown(Key.Esc);
        pane.RaiseKeyDown(Key.Enter);

        // Act
        pane.CurrentInput = "/";

        // Assert
        Assert.Equal(3, pane.SlashMenuRows);
    }

    [Fact]
    public void The_menu_opens_again_after_a_dismissed_command_was_cleared()
    {
        // Arrange
        var (pane, _) = CreatePane();
        using var owned = pane;
        pane.CurrentInput = "/";
        pane.RaiseKeyDown(Key.Esc);
        pane.RaiseKeyDown(Key.Esc);

        // Act
        pane.CurrentInput = "/";

        // Assert
        Assert.Equal(3, pane.SlashMenuRows);
    }
}
