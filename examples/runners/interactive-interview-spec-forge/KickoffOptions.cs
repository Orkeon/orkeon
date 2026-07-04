using Orkeon.Hosting;

namespace Orkeon.Examples.Interactive.InterviewSpecForge;

/// <summary>
/// Concrete <see cref="RunnerOptionsBase"/> used to drive a single
/// <c>RunnerExecution.RunOneShotAsync</c> from inside an interactive
/// command (ForgeCommand, ReplayCommand). The base class is abstract;
/// this is a minimal subclass with no extra options of its own.
/// </summary>
internal sealed class KickoffOptions : RunnerOptionsBase
{
}
