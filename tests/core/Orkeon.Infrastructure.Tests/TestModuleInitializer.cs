using System.Runtime.CompilerServices;
using Orkeon.Domain.Common;
using Orkeon.Infrastructure.Serialization;

namespace Orkeon.Infrastructure.Tests;

/// <summary>
/// Configures the <see cref="ComponentBase.DefaultSerializer"/> before any test runs.
/// Required because ComponentBase delegates serialization to IComponentSerializer,
/// which in production is wired up via DI / AddOrkeonInfrastructure().
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
