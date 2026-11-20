namespace Orkeon.Constants.Protocol;

/// <summary>The <c>mode</c> of a <see cref="RunEventKinds.RunDecision"/> event: the crew's <c>decision.mode</c> (#429).</summary>
public static class RunDecisionModes
{
    /// <summary>The crew declares no mode: it behaves as before the key existed.</summary>
    public const string Unspecified = "unspecified";

    /// <summary>The crew never asks System One anything.</summary>
    public const string None = "none";

    /// <summary>The crew runs with System One or without it, the launch picks which.</summary>
    public const string Optional = "optional";

    /// <summary>The crew cannot run without System One.</summary>
    public const string Required = "required";

    /// <summary>What <c>--validate</c> reports for a procedural script, which it bundles and never loads.</summary>
    public const string Unknown = "unknown";
}

/// <summary>The <c>circuit</c> of a <see cref="RunEventKinds.RunDecision"/> event.</summary>
public static class DecisionCircuitStates
{
    /// <summary>The decision model is asked.</summary>
    public const string On = "on";

    /// <summary>No call reaches the decision model.</summary>
    public const string Off = "off";
}

/// <summary>The <c>cause</c> of a <see cref="RunEventKinds.RunDecision"/> event whose circuit is off.</summary>
public static class DecisionCircuitCauses
{
    /// <summary>The <c>--no-decision</c> switch.</summary>
    public const string Switch = "switch";

    /// <summary><c>Orkeon:Decision:Enabled=false</c>.</summary>
    public const string Settings = "settings";

    /// <summary>No decision provider resolved.</summary>
    public const string NoProvider = "no-provider";
}
