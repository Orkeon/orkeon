using Microsoft.Extensions.Logging;
using Orkeon.Tools.Web.Chromium;
using Orkeon.Tools.Web.Firefox;
using Orkeon.Tools.Web.Safari;

namespace Orkeon.Tools.Web.Browser;

/// <summary>The engines the tool knows, by the value of <c>Engine</c>.</summary>
internal static class BrowserEngines
{
    private static readonly Dictionary<string, Func<FirefoxToolOptions, ILogger?, IBrowserEngine>> s_engines =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [FirefoxEngine.EngineId] = static (options, logger) => new FirefoxEngine(options, logger),
            [ChromiumVariant.Chrome.Id] = static (options, logger) => new ChromiumEngine(ChromiumVariant.Chrome, options, logger),
            [ChromiumVariant.Edge.Id] = static (options, logger) => new ChromiumEngine(ChromiumVariant.Edge, options, logger),
            [ChromiumVariant.Brave.Id] = static (options, logger) => new ChromiumEngine(ChromiumVariant.Brave, options, logger),
            [SafariEngine.EngineId] = static (options, _) => new SafariEngine(options),
        };

    /// <summary>The engine the options name.</summary>
    /// <exception cref="InvalidOperationException">The options name no engine the tool knows: the value is never written, another engine never taken. Or they ask for a headless mode the engine lacks.</exception>
    public static IBrowserEngine Create(FirefoxToolOptions options, ILogger? logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!s_engines.TryGetValue(options.Engine.Trim(), out var create))
        {
            throw new InvalidOperationException(
                $"{options.Section}:{nameof(FirefoxToolOptions.Engine)} is not a browser engine: write one of {string.Join(", ", s_engines.Keys)}.");
        }

        var engine = create(options, logger);
        return options.Headless && !engine.Capabilities.Headless
            ? throw new InvalidOperationException(
                $"{options.Section}:{nameof(FirefoxToolOptions.Headless)} cannot be true with the engine {engine.Id}: {engine.DisplayName} has no headless mode.")
            : engine;
    }
}
