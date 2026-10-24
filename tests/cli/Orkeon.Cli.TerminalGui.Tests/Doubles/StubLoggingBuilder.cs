using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Orkeon.Cli.TerminalGui.Tests.Doubles;

/// <summary>Logging builder over a plain service collection the test inspects.</summary>
internal sealed class StubLoggingBuilder : ILoggingBuilder
{
    public IServiceCollection Services { get; } = new ServiceCollection();
}
