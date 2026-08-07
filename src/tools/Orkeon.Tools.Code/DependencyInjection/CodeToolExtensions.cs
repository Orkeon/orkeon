using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
        services.AddTransient<IBaseTool>(sp =>
        {
            var fs = sp.GetRequiredService<IFileSystemService>();
            var logger = sp.GetService<ILogger<ShellCommandTool>>();
            var configuration = sp.GetService<IConfiguration>();
            var allowInterpreters = configuration
                ?.GetValue("Orkeon:Tools:Shell:AllowInterpreters", false) ?? false;
            var replacement = ReadCommandList(configuration, "Orkeon:Tools:Shell:AllowedCommands");
            var extras = ReadCommandList(configuration, "Orkeon:Tools:Shell:ExtraAllowedCommands");
            return new ShellCommandTool(fs, allowedCommands: replacement, blockedPatterns: null, logger,
                allowInterpreters: allowInterpreters, extraAllowedCommands: extras);
        });
        // SecureCodeInterpreterTool is registered via AddOrkeonCodeSandbox() in InfrastructureExtensions
        return services;
    }

    /// <summary>
    /// Reads a string-array config section into an allowlist, mapping an absent or empty
    /// section to <c>null</c> — never an empty array, which the ShellCommandTool ctor
    /// would take as "custom allowlist with zero entries" and block every command.
    /// </summary>
    private static string[]? ReadCommandList(IConfiguration? configuration, string key)
    {
        var values = configuration?.GetSection(key).GetChildren()
            .Select(c => c.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!)
            .ToArray();
        return values is { Length: > 0 } ? values : null;
    }
}
