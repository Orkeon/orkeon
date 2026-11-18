using System.Net.Http.Headers;
using System.Reflection;
using Orkeon.Domain.Constants.Http;

namespace Orkeon.Tools.Abstractions.Base;

/// <summary>
/// The User-Agent the HTTP tools send: <c>Orkeon/&lt;version of the running release&gt;</c> and where the project
/// lives (#418). The version is the informational version of this assembly, build metadata removed.
/// </summary>
internal static class ToolUserAgent
{
    /// <summary>The header value; the fixed <see cref="HttpDefaults.DefaultUserAgent"/> when the version is no valid product token.</summary>
    public static string Value { get; } = Build();

    private static string Build()
    {
        var informational = typeof(ToolUserAgent).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(ToolUserAgent).Assembly.GetName().Version?.ToString(3)
            ?? "";
        var plus = informational.IndexOf('+', StringComparison.Ordinal);
        var version = plus < 0 ? informational : informational[..plus];

        // A version built with characters a product token refuses must not take every HTTP tool down.
        var candidate = $"Orkeon/{version} (+https://github.com/Orkeon/orkeon)";
        using var probe = new HttpRequestMessage();
        return probe.Headers.UserAgent.TryParseAdd(candidate) ? candidate : HttpDefaults.DefaultUserAgent;
    }
}
