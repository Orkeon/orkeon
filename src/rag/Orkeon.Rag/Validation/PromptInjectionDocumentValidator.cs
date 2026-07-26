using System.Text.RegularExpressions;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Rag.Validation;

/// <summary>
/// Detects prompt injection attempts embedded in documents entering the RAG pipeline,
/// including web content fetched by the opt-in web fallback (RAG-06/C1 security).
/// Deterministic heuristics only (no LLM, fully testable offline):
/// model-addressed directives (EN + FR), chat-template control tokens, hidden HTML
/// markup, massive HTML comments, exfiltration vectors (auto-loading markdown images,
/// encoded link payloads, data URIs) and long base64 blobs.
/// Exposes a rich <see cref="Analyze(string)"/> API (verdict + reasons + spans) and
/// stays pluggable in the ingestion <see cref="DataValidationPipeline"/> through
/// <see cref="IDataValidator"/>. Original port from RAG-02/C3.
/// </summary>
public sealed partial class PromptInjectionDocumentValidator : IDataValidator
{
    private const int ExcerptMaxLength = 80;

    /// <summary>Initializes the validator with the default policy (flag suspicious).</summary>
    public PromptInjectionDocumentValidator()
        : this(PromptInjectionPolicy.Default)
    {
    }

    /// <summary>Initializes the validator with an explicit handling policy.</summary>
    public PromptInjectionDocumentValidator(PromptInjectionPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        Policy = policy;
    }

    /// <summary>The configured handling policy (thresholds + suspicious handling).</summary>
    public PromptInjectionPolicy Policy { get; }

    /// <inheritdoc />
    public string Name => "PromptInjectionDetection";

    // ------------------------------------------------------------------
    // Direct injection patterns — English (high risk)
    // ------------------------------------------------------------------

    [GeneratedRegex(@"ignore\s+(all\s+)?previous\s+instructions", RegexOptions.IgnoreCase)]
    private static partial Regex IgnorePreviousInstructionsPattern();

    [GeneratedRegex(@"you\s+are\s+now\s+", RegexOptions.IgnoreCase)]
    private static partial Regex YouAreNowPattern();

    [GeneratedRegex(@"act\s+as\s+(if\s+)?", RegexOptions.IgnoreCase)]
    private static partial Regex ActAsPattern();

    [GeneratedRegex(@"pretend\s+(you\s+are|to\s+be)\s+", RegexOptions.IgnoreCase)]
    private static partial Regex PretendToBePattern();

    [GeneratedRegex(@"new\s+instructions\s*:", RegexOptions.IgnoreCase)]
    private static partial Regex NewInstructionsPattern();

    [GeneratedRegex(@"override\s+(your\s+)?(system|instructions|rules|guidelines)", RegexOptions.IgnoreCase)]
    private static partial Regex OverridePattern();

    [GeneratedRegex(@"disregard\s+(all\s+)?(previous|prior|above|your)", RegexOptions.IgnoreCase)]
    private static partial Regex DisregardPattern();

    // Requires an actual instruction-like object: a bare "don't forget to..." in
    // legitimate prose must not fire (false-positive control).
    [GeneratedRegex(@"forget\s+(all\s+|everything\s+)?(your?\s+)?(previous\s+|prior\s+)?(instructions|rules|guidelines|training|programming)", RegexOptions.IgnoreCase)]
    private static partial Regex ForgetPattern();

    // ------------------------------------------------------------------
    // Direct injection patterns — French variants (high risk)
    // ------------------------------------------------------------------

    [GeneratedRegex(@"ignore[zr]?\s+(toutes?\s+les\s+|les\s+|tes\s+|vos\s+)?instructions\s+(pr[ée]c[ée]dentes|ant[ée]rieures)", RegexOptions.IgnoreCase)]
    private static partial Regex IgnoreInstructionsFrPattern();

    [GeneratedRegex(@"oublie[zr]?\s+(toutes?\s+)?(tes|vos)\s+(instructions|r[èe]gles|consignes)", RegexOptions.IgnoreCase)]
    private static partial Regex ForgetFrPattern();

    [GeneratedRegex(@"(tu\s+es|vous\s+[êe]tes)\s+(maintenant|d[ée]sormais)\s+", RegexOptions.IgnoreCase)]
    private static partial Regex YouAreNowFrPattern();

    [GeneratedRegex(@"nouvelles\s+(instructions|consignes)\s*:", RegexOptions.IgnoreCase)]
    private static partial Regex NewInstructionsFrPattern();

    [GeneratedRegex(@"r[ée]v[èe]le[zr]?\s+(ton|votre)\s+(prompt|invite)\s+syst[èe]me", RegexOptions.IgnoreCase)]
    private static partial Regex RevealSystemPromptFrPattern();

    // ------------------------------------------------------------------
    // System prompt leaking patterns (high risk)
    // ------------------------------------------------------------------

    [GeneratedRegex(@"^system\s*:", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex SystemRolePattern();

    [GeneratedRegex(@"reveal\s+your\s+system\s+prompt", RegexOptions.IgnoreCase)]
    private static partial Regex RevealSystemPromptPattern();

    [GeneratedRegex(@"show\s+me\s+your\s+(system\s+)?prompt", RegexOptions.IgnoreCase)]
    private static partial Regex ShowPromptPattern();

    [GeneratedRegex(@"print\s+your\s+(initial\s+)?instructions", RegexOptions.IgnoreCase)]
    private static partial Regex PrintInstructionsPattern();

    // ------------------------------------------------------------------
    // Chat-template control tokens (high risk, density-sensitive: the score
    // accumulates per occurrence, so a burst of tokens escalates to Rejected)
    // ------------------------------------------------------------------

    [GeneratedRegex(@"<\|(im_start|im_end|system|user|assistant|endoftext)\|>|<<SYS>>|\[/?INST\]", RegexOptions.IgnoreCase)]
    private static partial Regex ChatControlTokenPattern();

    // ------------------------------------------------------------------
    // Suspicious markup: hidden HTML that survived text extraction
    // ------------------------------------------------------------------

    [GeneratedRegex(@"display\s*:\s*none|visibility\s*:\s*hidden|opacity\s*:\s*(0|0?\.0[0-9]*)(?![\d.])|font-size\s*:\s*0(px|pt|em|rem|%)?(?![\d.])", RegexOptions.IgnoreCase)]
    private static partial Regex HiddenStylePattern();

    [GeneratedRegex(@"aria-hidden\s*=\s*[""']?true", RegexOptions.IgnoreCase)]
    private static partial Regex AriaHiddenPattern();

    [GeneratedRegex(@"<!--(.*?)-->", RegexOptions.Singleline)]
    private static partial Regex HtmlCommentPattern();

    // ------------------------------------------------------------------
    // Exfiltration vectors
    // ------------------------------------------------------------------

    // Markdown image: auto-loaded by any renderer (zero-click exfiltration) when
    // the URL carries a long query payload.
    [GeneratedRegex(@"!\[[^\]]{0,200}\]\(\s*https?://[^)\s]{1,500}\?[^)\s]{32,}\)")]
    private static partial Regex MarkdownImageExfilPattern();

    // Markdown link whose query string is stuffed with percent-encoded data.
    [GeneratedRegex(@"\[[^\]]{0,200}\]\(\s*https?://[^)\s]*(%[0-9A-Fa-f]{2}){6,}[^)\s]*\)")]
    private static partial Regex MarkdownLinkEncodedPattern();

    [GeneratedRegex(@"data:[\w/+.\-]+;base64,[A-Za-z0-9+/=]{40,}", RegexOptions.IgnoreCase)]
    private static partial Regex DataUriPattern();

    // ------------------------------------------------------------------
    // Encoded payloads (medium risk)
    // ------------------------------------------------------------------

    [GeneratedRegex(@"base64[:\s]+[A-Za-z0-9+/=]{20,}", RegexOptions.IgnoreCase)]
    private static partial Regex Base64Pattern();

    // Standalone base64 blob of 120+ characters (opaque payload smuggling).
    [GeneratedRegex(@"(?<![A-Za-z0-9+/=])(?:[A-Za-z0-9+/]{4}){30,}(?:==|=)?")]
    private static partial Regex Base64BlobPattern();

    [GeneratedRegex(@"\\u[0-9a-fA-F]{4}(\\u[0-9a-fA-F]{4}){3,}", RegexOptions.None)]
    private static partial Regex UnicodeEscapePattern();

    private sealed record DetectionRule(string Id, string Description, double Weight, Regex Pattern);

    private static readonly DetectionRule[] DetectionRules =
    [
        // High-risk patterns (weight 0.4 / 0.35 each)
        new("ignore-previous", "Ignore previous instructions", 0.4, IgnorePreviousInstructionsPattern()),
        new("identity-override", "Identity override attempt", 0.35, YouAreNowPattern()),
        new("override", "Override attempt", 0.4, OverridePattern()),
        new("disregard", "Disregard instructions attempt", 0.4, DisregardPattern()),
        new("memory-reset", "Memory reset attempt", 0.35, ForgetPattern()),
        new("system-role", "System role injection", 0.4, SystemRolePattern()),
        new("reveal-prompt", "System prompt reveal", 0.35, RevealSystemPromptPattern()),
        new("show-prompt", "Prompt reveal request", 0.35, ShowPromptPattern()),
        new("print-instructions", "Instruction print request", 0.35, PrintInstructionsPattern()),
        new("new-instructions", "New instructions injection", 0.4, NewInstructionsPattern()),

        // French variants (same weights as their English counterparts)
        new("ignore-previous-fr", "Ignore previous instructions (FR)", 0.4, IgnoreInstructionsFrPattern()),
        new("memory-reset-fr", "Memory reset attempt (FR)", 0.35, ForgetFrPattern()),
        new("identity-override-fr", "Identity override attempt (FR)", 0.35, YouAreNowFrPattern()),
        new("new-instructions-fr", "New instructions injection (FR)", 0.4, NewInstructionsFrPattern()),
        new("reveal-prompt-fr", "System prompt reveal (FR)", 0.35, RevealSystemPromptFrPattern()),

        // Chat-template control tokens (per-occurrence accumulation = density heuristic)
        new("chat-control-token", "Chat-template control token", 0.3, ChatControlTokenPattern()),

        // Hidden markup surviving text extraction
        new("hidden-style", "Hidden HTML styling (display:none / visibility / opacity / font-size:0)", 0.35, HiddenStylePattern()),
        new("aria-hidden", "aria-hidden markup", 0.25, AriaHiddenPattern()),

        // Exfiltration vectors
        new("md-image-exfil", "Auto-loading markdown image with encoded query payload", 0.35, MarkdownImageExfilPattern()),
        new("md-link-encoded", "Markdown link with percent-encoded payload", 0.25, MarkdownLinkEncodedPattern()),
        new("data-uri", "Base64 data URI", 0.3, DataUriPattern()),

        // Medium-risk patterns (weight 0.25 each)
        new("act-as", "Act as pattern", 0.25, ActAsPattern()),
        new("pretend", "Pretend to be pattern", 0.25, PretendToBePattern()),

        // Encoded patterns
        new("base64-labelled", "Potential base64-encoded injection", 0.2, Base64Pattern()),
        new("base64-blob", "Long base64 blob (120+ chars)", 0.3, Base64BlobPattern()),
        new("unicode-escape", "Potential unicode-encoded injection", 0.2, UnicodeEscapePattern()),
    ];

    private const string MassiveCommentRuleId = "html-comment-mass";
    private const string MassiveCommentDescription = "Massive HTML comments (potential hidden payload)";
    private const double MassiveCommentWeight = 0.3;
    private const int MassiveCommentTotalThreshold = 500;
    private const double MassiveCommentRatioThreshold = 0.25;

    /// <summary>
    /// Runs the deterministic heuristic analysis on raw content.
    /// Never mutates the content: this is detection, not sanitization.
    /// </summary>
    public PromptInjectionAnalysis Analyze(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return PromptInjectionAnalysis.CleanResult;
        }

        var reasons = new List<string>();
        var spans = new List<InjectionSpan>();
        double totalScore = 0.0;

        foreach (var rule in DetectionRules)
        {
            var matches = rule.Pattern.Matches(content);
            if (matches.Count == 0)
            {
                continue;
            }

            reasons.Add($"{rule.Description} ({matches.Count} occurrence(s))");
            totalScore += rule.Weight * matches.Count;

            foreach (Match match in matches)
            {
                spans.Add(CreateSpan(rule.Id, rule.Description, content, match.Index, match.Length));
            }
        }

        totalScore += ScoreMassiveHtmlComments(content, reasons, spans);

        var riskScore = Math.Min(totalScore, 1.0);
        var verdict = riskScore > Policy.RejectThreshold
            ? PromptInjectionVerdict.Rejected
            : riskScore >= Policy.SuspiciousThreshold
                ? PromptInjectionVerdict.Suspicious
                : PromptInjectionVerdict.Clean;

        if (verdict == PromptInjectionVerdict.Clean && reasons.Count == 0)
        {
            return PromptInjectionAnalysis.CleanResult;
        }

        return new PromptInjectionAnalysis
        {
            Verdict = verdict,
            RiskScore = riskScore,
            Reasons = reasons,
            Spans = spans,
        };
    }

    /// <summary>Analyzes a <see cref="RagDocument"/>'s content.</summary>
    public PromptInjectionAnalysis Validate(RagDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Analyze(document.Content);
    }

    /// <summary>
    /// Applies the configured policy to a verdict: <see cref="PromptInjectionVerdict.Rejected"/>
    /// is always discarded; <see cref="PromptInjectionVerdict.Suspicious"/> only when the
    /// policy says <see cref="SuspiciousContentAction.Discard"/>.
    /// </summary>
    public bool ShouldDiscard(PromptInjectionVerdict verdict) =>
        verdict == PromptInjectionVerdict.Rejected
        || (verdict == PromptInjectionVerdict.Suspicious && Policy.SuspiciousAction == SuspiciousContentAction.Discard);

    /// <inheritdoc />
    public Task<DataValidationResult> ValidateAsync(string content, DataValidationContext context, CancellationToken ct = default)
    {
        var analysis = Analyze(content);

        var result = analysis.Verdict switch
        {
            PromptInjectionVerdict.Rejected => DataValidationResult.Rejected(
                "High-risk prompt injection detected in document",
                analysis.RiskScore,
                analysis.Reasons.ToList()),
            PromptInjectionVerdict.Suspicious => DataValidationResult.Quarantined(
                "Potential prompt injection detected in document",
                analysis.RiskScore,
                analysis.Reasons.ToList()),
            _ => DataValidationResult.Allowed(),
        };

        return Task.FromResult(result);
    }

    private static double ScoreMassiveHtmlComments(string content, List<string> reasons, List<InjectionSpan> spans)
    {
        var comments = HtmlCommentPattern().Matches(content);
        if (comments.Count == 0)
        {
            return 0.0;
        }

        var totalLength = 0;
        foreach (Match comment in comments)
        {
            totalLength += comment.Length;
        }

        var ratio = (double)totalLength / content.Length;
        if (totalLength <= MassiveCommentTotalThreshold && ratio <= MassiveCommentRatioThreshold)
        {
            return 0.0;
        }

        reasons.Add($"{MassiveCommentDescription} ({comments.Count} comment(s), {totalLength} chars, {ratio:P0} of content)");
        foreach (Match comment in comments)
        {
            spans.Add(CreateSpan(MassiveCommentRuleId, MassiveCommentDescription, content, comment.Index, comment.Length));
        }

        return MassiveCommentWeight;
    }

    private static InjectionSpan CreateSpan(string ruleId, string description, string content, int start, int length)
    {
        var excerptLength = Math.Min(length, ExcerptMaxLength);
        var excerpt = content.Substring(start, excerptLength);
        if (excerptLength < length)
        {
            excerpt += "…";
        }

        return new InjectionSpan(ruleId, description, start, length, excerpt);
    }
}
