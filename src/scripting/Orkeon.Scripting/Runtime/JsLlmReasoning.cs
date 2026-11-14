using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Scripting.Runtime;

#pragma warning disable IDE1006 // JS-facing members are camelCase on purpose.
#pragma warning disable CS1591 // JS-interop mirror of LlmReasoning and LlmCapabilities in Typings/llm.d.ts; that declaration is the contract scripts read.

/// <summary>
/// What the runtime does with the effort a call asks for (#360): <c>reasoning</c> on <c>LlmConfig</c>, on the
/// result of <c>act</c> and <c>chat</c>, and on the object <c>stream()</c> returns.
/// </summary>
public sealed class JsLlmReasoning
{
    public string model { get; }
    public string asked { get; }
    public string? sent { get; }

    /// <summary><c>lowered</c>, <c>dropped</c> or <c>unchecked</c>; null when the effort is sent as asked on a model the table knows.</summary>
    public string? notice { get; }

    private JsLlmReasoning(LlmEffortReport report)
    {
        model = report.Model;
        asked = report.Asked;
        sent = report.Sent;
        notice = report.Notice switch
        {
            LlmEffortNotice.Lowered => "lowered",
            LlmEffortNotice.Dropped => "dropped",
            LlmEffortNotice.Unchecked => "unchecked",
            _ => null,
        };
    }

    /// <summary>The script's view of <paramref name="report"/>; null when the call asks for no effort or the provider cannot tell.</summary>
    internal static JsLlmReasoning? From(LlmEffortReport? report) => report is null ? null : new JsLlmReasoning(report);
}

/// <summary>What a provider and one of its models take for reasoning (#359): <c>capabilities</c> on <c>LlmConfig</c>.</summary>
public sealed class JsLlmCapabilities
{
    public string model { get; }
    public bool known { get; }

    /// <summary><c>none</c>, <c>effortOnly</c>, <c>toggle</c> or <c>budget</c>.</summary>
    public string thinking { get; }

    /// <summary>The levels a call sees sent as asked, in order; empty when none is.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "JS-facing: the engine copies a CLR array into a JS array (ArrayConversionMode.Copy), which the typings declare; a fresh copy per instance, never shared.")]
    public string[] efforts { get; }

    private JsLlmCapabilities(LlmReasoningCapabilities capabilities)
    {
        model = capabilities.Model;
        known = capabilities.Known;
        thinking = capabilities.Thinking switch
        {
            ThinkingSupport.EffortOnly => "effortOnly",
            ThinkingSupport.Toggle => "toggle",
            ThinkingSupport.Budget => "budget",
            _ => "none",
        };
        efforts = [.. capabilities.Efforts];
    }

    /// <summary>The script's view of <paramref name="capabilities"/>; null when the provider cannot tell.</summary>
    internal static JsLlmCapabilities? From(LlmReasoningCapabilities? capabilities)
        => capabilities is null ? null : new JsLlmCapabilities(capabilities);
}

#pragma warning restore CS1591
#pragma warning restore IDE1006
