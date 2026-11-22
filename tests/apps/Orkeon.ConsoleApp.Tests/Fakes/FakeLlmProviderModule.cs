using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.ConsoleApp.Tests.Fakes;

/// <summary>An <see cref="ILlmProviderModule"/> answering <see cref="Key"/> with a stub provider; counts its builds.</summary>
internal sealed class FakeLlmProviderModule : ILlmProviderModule
{
    /// <inheritdoc />
    public string Key { get; init; } = "fake-cli";

    /// <summary>How many providers the module built.</summary>
    public int Builds { get; private set; }

    /// <inheritdoc />
    public ILlmProvider Create(LlmConfig config, LlmProviderModuleContext context)
    {
        Builds++;
        return new StubLlmProvider { Name = Key };
    }
}
