using Orkeon.Analysis.Abstractions.Models;

namespace Orkeon.Analysis.Fingerprinters;

public static class AngularRules
{
    public const string Language = "typescript";
    public const string Framework = "angular";

    public static IReadOnlyList<FingerprintRule> Rules { get; } =
    [
        new("Component", Language, ["ui-component", Framework]),
        new("Directive", Language, ["directive", Framework]),
        new("Pipe",      Language, ["pipe", Framework]),
        new("NgModule",  Language, ["module", Framework]),
        new("Injectable", Language, ["service", Framework]),
        new("Input",     Language, ["input-binding", Framework]),
        new("Output",    Language, ["output-binding", Framework]),
        new("HostBinding", Language, ["host-binding", Framework]),
    ];

    public static FrameworkFingerprinter Create() => new(Framework, Rules);
}
