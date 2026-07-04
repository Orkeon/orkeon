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
        // builds and tests) is `Orkeon:Tools:Shell:AllowInterpreters = true` in appsettings;
        // the tool emits its security warning when active. Hosts without IConfiguration (or
        // without the flag) keep the strict read-only default.
        services.AddTransient<IBaseTool>(sp =>
        {
            var fs = sp.GetRequiredService<IFileSystemService>();
            var logger = sp.GetService<ILogger<ShellCommandTool>>();
            var allowInterpreters = sp.GetService<IConfiguration>()
                ?.GetValue("Orkeon:Tools:Shell:AllowInterpreters", false) ?? false;
            return new ShellCommandTool(fs, allowedCommands: null, blockedPatterns: null, logger,
                allowInterpreters: allowInterpreters);
        });
        // SecureCodeInterpreterTool is registered via AddOrkeonCodeSandbox() in InfrastructureExtensions
        return services;
    }
}
