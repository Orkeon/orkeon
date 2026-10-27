using Microsoft.Extensions.Logging;

namespace Orkeon.Tools.Code.Sessions;

/// <summary>
/// The <c>Orkeon:Tools:Shell:Environment</c> section as a session applies it: the names of
/// <c>Propagate</c> that passed <c>ShellEnvironment.SelectPropagated</c> and the <c>SessionBus</c>
/// switch. Built once per host, so a refused name and the bus are warned about once, not at every
/// tool the registry builds.
/// </summary>
/// <param name="Names">The accepted names, in their configured order.</param>
/// <param name="SessionBus">Whether a confined session receives the operator's session bus.</param>
internal sealed record ShellPropagatedVariables(IReadOnlyList<string> Names, bool SessionBus)
{
    /// <summary>The section filtered, a refused name and an enabled session bus each logged as a warning.</summary>
    /// <param name="options">The section as configured.</param>
    /// <param name="read">Reads one variable of the parent environment; null when it is unset.</param>
    /// <param name="logger">Receives the warnings; null logs nothing.</param>
    public static ShellPropagatedVariables From(ShellEnvironmentOptions options, Func<string, string?> read, ILogger? logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        var names = ShellEnvironment.SelectPropagated(options.Propagate, read, logger);
        if (options.SessionBus && logger is not null)
            ShellEnvironment.WarnSessionBus(logger);
        return new ShellPropagatedVariables(names, options.SessionBus);
    }
}
