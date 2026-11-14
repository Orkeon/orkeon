using Orkeon.Scripting.Runtime;
using Orkeon.Scripting.Tests.Internal;

namespace Orkeon.Scripting.Tests.Runtime;

public sealed class JsEventBrokerTests
{
    [Theory]
    [Trait("BR", "CH-364")]
    [InlineData("broker.queue(undefined)")]
    [InlineData("broker.topic(undefined)")]
    [InlineData("broker.topic(undefined, { mode: 'parallel' })")]
    public void ShouldReachTheScriptsCatch_WhenAQueueOrTopicHasNoName(string call)
    {
        var engine = new JsEngineFactory().Create();
        engine.SetValue("broker", new JsEventBroker(engine));

        var caught = ScriptRefusal.Caught(engine, call);

        Assert.StartsWith("true|ArgumentNullException|", caught, StringComparison.Ordinal);
    }
}
