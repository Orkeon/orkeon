using System.Runtime.CompilerServices;
using Orkeon.Domain.Common;
using Orkeon.Infrastructure.Serialization;

namespace Orkeon.Scripting.Tests;

/// <summary>
/// The built-in tools a script reaches through <c>tools.*</c> run the typed pipeline, which
/// needs <see cref="ComponentBase.DefaultSerializer"/>. A host sets it through
/// <c>AddOrkeonInfrastructure()</c>; this suite builds none, so the examples that read a file
/// (<c>Examples/</c>) would fail on the first tool call instead of on what they test. Same
/// initializer as the tool test projects; non-destructive when something set it first.
/// </summary>
internal static class TestModuleInitializer
{
    [ModuleInitializer]
    internal static void Initialize() =>
        ComponentBase.TrySetDefaultSerializer(JsonComponentSerializer.Instance);
}
