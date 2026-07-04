using Orkeon.Cli.Abstractions.Commands;
using Orkeon.Cli.Abstractions.Registry;
using Orkeon.Examples.Interactive.InterviewSpecForge.Commands;

namespace Orkeon.Examples.Interactive.InterviewSpecForge;

/// <summary>
/// Specific registry for the interview-spec-forge REPL. Pairs with
/// <c>DefaultCommandRegistry</c> (help/exit/clear) inside <c>InterviewSpecForgeRunner</c>.
/// Exposes 10 commands:
///   list, show, forge, status, topics, tasks, glossary, replay, search, test-edit-dialog.
/// </summary>
public sealed class InterviewSpecForgeCommandRegistry : IInteractiveCommandRegistry
{
    private readonly IReadOnlyList<IInteractiveCommand> _commands;

    public InterviewSpecForgeCommandRegistry(
        ListCommand list,
        ShowCommand show,
        ForgeCommand forge,
        StatusCommand status,
        TopicsCommand topics,
        TasksCommand tasks,
        GlossaryCommand glossary,
        ReplayCommand replay,
        SearchCommand search,
        TestEditCommand testEdit)
    {
        _commands = new IInteractiveCommand[]
        {
            list, show, forge, status, topics, tasks, glossary, replay, search, testEdit,
        };
    }

    public IEnumerable<IInteractiveCommand> Commands => _commands;
    public IInteractiveCommand? Fallback => null;
}
