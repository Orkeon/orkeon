using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Crew.Routing;
using Orkeon.Cli.Abstractions.Commands;
using Orkeon.Cli.Commands.Scripting.Loading;
using Orkeon.Cli.Commands.Scripting.Runtime;
using Orkeon.Cli.Commands.Scripting.Tests.Doubles;
using Orkeon.Cli.Commands.Scripting.Tests.Fixtures;
using Orkeon.Scripting;
using Orkeon.Scripting.Toolchain;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Cli.Commands.Scripting.Tests.Runtime;

/// <summary>
/// #463: <c>ctx.services.get("pick")</c>, the contract of <c>ctx.llm.pick</c> for a command: the same request and
/// options, the same refusals, the same answer, in one decision call that follows the command's signal.
/// </summary>
public sealed class PickServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ScriptServiceLocator Locator(IServiceProvider services) =>
        new(DefaultScriptServiceWhitelist.Build().Build(), services);

    /// <summary>Runs a command whose handler body is <paramref name="body"/> and returns what it wrote.</summary>
    private static async Task<string> RunAsync(ISystemOnePicker picker, string body)
    {
        var directory = Path.Combine(Path.GetTempPath(), "orkeon-pick-cmd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, "cmd"));
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "cmd", "choose.cmd.ts"), $$"""
                defineCommand({
                  name: "choose",
                  description: "Asks System One.",
                  async handler(args, ctx) {
                    const pick = ctx.services.get("pick");
                    {{body}}
                    return ctx.continue();
                  },
                });
                """, Ct);
            var fs = new DiskBackedFileSystemService(directory, virtualRoot: "/workspace");
            using var services = new ServiceCollection().AddSingleton(picker).BuildServiceProvider();
            var loader = new ScriptCommandLoader(
                fs,
                PassThroughTranspiler.Instance,
                new JsEngineFactory(),
                Microsoft.Extensions.Options.Options.Create(new ScriptCommandLoaderOptions { Directories = ["/workspace/cmd"], EsbuildTranspile = false }),
                new ScriptCommandLoaderDependencies { Services = Locator(services) });
            var (registry, _) = await loader.LoadAndRegisterAsync(Ct);
            var output = new ScriptedTestConsole();

            await registry.ScriptCommands.Single(c => c.Name == "choose").ExecuteAsync(
                new CommandContext { RawInput = "/choose", Args = [], Console = output, Scope = services }, Ct);

            return output.Output;
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { /* best-effort */ }
        }
    }

    [Fact]
    [Trait("BR", "CH-463")]
    public void ShouldOfferThePick_OnlyWhenTheHostWiresOne()
    {
        using var bare = new ServiceCollection().BuildServiceProvider();
        using var wired = new ServiceCollection().AddSingleton<ISystemOnePicker>(new StubSystemOnePicker()).BuildServiceProvider();

        Assert.False(Locator(bare).has(ScriptServiceKeys.Pick));
        Assert.True(Locator(wired).has(ScriptServiceKeys.Pick));
        using var engine = new Jint.Engine(options => options.Constraints.PromiseTimeout = Orkeon.Tests.Shared.Timing.HangGuards.EnginePromise);
        Assert.IsType<PickService>(Locator(wired).BoundTo(engine, Ct).get(ScriptServiceKeys.Pick));
    }

    [Fact]
    [Trait("BR", "CH-463")]
    public async Task ShouldAnswerAsCtxLlmPick_InOneCallThatFollowsTheCommandsSignal()
    {
        var picker = new StubSystemOnePicker();

        var output = await RunAsync(picker, """
            const answer = await pick.pick("Refactor the parser.", { effort: { min: "low", max: "high" } });
            ctx.writeLine("answer:" + JSON.stringify(answer));
            """);

        Assert.Contains("""answer:{"effort":{"value":"low","reason":"model","picked":"low","probability":0.75}}""", output, StringComparison.Ordinal);
        var asked = Assert.Single(picker.Requests);
        Assert.Equal("Refactor the parser.", asked.Request);
        Assert.Equal("low", asked.Effort?.Min);
        Assert.Equal("high", asked.Effort?.Max);
        Assert.Null(asked.Profile);
        Assert.True(Assert.Single(picker.Tokens).CanBeCanceled);
    }

    [Theory]
    [Trait("BR", "CH-463")]
    [InlineData("""pick.pick("r", { effort: {}, speed: 1 })""", """services.get("pick").pick { options } has no option 'speed': it takes profile, effort.""")]
    [InlineData("""pick.pick(42, { effort: {} })""", """services.get("pick").pick { request } takes the text the decision model reads, as a string.""")]
    [InlineData("""pick.pick("r", {})""", """services.get("pick").pick { options } asks neither a profile nor an effort""")]
    [InlineData("""pick.pick("r", { profile: { ladder: ["a", 2] } })""", """services.get("pick").pick { profile.ladder } takes profile names: rung 1 is not a string.""")]
    public async Task ShouldRefuseABadOption_BeforeAnyDecisionCall(string call, string refusal)
    {
        var picker = new StubSystemOnePicker();

        var output = await RunAsync(picker, $$"""
            try { await {{call}}; ctx.writeLine("no refusal"); }
            catch (e) { ctx.writeLine("refused:" + e.name + ":" + e.message); }
            """);

        Assert.Contains("refused:InvalidScriptError:" + refusal, output, StringComparison.Ordinal);
        Assert.Empty(picker.Requests);
    }
}
