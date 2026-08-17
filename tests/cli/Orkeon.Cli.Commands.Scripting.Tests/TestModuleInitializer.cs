using System.Runtime.CompilerServices;
using Orkeon.Domain.Common;
using Orkeon.Infrastructure.Serialization;

namespace Orkeon.Cli.Commands.Scripting.Tests;

/// <summary>
/// Configures the <see cref="ComponentBase.DefaultSerializer"/> before any test runs —
/// required by the typed-pipeline tools this project now exercises (ProgressReportTool).
/// Production hosts get this from <c>AddOrkeonInfrastructure()</c>.
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
