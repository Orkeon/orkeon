using Orkeon.Analysis.Abstractions.Models;

namespace Orkeon.Analysis.Fingerprinters;

public static class NestJsRules
{
    public const string Language = "typescript";
    public const string Framework = "nestjs";

    public static IReadOnlyList<FingerprintRule> Rules { get; } =
    [
        new("Controller",   Language, ["http-controller", Framework]),
        new("Injectable",   Language, ["service", Framework]),
        new("Module",       Language, ["module", Framework]),
        new("Get",          Language, ["http-endpoint", Framework]),
        new("Post",         Language, ["http-endpoint", Framework]),
        new("Put",          Language, ["http-endpoint", Framework]),
        new("Delete",       Language, ["http-endpoint", Framework]),
        new("Patch",        Language, ["http-endpoint", Framework]),
        new("Guard",        Language, ["guard", Framework]),
        new("UseGuards",    Language, ["guard", Framework]),
        new("Middleware",   Language, ["middleware", Framework]),
        new("Pipe",         Language, ["pipe", Framework]),
        new("UsePipes",     Language, ["pipe", Framework]),
        new("Interceptor",  Language, ["interceptor", Framework]),
        new("UseInterceptors", Language, ["interceptor", Framework]),
    ];

    public static FrameworkFingerprinter Create() => new(Framework, Rules);
}
