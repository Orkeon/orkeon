using Orkeon.Analysis.Abstractions.Models;

namespace Orkeon.Analysis.Fingerprinters;

public static class FlaskRules
{
    public const string Language = "python";
    public const string Framework = "flask";

    public static IReadOnlyList<FingerprintRule> Rules { get; } =
    [
        new("app.route",        Language, ["http-endpoint", Framework]),
        new("blueprint.route",  Language, ["http-endpoint", Framework, "blueprint"]),
        new("bp.route",         Language, ["http-endpoint", Framework, "blueprint"]),
        new("login_required",   Language, ["auth", Framework]),
        new("before_request",   Language, ["middleware", Framework]),
    ];

    public static FrameworkFingerprinter Create() => new(Framework, Rules);
}
