using Orkeon.Scripting;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Cli.Commands.Scripting.Tests;

internal static class EngineHangGuardInitializer
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Apply()
    {
        JsEngineFactory.PromiseTimeout = HangGuards.EnginePromise;
    }
}
