using Orkeon.Hosting;

namespace Orkeon.Examples.Interactive.ClaimVerification;

/// <summary>
/// Concrete <see cref="RunnerOptionsBase"/> used to drive a single
/// <see cref="RunnerExecution.RunOneShotAsync"/> from inside an interactive
/// command. The base class is abstract; this is a minimal subclass with no
/// extra options of its own.
/// </summary>
internal sealed class KickoffOptions : RunnerOptionsBase
{
}
