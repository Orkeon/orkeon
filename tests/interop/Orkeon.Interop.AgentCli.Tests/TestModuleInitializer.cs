using System.Runtime.CompilerServices;
using Orkeon.Domain.Common;
using Orkeon.Infrastructure.Serialization;

namespace Orkeon.Interop.AgentCli.Tests;

/// <summary>
/// Configures the <see cref="ComponentBase.DefaultSerializer"/> before any test runs --
/// the loop tests ride the typed tool pipeline. Production hosts get this from
/// <c>AddOrkeonInfrastructure()</c>.
/// </summary>
internal static class TestModuleInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        if (!ComponentBase.IsDefaultSerializerConfigured)
        {
            ComponentBase.DefaultSerializer = JsonComponentSerializer.Instance;
        }
    }
}
