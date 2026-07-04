using System.Runtime.CompilerServices;
using Orkeon.Domain.Common;
using Orkeon.Infrastructure.Serialization;

namespace Orkeon.Application.Tests.EventHub;

internal static class TestModuleInitializerEventHub
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
