using Orkeon.Cli.Abstractions.Commands;
using Orkeon.Cli.Abstractions.Registry;
using Orkeon.ConsoleApp.Commands.Qa;

namespace Orkeon.ConsoleApp.Registries;

/// <summary>
/// Registry for the Q&amp;A runner. Has no specific commands of its own — every
/// non-default input is routed to <see cref="AskQuestionCommand"/> via <see cref="Fallback"/>.
/// </summary>
internal sealed class QaCommandRegistry : IInteractiveCommandRegistry
{
    private readonly AskQuestionCommand? _fallback;

    public QaCommandRegistry()
    {
        _fallback = null;
    }

    public QaCommandRegistry(AskQuestionCommand fallback)
    {
        _fallback = fallback;
    }

    public IEnumerable<IInteractiveCommand> Commands => Array.Empty<IInteractiveCommand>();
    public IInteractiveCommand? Fallback => _fallback;
}
