using System.Text.Json;
using Orkeon.Cli.Commands.Scripting.Progress;

namespace Orkeon.Cli.Commands.Scripting.Tests.Progress;

public sealed class ToolCallSubjectTests
{
    [Fact]
    public void A_shell_call_shows_its_command()
    {
        // Arrange
        var arguments = new Dictionary<string, object?> { ["command"] = "ls -la" };

        // Act
        var subject = ToolCallSubject.Of("shell_command", arguments);

        // Assert
        Assert.Equal("ls -la", subject);
    }

    [Fact]
    public void A_value_the_model_sent_as_json_is_read()
    {
        // Arrange
        var arguments = new Dictionary<string, object?> { ["path"] = JsonElement.Parse("\"a.cs\"") };

        // Act
        var subject = ToolCallSubject.Of("file_read", arguments);

        // Assert
        Assert.Equal("a.cs", subject);
    }

    [Fact]
    public void An_edit_never_shows_what_it_writes()
    {
        // Arrange
        var arguments = new Dictionary<string, object?> { ["path"] = "a.cs", ["new_string"] = "secret" };

        // Act
        var subject = ToolCallSubject.Of("edit_file", arguments);

        // Assert
        Assert.Equal("a.cs", subject);
    }

    [Fact]
    public void A_tool_not_listed_shows_none_of_its_arguments()
    {
        // Arrange
        var arguments = new Dictionary<string, object?> { ["path"] = "a.cs", ["token"] = "secret" };

        // Act
        var subject = ToolCallSubject.Of("some_tool", arguments);

        // Assert
        Assert.Empty(subject);
    }

    [Fact]
    public void A_command_on_several_lines_is_shown_on_one()
    {
        // Arrange
        var arguments = new Dictionary<string, object?> { ["command"] = "cd x\n  && ls" };

        // Act
        var subject = ToolCallSubject.Of("shell_command", arguments);

        // Assert
        Assert.Equal("cd x && ls", subject);
    }

    [Fact]
    public void A_very_long_command_is_bounded()
    {
        // Arrange
        var arguments = new Dictionary<string, object?> { ["command"] = new string('x', 5000) };

        // Act
        var subject = ToolCallSubject.Of("shell_command", arguments);

        // Assert
        Assert.Equal(400, subject.Length);
    }
}
