using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Crew.Routing;
using Orkeon.Application.DependencyInjection;
using Orkeon.Cli.Commands.Scripting.DependencyInjection;
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
    /// <summary>The picker as a host offers it: beside the run scope AddScriptCommands registers, which the service reads.</summary>
    private static ServiceCollection WithRunScope(ServiceCollection services)
    {
        services.AddSingleton(sp => new Orkeon.Cli.Commands.Scripting.DependencyInjection.ScriptEngineRunScope(sp));
        return services;
    }

    private static Task<string> RunAsync(ISystemOnePicker picker, string body, CancellationToken? ct = null) =>
        RunAsync(WithRunScope(new ServiceCollection()).AddSingleton(picker).BuildServiceProvider(), body, ct ?? Ct);

    /// <summary>Writes the command whose handler body is <paramref name="body"/> under <c>cmd/</c> of a new directory.</summary>
    private static async Task<string> WriteCommandAsync(string body)
    {
        var directory = Path.Combine(Path.GetTempPath(), "orkeon-pick-cmd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, "cmd"));
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
        return directory;
    }

    /// <summary>Runs the command over <paramref name="services"/> (disposed after the run) and returns what it wrote.</summary>
    private static async Task<string> RunAsync(ServiceProvider services, string body, CancellationToken ct)
    {
        using var owned = services;
        var directory = await WriteCommandAsync(body);
        try
        {
            var fs = new DiskBackedFileSystemService(directory, virtualRoot: "/workspace");
            var loader = new ScriptCommandLoader(
                fs,
                PassThroughTranspiler.Instance,
                new JsEngineFactory(),
                Microsoft.Extensions.Options.Options.Create(new ScriptCommandLoaderOptions { Directories = ["/workspace/cmd"], EsbuildTranspile = false }),
                new ScriptCommandLoaderDependencies { Services = Locator(services) });
            var (registry, _) = await loader.LoadAndRegisterAsync(Ct);
            var output = new ScriptedTestConsole();

            try
            {
                await registry.ScriptCommands.Single(c => c.Name == "choose").ExecuteAsync(
                    new CommandContext { RawInput = "/choose", Args = [], Console = output, Scope = services }, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested && !Ct.IsCancellationRequested)
            {
                // The run a test cancelled on purpose: what it wrote until then is the answer.
            }

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
        using var bare = WithRunScope(new ServiceCollection()).BuildServiceProvider();
        using var wired = WithRunScope(new ServiceCollection()).AddSingleton<ISystemOnePicker>(new StubSystemOnePicker()).BuildServiceProvider();

        Assert.False(Locator(bare).has(ScriptServiceKeys.Pick));
        Assert.True(Locator(wired).has(ScriptServiceKeys.Pick));
        using var engine = new Jint.Engine(options => options.Constraints.PromiseTimeout = Orkeon.Tests.Shared.Timing.HangGuards.EnginePromise);
        Assert.IsType<PickService>(Locator(wired).BoundTo(engine, Ct).get(ScriptServiceKeys.Pick));
    }

    /// <summary>
    /// A container with a picker but no run scope (a host that did not call AddScriptCommands): the scoped picker is
    /// never read from the root, so the service is absent and refused as any service that is not registered.
    /// </summary>
    [Fact]
    [Trait("BR", "CH-463")]
    public void ShouldNotOfferThePick_WhenNoRunScopeIsRegistered()
    {
        using var services = new ServiceCollection()
            .AddScoped<ISystemOnePicker>(_ => new StubSystemOnePicker())
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        Assert.False(Locator(services).has(ScriptServiceKeys.Pick));
        using var engine = new Jint.Engine(options => options.Constraints.PromiseTimeout = Orkeon.Tests.Shared.Timing.HangGuards.EnginePromise);
        var refusal = Assert.ThrowsAny<Exception>(() => Locator(services).BoundTo(engine, Ct).get(ScriptServiceKeys.Pick));
        Assert.Contains("not registered in this host", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The wiring a host ships (<c>orkeon-repl</c>'s Program): AddOrkeonApplication registers the scoped picker,
    /// AddScriptCommands the run scope and the loader, under a container that validates scopes.
    /// </summary>
    [Fact]
    [Trait("BR", "CH-463")]
    public async Task ShouldOfferAndAnswerThePick_ThroughTheHostWiring_WhenTheContainerValidatesScopes()
    {
        var directory = await WriteCommandAsync("""
            ctx.writeLine("has:" + ctx.services.has("pick"));
            const answer = await pick.pick("r", { effort: { fallback: "medium" } });
            ctx.writeLine("answer:" + answer.effort.value);
            """);
        try
        {
            var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
            var collection = new ServiceCollection();
            collection.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(configuration);
            collection.AddLogging();
            collection.AddSingleton<Orkeon.Domain.FileSystem.IFileSystemService>(new DiskBackedFileSystemService(directory, virtualRoot: "/workspace"));
            collection.AddOrkeonApplication();
            collection.AddScriptCommands(configuration, cfg =>
            {
                cfg.Directories = ["/workspace/cmd"];
                cfg.EsbuildTranspile = false;
            });
            await using var services = collection.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

            var (registry, _) = await services.GetRequiredService<ScriptCommandLoader>().LoadAndRegisterAsync(Ct);
            var output = new ScriptedTestConsole();
            await registry.ScriptCommands.Single(c => c.Name == "choose").ExecuteAsync(
                new CommandContext { RawInput = "/choose", Args = [], Console = output, Scope = services }, Ct);

            Assert.Contains("has:true", output.Output, StringComparison.Ordinal);
            Assert.Contains("answer:medium", output.Output, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { /* best-effort */ }
        }
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
    }

    [Fact]
    [Trait("BR", "CH-463")]
    public async Task ShouldFollowTheCommandsToken_WhenTheRunIsCancelledDuringThePick()
    {
        using var run = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var before = true;
        var during = false;
        var picker = new StubSystemOnePicker
        {
            OnPick = token =>
            {
                before = token.IsCancellationRequested;
                run.Cancel();
                during = token.IsCancellationRequested;
            },
        };

        await RunAsync(picker, """await pick.pick("r", { effort: {} });""", run.Token);

        Assert.Single(picker.Requests);
        Assert.False(before);
        Assert.True(during);
    }

    [Fact]
    [Trait("BR", "CH-463")]
    public async Task ShouldResolveTheScopedPicker_FromTheRunScope_WhenTheContainerValidatesScopes()
    {
        var picker = new StubSystemOnePicker();
        var services = new ServiceCollection()
            .AddScoped<ISystemOnePicker>(_ => picker)
            .AddSingleton(sp => new Orkeon.Cli.Commands.Scripting.DependencyInjection.ScriptEngineRunScope(sp))
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        Assert.True(Locator(services).has(ScriptServiceKeys.Pick));
        var output = await RunAsync(services, """
            ctx.writeLine("has:" + ctx.services.has("pick"));
            const answer = await pick.pick("r", { effort: {} });
            ctx.writeLine("answer:" + answer.effort.value);
            """, Ct);

        Assert.Contains("has:true", output, StringComparison.Ordinal);
        Assert.Contains("answer:low", output, StringComparison.Ordinal);
        Assert.Single(picker.Requests);
    }

    [Fact]
    [Trait("BR", "CH-463")]
    public async Task ShouldAnswerAProfilePick()
    {
        var picker = new StubSystemOnePicker
        {
            Answer = new SystemOnePickResponse
            {
                Profile = new SystemOnePickAnswer { Value = "smart", Reason = SystemOnePickReasons.Model, Picked = "smart", Probability = 0.9 },
            },
        };

        var output = await RunAsync(picker, """
            const answer = await pick.pick("Plan the release.", { profile: { ladder: ["fast", "smart"], fallback: "fast" } });
            ctx.writeLine("answer:" + JSON.stringify(answer));
            """);

        Assert.Contains("""answer:{"profile":{"value":"smart","reason":"model","picked":"smart","probability":0.9}}""", output, StringComparison.Ordinal);
        var asked = Assert.Single(picker.Requests);
        Assert.Equal<string>(["fast", "smart"], asked.Profile!.Ladder!);
        Assert.Equal("fast", asked.Profile?.Fallback);
        Assert.Null(asked.Effort);
    }

    [Fact]
    [Trait("BR", "CH-463")]
    public async Task ShouldAskBothParts_InOneRequestToThePicker()
    {
        var picker = new StubSystemOnePicker
        {
            Answer = new SystemOnePickResponse
            {
                Profile = new SystemOnePickAnswer { Value = "fast", Reason = SystemOnePickReasons.Model, Picked = "fast", Probability = 0.8 },
                Effort = new SystemOnePickAnswer { Value = "high", Reason = SystemOnePickReasons.Model, Picked = "high", Probability = 0.7 },
            },
        };

        var output = await RunAsync(picker, """
            const answer = await pick.pick("r", { profile: { ladder: ["fast", "smart"] }, effort: { max: "high" } });
            ctx.writeLine("answer:" + answer.profile.value + "/" + answer.effort.value);
            """);

        Assert.Contains("answer:fast/high", output, StringComparison.Ordinal);
        var asked = Assert.Single(picker.Requests);
        Assert.NotNull(asked.Profile);
        Assert.Equal("high", asked.Effort?.Max);
    }

    [Fact]
    [Trait("BR", "CH-463")]
    public async Task ShouldRejectThePickersRefusal_AsAnInvalidScriptErrorNamingTheOption()
    {
        var picker = new StubSystemOnePicker
        {
            OnPick = _ => throw new ArgumentException("names a profile the host does not declare.", "profile.fallback"),
        };

        var output = await RunAsync(picker, """
            try { await pick.pick("r", { profile: { fallback: "nope" } }); ctx.writeLine("no refusal"); }
            catch (e) { ctx.writeLine("refused:" + e.name + ":" + e.message); }
            """);

        Assert.Contains("""refused:InvalidScriptError:services.get("pick").pick { profile.fallback } names a profile the host does not declare.""", output, StringComparison.Ordinal);
        Assert.Single(picker.Requests);
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
