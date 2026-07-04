using Orkeon.Analysis.Abstractions.Models;

namespace Orkeon.Analysis.Fingerprinters;

public static class FastApiRules
{
    public const string Language = "python";
    public const string Framework = "fastapi";

    public static IReadOnlyList<FingerprintRule> Rules { get; } =
    [
        new("app.get",      Language, ["http-endpoint", Framework]),
        new("app.post",     Language, ["http-endpoint", Framework]),
        new("app.put",      Language, ["http-endpoint", Framework]),
        new("app.delete",   Language, ["http-endpoint", Framework]),
        new("router.get",   Language, ["http-endpoint", Framework]),
        new("router.post",  Language, ["http-endpoint", Framework]),
        new("router.put",   Language, ["http-endpoint", Framework]),
        new("router.delete", Language, ["http-endpoint", Framework]),
        new("Depends",      Language, ["dependency-injection", Framework]),
    ];

    public static FrameworkFingerprinter Create() => new(Framework, Rules);
}
