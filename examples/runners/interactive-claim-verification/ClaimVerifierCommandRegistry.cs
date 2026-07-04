using Orkeon.Cli.Abstractions.Commands;
using Orkeon.Cli.Abstractions.Registry;
using Orkeon.Examples.Interactive.ClaimVerification.Commands;

namespace Orkeon.Examples.Interactive.ClaimVerification;

/// <summary>
/// Specific registry for the claim-verification REPL. Pairs with
/// <c>DefaultCommandRegistry</c> (help/exit/clear) inside <c>ClaimVerifierRunner</c>.
/// </summary>
public sealed class ClaimVerifierCommandRegistry : IInteractiveCommandRegistry
{
    private readonly IReadOnlyList<IInteractiveCommand> _commands;

    public ClaimVerifierCommandRegistry(
        ListCommand list,
        ShowCommand show,
        VerifyCommand verify,
        AddClaimCommand addClaim)
    {
        _commands = new IInteractiveCommand[] { list, show, verify, addClaim };
    }

    public IEnumerable<IInteractiveCommand> Commands => _commands;
    public IInteractiveCommand? Fallback => null;
}
