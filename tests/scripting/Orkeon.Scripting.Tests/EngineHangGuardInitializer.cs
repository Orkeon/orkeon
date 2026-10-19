using System.Runtime.CompilerServices;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Scripting.Tests;

/// <summary>
/// CH-04: every engine the product builds for this project's tests (<c>JsEngineFactory.Create()</c>,
/// <c>ScriptHost</c>) takes its promise timeout from the named hang guard.
/// </summary>
internal static class EngineHangGuardInitializer
{
    [ModuleInitializer]
    internal static void Apply()
    {
        JsEngineFactory.PromiseTimeout = HangGuards.EnginePromise;
    }
}
