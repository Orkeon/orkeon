using Orkeon.Scripting.Cli.Commands.Forge;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Scripting.Cli.Tests.Forge;

/// <summary>
/// The forge's tool policy: what escapes the VFS never enters a forged crew. A forged crew runs
/// on the test bench with real tools, so the mailbox tools (MAIL) are denied with the shell and
/// the interpreter; <c>email_parser</c> only reads an .eml file through the VFS and stays.
/// </summary>
public sealed class ForgeSandboxTests
{
    private static readonly string[] MailboxTools =
    [
        "email_accounts", "email_folders", "email_search", "email_read", "email_save_attachment",
        "email_create_folder", "email_rename_folder", "email_move", "email_mark", "email_delete",
        "email_draft", "email_send",
    ];

    [Fact]
    public void The_catalogue_of_a_forged_crew_leaves_out_every_mailbox_tool()
    {
        var registered = MailboxTools
            .Concat(["email_parser", "file_read", "shell_command", "code_interpreter"])
            .Select(name => new StubBaseTool(name))
            .ToList();

        var catalogue = ForgeSandbox.SelectCrewTools(registered).Select(tool => tool.Name).ToList();

        Assert.Equal(["email_parser", "file_read"], catalogue);
    }

    [Fact]
    public void Every_mailbox_tool_is_denied()
    {
        foreach (var name in MailboxTools)
            Assert.Contains(name, ForgeSandbox.DeniedTools);
        Assert.DoesNotContain("email_parser", ForgeSandbox.DeniedTools);
    }
}
