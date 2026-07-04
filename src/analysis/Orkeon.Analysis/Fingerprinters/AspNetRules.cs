using Orkeon.Analysis.Abstractions.Models;

namespace Orkeon.Analysis.Fingerprinters;

public static class AspNetRules
{
    public const string Language = "csharp";
    public const string Framework = "aspnet";

    public static IReadOnlyList<FingerprintRule> Rules { get; } =
    [
        new("ApiController", Language, ["api-controller", Framework]),
        new("HttpGet",       Language, ["http-endpoint", Framework]),
        new("HttpPost",      Language, ["http-endpoint", Framework]),
        new("HttpPut",       Language, ["http-endpoint", Framework]),
        new("HttpDelete",    Language, ["http-endpoint", Framework]),
        new("HttpPatch",     Language, ["http-endpoint", Framework]),
        new("HttpOptions",   Language, ["http-endpoint", Framework]),
        new("Authorize",     Language, ["auth", Framework]),
        new("AllowAnonymous", Language, ["auth", Framework]),
        new("Route",         Language, ["route", Framework]),
        new("FromBody",      Language, ["param-binding", Framework]),
        new("FromQuery",     Language, ["param-binding", Framework]),
        new("FromRoute",     Language, ["param-binding", Framework]),
        new("FromHeader",    Language, ["param-binding", Framework]),
        new("Middleware",    Language, ["middleware", Framework]),
    ];

    public static FrameworkFingerprinter Create() => new(Framework, Rules);
}
