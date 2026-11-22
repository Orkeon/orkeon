using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Scripting.Cli.Tests.Doubles;

/// <summary>An <see cref="ILlmProviderModule"/> answering <see cref="Key"/> with a stub provider; counts its builds.</summary>
internal sealed class FakeLlmProviderModule : ILlmProviderModule
{
    private int _builds;

    /// <inheritdoc />
    public string Key { get; init; } = "fake-cli";

    /// <summary>How many providers the module built.</summary>
    public int Builds => Volatile.Read(ref _builds);

    /// <inheritdoc />
    public ILlmProvider Create(Orkeon.Domain.SharedKernel.ValueObjects.LlmConfig config, LlmProviderModuleContext context)
    {
        Interlocked.Increment(ref _builds);
        return new StubLlmProvider { Name = Key };
    }
}
