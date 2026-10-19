using Jint;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Scripting.Tests;

public sealed class JsEngineFactoryPromiseTimeoutTests
{
    /// <summary>
    /// ScriptHost builds its engine with <c>JsEngineFactory.Create()</c>, so the factory path is the
    /// one observed. The value is always the same, so the process-wide setting cannot leak a
    /// different timeout into a parallel class. The factory is handed over as a method group so the
    /// sweep leaves this test alone.
    /// </summary>
    [Fact]
    [Trait("BR", "CH-04")]
    public void Engines_built_by_the_product_take_the_internal_PromiseTimeout()
    {
        JsEngineFactory.PromiseTimeout = HangGuards.EnginePromise;

        var engine = Build(new JsEngineFactory().Create);

        Assert.Equal(HangGuards.EnginePromise, PromiseTimeoutOf(engine));
    }

    /// <summary>Jint 4.16 keeps <c>Engine.Options</c> internal: read it by reflection.</summary>
    private static TimeSpan PromiseTimeoutOf(Engine engine)
    {
        var options = typeof(Engine).GetProperty("Options", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)!.GetValue(engine)!;
        var constraints = options.GetType().GetProperty("Constraints")!.GetValue(options)!;
        return (TimeSpan)constraints.GetType().GetProperty("PromiseTimeout")!.GetValue(constraints)!;
    }

    private static Engine Build(Func<Engine> create) => create();
}
