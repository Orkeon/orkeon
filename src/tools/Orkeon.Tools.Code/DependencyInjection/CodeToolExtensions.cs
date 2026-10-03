using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;

namespace Orkeon.Tools.Code.DependencyInjection;

/// <summary>
/// Extension methods for registering code tools.
/// Note: CodeInterpreterTool has been removed. Use SecureCodeInterpreterTool
/// registered via AddOrkeonCodeSandbox() in InfrastructureExtensions instead.
/// </summary>
public static class CodeToolExtensions
{
    /// <summary>
    /// Adds code tools to the service collection.
    /// </summary>
    public static IServiceCollection AddOrkeonCodeTools(this IServiceCollection services)
    {
        // Explicit factory: a bare AddTransient<IBaseTool, ShellCommandTool>() lets DI pick the
        // greediest ctor and resolve the optional `IEnumerable<string> allowedCommands` as an
        // EMPTY collection (no string services are registered) — and `empty ?? default` keeps the
        // empty set, so EVERY command is blocked ("not in the allowlist. Allowed: "). Passing
        // null explicitly restores the documented default allowlist (read-only commands only:
        // ls/cat/pwd/grep/… plus git restricted to read-only subcommands).
        //
        // SECURITY: interpreters (node/dotnet/npm/find) and mutating git subcommands are
        // intentionally NOT enabled by default — they are RCE-equivalent on the host. The
        // documented opt-in for trusted coding-agent hosts (which must run `git commit`,
        // builds and tests) is the appsettings flag `Orkeon:Tools:Shell:AllowInterpreters`,
        // set to true; the tool emits its security warning when active. Hosts without
        // IConfiguration (or without the flag) keep the strict read-only default.
        //
        // Allowlist keys (both string arrays, absent/empty section ⇒ null, see the trap
        // above — an empty-but-non-null list would block everything):
        //  - `Orkeon:Tools:Shell:ExtraAllowedCommands` — ADDITIVE on top of the default
        //    (or replacement) allowlist; the recommended way to allow make/cargo/etc.
        //    Composes with AllowInterpreters.
        //  - `Orkeon:Tools:Shell:AllowedCommands` — full REPLACEMENT, verbatim. Per the
        //    ctor contract it cancels AllowInterpreters and re-enables the git read-only
        //    subcommand restriction. Extras still union on top of it.
        //
        // The section is bound once, from the container's configuration when there is one, and
        // validated when a host starts (GAP-40): read inside the factory, an AllowInterpreters the
        // binder could not convert was an exception out of the registry, under --list-tools too.
        services.AddOptions<ShellToolOptions>()
            .Configure<IServiceProvider>((options, provider) =>
                provider.GetService<IConfiguration>()?.GetSection(ShellToolOptions.SectionName).Bind(options))
            .ValidateOnStart();
        services.AddTransient<IBaseTool>(sp =>
        {
            var fs = sp.GetRequiredService<IFileSystemService>();
            var logger = sp.GetService<ILogger<ShellCommandTool>>();
            var shell = sp.GetRequiredService<IOptions<ShellToolOptions>>().Value;
            return new ShellCommandTool(fs, allowedCommands: CommandList(shell.AllowedCommands), blockedPatterns: null, logger,
                allowInterpreters: shell.AllowInterpreters, extraAllowedCommands: CommandList(shell.ExtraAllowedCommands));
        });
        // SecureCodeInterpreterTool is registered via AddOrkeonCodeSandbox() in InfrastructureExtensions
        return services;
    }

    /// <summary>
    /// A bound command list as an allowlist, its blank entries dropped and an empty list mapped to
    /// <c>null</c> — never an empty array, which the ShellCommandTool ctor would take as "custom
    /// allowlist with zero entries" and block every command.
    /// </summary>
    [SuppressMessage("Major Code Smell", "S1168:Empty arrays and collections should be returned instead of null",
        Justification = "null is a third state here, not the absence of a value: the ShellCommandTool ctor reads null as " +
                        "\"no custom allowlist, keep the built-in read-only default\" and an empty array as \"custom allowlist " +
                        "with zero entries\", which blocks every command. Returning an empty array would silently disable the " +
                        "shell tool on any host whose configuration omits the section.")]
    private static string[]? CommandList(IEnumerable<string> commands)
    {
        var values = commands.Where(v => !string.IsNullOrWhiteSpace(v)).ToArray();
        return values is { Length: > 0 } ? values : null;
    }
}
