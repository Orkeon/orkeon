using System.Collections.Immutable;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Cli.Abstractions.Commands;
using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.Commands.Scripting.Dispatch;
using Orkeon.Cli.Commands.Scripting.Loading;
using Orkeon.Cli.Commands.Scripting.Registry;
using Orkeon.Cli.Commands.Scripting.Runtime;
using Orkeon.Cli.Commands.Scripting.Tests.Fixtures;
using Orkeon.Infrastructure.Communication;
using Orkeon.Scripting;
using Orkeon.Scripting.Builders;
using Orkeon.Scripting.Toolchain;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Cli.Commands.Scripting.Tests.Dispatch;

/// <summary>Shared loader and helpers of the RT-20 chain tests.</summary>
internal static class ChainHarness
{
    internal static IInteractiveCommand Load(CommandDispatchService service, string tempDir, string crewsDir)
        => Load(service, tempDir, crewsDir, "chain-async.cmd.ts", "chain-async");

    /// <summary>Loads <paramref name="command"/> from <paramref name="fixture"/>, <paramref name="extra"/> adding services to the whitelist.</summary>
    internal static IInteractiveCommand Load(
        CommandDispatchService service, string tempDir, string crewsDir, string fixture, string command,
        Func<ScriptServiceWhitelist, ScriptServiceWhitelist>? extra = null)
    {
        foreach (var crew in new[] { "one", "two" })
        {
            Directory.CreateDirectory(Path.Combine(crewsDir, crew));
            File.WriteAllText(Path.Combine(crewsDir, crew, "crew.ork.ts"), $"\"{crew}-out\";");
        }
        var crewFs = new DiskBackedFileSystemService(crewsDir, virtualRoot: "/crews");
        var facade = new ScriptHostFacade(
            new ScriptHost(crewFs, new JsEngineFactory(), NullLogger<ScriptHost>.Instance),
            crewFs,
            Microsoft.Extensions.Options.Options.Create(new ScriptHostFacadeOptions { CrewDirectories = ImmutableArray.Create("/crews") }),
            service,
            NullLogger<ScriptHostFacade>.Instance);

        var builder = new ScriptServiceWhitelist().AddOptional("script-host", _ => facade);
        return LoadAll(service, tempDir, fixture, extra is null ? builder : extra(builder)).Single(c => c.Name == command);
    }

    /// <summary>Loads every command of <paramref name="fixture"/>, the scripts seeing the services of <paramref name="whitelist"/>.</summary>
    internal static IReadOnlyList<IInteractiveCommand> LoadAll(
        CommandDispatchService service, string tempDir, string fixture, ScriptServiceWhitelist whitelist)
    {
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture), Path.Combine(tempDir, fixture));
        var loader = new ScriptCommandLoader(
            new DiskBackedFileSystemService(tempDir, virtualRoot: "/cmd"),
            PassThroughTranspiler.Instance,
            new JsEngineFactory(),
            Microsoft.Extensions.Options.Options.Create(new ScriptCommandLoaderOptions
            {
                Directories = ImmutableArray.Create("/cmd"),
                EsbuildTranspile = false,
            }),
            new ScriptCommandLoaderDependencies
            {
                LoggerFactory = null,
                Services = new ScriptServiceLocator(whitelist.Build(), EmptyProvider.Instance),
                Dispatch = service,
            });
        var (registry, _) = loader.LoadAndRegisterAsync(CancellationToken.None).GetAwaiter().GetResult();
        return registry.ScriptCommands.ToList();
    }

    internal static CommandContext Ctx(string raw, string arg, IConsoleAdapter console) => new()
    {
        RawInput = raw,
        Args = new[] { arg },
        Console = console,
        Scope = EmptyProvider.Instance,
    };

    internal static async Task WaitUntil(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!predicate() && DateTime.UtcNow < deadline)
            await Task.Delay(20);
    }

    internal sealed class EmptyProvider : IServiceProvider
    {
        public static readonly EmptyProvider Instance = new();
        public object? GetService(Type serviceType) => null;
    }
}
