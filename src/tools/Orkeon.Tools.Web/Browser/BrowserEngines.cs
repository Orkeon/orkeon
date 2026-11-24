using Microsoft.Extensions.Logging;
using Orkeon.Tools.Web.Firefox;

namespace Orkeon.Tools.Web.Browser;

/// <summary>The engines the tool knows, by the value of <c>Engine</c>.</summary>
internal static class BrowserEngines
{
    private static readonly Dictionary<string, Func<FirefoxToolOptions, ILogger?, IBrowserEngine>> s_engines =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [FirefoxEngine.EngineId] = static (options, logger) => new FirefoxEngine(options, logger),
        };

    /// <summary>The engine the options name.</summary>
    /// <exception cref="InvalidOperationException">The options name no engine the tool knows: the value is never written, another engine never taken.</exception>
    public static IBrowserEngine Create(FirefoxToolOptions options, ILogger? logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        return s_engines.TryGetValue(options.Engine.Trim(), out var create)
            ? create(options, logger)
            : throw new InvalidOperationException(
                $"{options.Section}:{nameof(FirefoxToolOptions.Engine)} is not a browser engine: write one of {string.Join(", ", s_engines.Keys)}.");
    }
}
