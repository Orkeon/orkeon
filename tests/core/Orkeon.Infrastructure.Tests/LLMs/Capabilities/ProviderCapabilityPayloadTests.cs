using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.LLM;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Tests.TestDoubles;
using Polly;
using static Orkeon.Tests.Shared.Constants.TestDataConstants;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Infrastructure.Tests.LLMs;

/// <summary>
/// Cross-provider coverage of the capability foundation (LLM-02) and of the two options it
/// carries: structured outputs (LLM-03) and thinking (LLM-04).
/// </summary>
/// <remarks>
/// The point of the audit was not that the wiring was missing but that its absence was
/// invisible: a YAML crew could declare <c>response_format</c> or <c>thinking</c> and the
/// option would be dropped without a trace on ten providers out of twelve. So every test here
/// asserts one of two things — the field reaches the wire in the provider's own dialect, or an
/// actionable warning is emitted. Never silence.
/// </remarks>
public class ProviderCapabilityPayloadTests
{
    private const string SampleSchema =
        """{"type":"object","properties":{"answer":{"type":"string"}},"required":["answer"]}""";

    /// <summary>Every provider, by the type name its HTTP client is registered under.</summary>
    public static TheoryData<string> AllProviders() =>
    [
        nameof(OpenAILlmProvider), nameof(AzureOpenAILlmProvider),
        nameof(TogetherAiLlmProvider), nameof(MistralLlmProvider), nameof(KimiLlmProvider),
        nameof(QwenLlmProvider), nameof(HuggingFaceLlmProvider), nameof(ZaiLlmProvider),
        nameof(DeepSeekLlmProvider), nameof(AnthropicLlmProvider), nameof(OllamaLlmProvider),
        nameof(GrokLlmProvider), nameof(MiniMaxLlmProvider), nameof(GeminiLlmProvider),
        nameof(OpenRouterLlmProvider), nameof(MammouthLlmProvider),
    ];

    // ── The declaration itself ──────────────────────────────────────────────

    /// <summary>
    /// The declaration is what the whole mechanism reads, so it is reviewed in diff rather
    /// than left implicit. Only DeepSeek and MiniMax replay reasoning content (a separate
    /// field for one, a re-inlined think block for the other); only Anthropic needs
    /// explicit cache breakpoints — the claims the audit found most often over-generalised.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllProviders))]
    public void ShouldDeclareCapabilities_ForEveryProvider(string providerTypeName)
    {
        var provider = ProviderProbe.Create(providerTypeName);
        try
        {
            var capabilities = provider.Capabilities;

            Assert.NotSame(LlmProviderCapabilities.Unknown, capabilities);
            Assert.Equal(
                providerTypeName is nameof(DeepSeekLlmProvider) or nameof(MiniMaxLlmProvider),
                capabilities.ReplaysReasoningContent);
            Assert.Equal(providerTypeName == nameof(AnthropicLlmProvider), capabilities.ExplicitPromptCaching);
            Assert.Equal(providerTypeName == nameof(DeepSeekLlmProvider), capabilities.RequiresJsonKeywordInPrompt);
            // No vendor API documents a GBNF `grammar` field: only the configuration turns it on.
            Assert.False(capabilities.GbnfGrammar);
        }
        finally
        {
            (provider as IDisposable)?.Dispose();
        }
    }

    // ── Structured outputs, in each dialect (G-15) ──────────────────────────

    [Theory]
    [InlineData(nameof(OpenAILlmProvider))]
    [InlineData(nameof(AzureOpenAILlmProvider))]
    [InlineData(nameof(TogetherAiLlmProvider))]
    [InlineData(nameof(MistralLlmProvider))]
    [InlineData(nameof(KimiLlmProvider))]
    [InlineData(nameof(QwenLlmProvider))]
    [InlineData(nameof(HuggingFaceLlmProvider))]
    [InlineData(nameof(ZaiLlmProvider))]
    [InlineData(nameof(DeepSeekLlmProvider))]
    [InlineData(nameof(GrokLlmProvider))]
    [InlineData(nameof(OpenRouterLlmProvider))]
    public async Task ShouldSendJsonObjectConstraint_OnTheOpenAiCompatibleFamily(string providerTypeName)
    {
        var body = await ProviderProbe.CapturePayloadAsync(providerTypeName, config => config with
        {
            // "json" in the prompt keeps DeepSeek's own guard quiet; irrelevant elsewhere.
            SystemMessage = "Answer in json.",
            ResponseFormat = LlmResponseFormat.JsonObject(),
        });

        Assert.True(body.TryGetProperty("response_format", out var value), "expected `response_format` on the wire");
        Assert.Equal("json_object", value.GetProperty("type").GetString());
    }

    /// <summary>
    /// Anthropic has **no schema-less JSON mode**: <c>output_config.format</c> takes exactly
    /// <c>type</c> (always <c>"json_schema"</c>) and <c>schema</c>. A bare <c>json_object</c>
    /// request cannot be expressed at the API level, so it is reported rather than sent in a
    /// shape the API would reject.
    /// </summary>
    [Fact]
    public async Task ShouldReportAJsonObjectRequest_OnAnthropicWhichHasNoSchemaLessMode()
    {
        var probe = await ProviderProbe.CapturePayloadWithLogAsync(nameof(AnthropicLlmProvider), config => config with
        {
            ResponseFormat = LlmResponseFormat.JsonObject(),
        });

        Assert.False(probe.Body.TryGetProperty("output_config", out _));
        Assert.Contains(probe.Warnings, w => w.Contains("response_format without a schema", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ShouldSendTheSchema_AsAnthropicOutputConfigFormat()
    {
        var body = await ProviderProbe.CapturePayloadAsync(nameof(AnthropicLlmProvider), config => config with
        {
            ResponseFormat = LlmResponseFormat.JsonSchema("answer_shape", SampleSchema),
        });

        var format = body.GetProperty("output_config").GetProperty("format");
        Assert.Equal("json_schema", format.GetProperty("type").GetString());
        Assert.Equal(JsonValueKind.Object, format.GetProperty("schema").ValueKind);
        // The object takes exactly two keys — no name, no strict (strict lives on tools).
        Assert.Equal(2, format.EnumerateObject().Count());
    }

    /// <summary>Ollama's dialect is a bare <c>format</c> field, not a nested object.</summary>
    [Fact]
    public async Task ShouldSendJsonObjectConstraint_AsOllamaFormatField()
    {
        var body = await ProviderProbe.CapturePayloadAsync(nameof(OllamaLlmProvider), config => config with
        {
            ResponseFormat = LlmResponseFormat.JsonObject(),
        });

        Assert.Equal("json", body.GetProperty("format").GetString());
    }

    [Theory]
    [InlineData(nameof(OpenAILlmProvider))]
    [InlineData(nameof(AzureOpenAILlmProvider))]
    [InlineData(nameof(GrokLlmProvider))]
    [InlineData(nameof(TogetherAiLlmProvider))]
    [InlineData(nameof(MistralLlmProvider))]
    [InlineData(nameof(OpenRouterLlmProvider))]
    public async Task ShouldSendTheSchema_WhenTheProviderValidatesOne(string providerTypeName)
    {
        var body = await ProviderProbe.CapturePayloadAsync(providerTypeName, config => config with
        {
            ResponseFormat = LlmResponseFormat.JsonSchema("answer_shape", SampleSchema),
        });

        var format = body.GetProperty("response_format");
        Assert.Equal("json_schema", format.GetProperty("type").GetString());

        var schema = format.GetProperty("json_schema");
        Assert.Equal("answer_shape", schema.GetProperty("name").GetString());
        Assert.True(schema.GetProperty("strict").GetBoolean());
        // The schema must travel as a JSON object, not as an escaped string.
        Assert.Equal(JsonValueKind.Object, schema.GetProperty("schema").ValueKind);
        Assert.Equal("string",
            schema.GetProperty("schema").GetProperty("properties").GetProperty("answer")
                .GetProperty("type").GetString());
    }

    /// <summary>
    /// The aggregator declared from documentation alone (LLM-09, D-04): what the vendor does
    /// not document is reported, never written — the MiniMax rule, until a campaign measures it.
    /// </summary>
    [Theory]
    [InlineData(nameof(MiniMaxLlmProvider))]
    [InlineData(nameof(MammouthLlmProvider))]
    public async Task ShouldReportAResponseFormat_WhereNothingIsDeclared(string providerTypeName)
    {
        var probe = await ProviderProbe.CapturePayloadWithLogAsync(providerTypeName, config => config with
        {
            ResponseFormat = LlmResponseFormat.JsonObject(),
        });

        Assert.False(probe.Body.TryGetProperty("response_format", out _));
        Assert.Contains(probe.Warnings, w => w.Contains("response_format", StringComparison.Ordinal));
    }

    /// <summary>
    /// A provider that only guarantees well-formed JSON must degrade explicitly, so the caller
    /// learns the schema was not enforced instead of trusting a validation that never ran.
    /// </summary>
    [Theory]
    [InlineData(nameof(KimiLlmProvider))]
    [InlineData(nameof(ZaiLlmProvider))]
    [InlineData(nameof(HuggingFaceLlmProvider))]
    public async Task ShouldDowngradeSchemaToJsonObject_AndSaySo_WhenTheProviderCannotValidateIt(
        string providerTypeName)
    {
        var probe = await ProviderProbe.CapturePayloadWithLogAsync(providerTypeName, config => config with
        {
            ResponseFormat = LlmResponseFormat.JsonSchema("answer_shape", SampleSchema),
        });

        Assert.Equal("json_object", probe.Body.GetProperty("response_format").GetProperty("type").GetString());
        Assert.False(probe.Body.GetProperty("response_format").TryGetProperty("json_schema", out _));
        Assert.Contains(probe.Warnings, w => w.Contains("response_format.schema", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(AllProviders))]
    public async Task ShouldWriteNothing_WhenTheFormatIsTextOrUnset(string providerTypeName)
    {
        var textual = await ProviderProbe.CapturePayloadAsync(providerTypeName, config => config with
        {
            ResponseFormat = LlmResponseFormat.Text(),
        });
        var unset = await ProviderProbe.CapturePayloadAsync(providerTypeName, config => config);

        foreach (var body in new[] { textual, unset })
        {
            Assert.False(body.TryGetProperty("response_format", out _));
            Assert.False(body.TryGetProperty("format", out _));
            Assert.False(body.TryGetProperty("output_config", out _));
        }
    }

    // ── Thinking, in each dialect (G-16 / G-19) ─────────────────────────────

    [Theory]
    [InlineData(nameof(OpenAILlmProvider))]
    [InlineData(nameof(AzureOpenAILlmProvider))]
    [InlineData(nameof(GrokLlmProvider))]
    [InlineData(nameof(MistralLlmProvider))]
    [InlineData(nameof(KimiLlmProvider))]
    [InlineData(nameof(DeepSeekLlmProvider))]
    [InlineData(nameof(ZaiLlmProvider))]
    public async Task ShouldSendReasoningEffort_OnTheOpenAiCompatibleFamily(string providerTypeName)
    {
        var body = await ProviderProbe.CapturePayloadAsync(providerTypeName, config => config with
        {
            Thinking = new LlmThinkingConfig { Effort = "high" },
        });

        Assert.Equal("high", body.GetProperty("reasoning_effort").GetString());
    }

    /// <summary>
    /// OpenRouter's dialect carries the whole thinking intention in one <c>reasoning</c>
    /// object (LLM-09, D-04) — the first-level <c>reasoning_effort</c> is removed so the same
    /// effort is not sent twice, and the DeepSeek/GLM block is never written.
    /// </summary>
    [Fact]
    public async Task ShouldSendTheReasoningObject_OnOpenRouter()
    {
        var body = await ProviderProbe.CapturePayloadAsync(nameof(OpenRouterLlmProvider), config => config with
        {
            Thinking = new LlmThinkingConfig { Enabled = true, Effort = "low", BudgetTokens = 2048 },
        });

        var reasoning = body.GetProperty("reasoning");
        Assert.True(reasoning.GetProperty("enabled").GetBoolean());
        Assert.Equal("low", reasoning.GetProperty("effort").GetString());
        Assert.Equal(2048, reasoning.GetProperty("max_tokens").GetInt32());
        Assert.False(body.TryGetProperty("thinking", out _));
        Assert.False(body.TryGetProperty("reasoning_effort", out _));
    }

    /// <summary>The four providers campaign M18 measured taking the effort hint, which declared nothing before (#356).</summary>
    [Theory]
    [InlineData(nameof(MiniMaxLlmProvider))]
    [InlineData(nameof(MammouthLlmProvider))]
    [InlineData(nameof(TogetherAiLlmProvider))]
    [InlineData(nameof(HuggingFaceLlmProvider))]
    public async Task ShouldSendTheEffort_WhereTheCampaignMeasuredIt(string providerTypeName)
    {
        var probe = await ProviderProbe.CapturePayloadWithLogAsync(providerTypeName, config => config with
        {
            Thinking = new LlmThinkingConfig { Effort = "max" },
        });

        Assert.Equal("max", probe.Body.GetProperty("reasoning_effort").GetString());
        Assert.DoesNotContain(probe.Warnings, w => w.Contains("was declared", StringComparison.Ordinal));
    }

    /// <summary>Each of them carries the switch in its own field; MiniMax only the off value, which M3 alone accepts.</summary>
    [Theory]
    [InlineData(nameof(TogetherAiLlmProvider), false, """{"reasoning":{"enabled":false}}""")]
    [InlineData(nameof(TogetherAiLlmProvider), true, """{"reasoning":{"enabled":true}}""")]
    [InlineData(nameof(HuggingFaceLlmProvider), false, """{"chat_template_kwargs":{"enable_thinking":false}}""")]
    [InlineData(nameof(MiniMaxLlmProvider), false, """{"thinking":{"type":"disabled"}}""")]
    [InlineData(nameof(MiniMaxLlmProvider), true, "{}")]
    public async Task ShouldWriteTheSwitchInTheDialectsOwnField(string providerTypeName, bool enabled, string expected)
    {
        var body = await ProviderProbe.CapturePayloadAsync(providerTypeName, config => config with
        {
            Thinking = new LlmThinkingConfig { Enabled = enabled },
        });

        using var wanted = JsonDocument.Parse(expected);
        foreach (var field in new[] { "reasoning", "chat_template_kwargs", "thinking" })
        {
            if (wanted.RootElement.TryGetProperty(field, out var value))
                Assert.Equal(value.GetRawText(), body.GetProperty(field).GetRawText());
            else
                Assert.False(body.TryGetProperty(field, out _), field);
        }
    }

    /// <summary>
    /// Providers whose API decides on its own whether to reason accept the effort hint but not
    /// an on/off switch — and say so rather than pretending the switch was honoured.
    /// </summary>
    [Theory]
    [InlineData(nameof(OpenAILlmProvider))]
    [InlineData(nameof(GrokLlmProvider))]
    public async Task ShouldReportTheToggle_WhenOnlyAnEffortHintIsAccepted(string providerTypeName)
    {
        var probe = await ProviderProbe.CapturePayloadWithLogAsync(providerTypeName, config => config with
        {
            Thinking = new LlmThinkingConfig { Enabled = false },
        });

        Assert.False(probe.Body.TryGetProperty("thinking", out _));
        Assert.Contains(probe.Warnings, w => w.Contains("thinking.enabled", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ShouldSendAdaptiveThinkingAndEffort_OnAnthropic()
    {
        var body = await ProviderProbe.CapturePayloadAsync(nameof(AnthropicLlmProvider), config => config with
        {
            Thinking = new LlmThinkingConfig { Enabled = true, Effort = "high" },
        });

        Assert.Equal("adaptive", body.GetProperty("thinking").GetProperty("type").GetString());
        // Without it every Claude 5 model returns its thinking blocks empty (campaign M18, #356).
        Assert.Equal("summarized", body.GetProperty("thinking").GetProperty("display").GetString());
        Assert.Equal("high", body.GetProperty("output_config").GetProperty("effort").GetString());
    }

    /// <summary>The Anthropic off switch per (model, effort), measured by campaign M18 (#356).</summary>
    [Theory]
    [InlineData("claude-sonnet-5-5", "high", "between_tools")]
    [InlineData("claude-sonnet-5-5-20261001", null, "between_tools")]
    [InlineData("claude-sonnet-5", "xhigh", "disabled")]
    [InlineData("claude-haiku-5-5", "high", "disabled")]
    public async Task ShouldSendTheModelsOwnOffValue_OnAnthropic(string model, string? effort, string off)
    {
        var probe = await ProviderProbe.CapturePayloadWithLogAsync(nameof(AnthropicLlmProvider), config => config with
        {
            Model = model,
            Thinking = new LlmThinkingConfig { Enabled = false, Effort = effort },
        });

        var thinking = probe.Body.GetProperty("thinking");
        Assert.Equal(off, thinking.GetProperty("type").GetString());
        // between_tools refuses any other field, display included.
        Assert.False(thinking.TryGetProperty("display", out _));
        Assert.DoesNotContain(probe.Warnings, w => w.Contains("thinking.enabled", StringComparison.Ordinal));
    }

    /// <summary>Above the highest effort a model takes its off value with, the switch is omitted and the effort sent alone.</summary>
    [Theory]
    [InlineData("claude-sonnet-5-5", "xhigh")]
    [InlineData("claude-haiku-5-5", "xhigh")]
    [InlineData("claude-haiku-5-5", "max")]
    public async Task ShouldOmitTheOffSwitchAboveTheEffortTheModelTakesItWith_OnAnthropic(string model, string effort)
    {
        var probe = await ProviderProbe.CapturePayloadWithLogAsync(nameof(AnthropicLlmProvider), config => config with
        {
            Model = model,
            Thinking = new LlmThinkingConfig { Enabled = false, Effort = effort },
        });

        Assert.False(probe.Body.TryGetProperty("thinking", out _));
        Assert.Equal(effort, probe.Body.GetProperty("output_config").GetProperty("effort").GetString());
        Assert.Contains(probe.Warnings, w => w.Contains("cannot switch thinking off at effort", StringComparison.Ordinal));
    }

    /// <summary>Opus 5.5 and the Fable models refuse every off value: omitted with a warning, while on is sent as adaptive.</summary>
    [Theory]
    [InlineData("claude-opus-5-5")]
    [InlineData("claude-fable-5-2")]
    public async Task ShouldOmitTheOffSwitchAndKeepAdaptive_OnAnAlwaysThinkingClaude(string model)
    {
        var off = await ProviderProbe.CapturePayloadWithLogAsync(nameof(AnthropicLlmProvider), config => config with
        {
            Model = model,
            Thinking = new LlmThinkingConfig { Enabled = false },
        });
        var on = await ProviderProbe.CapturePayloadWithLogAsync(nameof(AnthropicLlmProvider), config => config with
        {
            Model = model,
            Thinking = new LlmThinkingConfig { Enabled = true },
        });

        Assert.False(off.Body.TryGetProperty("thinking", out _));
        Assert.Contains(off.Warnings, w => w.Contains("always reasons", StringComparison.Ordinal));
        Assert.Equal("adaptive", on.Body.GetProperty("thinking").GetProperty("type").GetString());
        Assert.Empty(on.Warnings);
    }

    /// <summary>The 4.x generation knows no level above high: the effort is lowered there, said aloud.</summary>
    [Fact]
    public async Task ShouldLowerTheEffortOnAClaude4Model_OnAnthropic()
    {
        var probe = await ProviderProbe.CapturePayloadWithLogAsync(nameof(AnthropicLlmProvider), config => config with
        {
            Model = "claude-opus-4-1",
            Thinking = new LlmThinkingConfig { Effort = "max" },
        });

        Assert.Equal("high", probe.Body.GetProperty("output_config").GetProperty("effort").GetString());
        Assert.Contains(probe.Warnings, w => w.Contains("Effort 'max'", StringComparison.Ordinal));
    }

    /// <summary>Ollama reads the same table: a model the host declares always-thinking keeps its effort, not its off switch.</summary>
    [Fact]
    public async Task ShouldJudgeThinkAgainstTheModel_OnOllama()
    {
        var probe = await ProviderProbe.CapturePayloadWithLogAsync(nameof(OllamaLlmProvider), config => config with
        {
            Model = "glm-5.3:cloud",
            Thinking = new LlmThinkingConfig { Enabled = false },
        });

        Assert.False(probe.Body.TryGetProperty("think", out _));
        Assert.Contains(probe.Warnings, w => w.Contains("always reasons", StringComparison.Ordinal));
    }

    /// <summary>
    /// Anthropic's <c>output_config.effort</c> knows the five levels: <c>xhigh</c> and <c>max</c>
    /// travel untouched, and nothing is reported.
    /// </summary>
    [Theory]
    [InlineData("xhigh")]
    [InlineData("max")]
    public async Task ShouldSendTheEffortAboveHighUntouched_OnAnthropic(string effort)
    {
        var probe = await ProviderProbe.CapturePayloadWithLogAsync(nameof(AnthropicLlmProvider), config => config with
        {
            Thinking = new LlmThinkingConfig { Enabled = true, Effort = effort },
        });

        Assert.Equal(effort, probe.Body.GetProperty("output_config").GetProperty("effort").GetString());
        Assert.DoesNotContain(probe.Warnings, w => w.Contains($"Effort '{effort}'", StringComparison.Ordinal));
    }

    /// <summary>
    /// OpenAI's and Grok's <c>reasoning_effort</c> know four levels, <c>xhigh</c> included (campaign M18, #356):
    /// <c>xhigh</c> travels, <c>max</c> is sent as <c>xhigh</c>, and the lowering is reported (LLM-02).
    /// </summary>
    [Theory]
    [InlineData(nameof(OpenAILlmProvider), "xhigh", "xhigh")]
    [InlineData(nameof(OpenAILlmProvider), "max", "xhigh")]
    [InlineData(nameof(GrokLlmProvider), "xhigh", "xhigh")]
    [InlineData(nameof(GrokLlmProvider), "max", "xhigh")]
    public async Task ShouldLowerTheEffortToXhighAndReportIt_WhereFourLevelsAreKnown(string providerTypeName, string effort, string sent)
    {
        var probe = await ProviderProbe.CapturePayloadWithLogAsync(providerTypeName, config => config with
        {
            Thinking = new LlmThinkingConfig { Effort = effort },
        });

        Assert.Equal(sent, probe.Body.GetProperty("reasoning_effort").GetString());
        Assert.Equal(sent != effort, probe.Warnings.Any(w => w.Contains($"Effort '{effort}'", StringComparison.Ordinal)));
    }

    /// <summary>
    /// Z.AI's field knows the five levels (campaign of 2026-11-14, #356): GLM-5.2 takes and reasons at
    /// <c>xhigh</c>, so it travels; GLM-5.3 keeps its own <c>low</c>, <c>high</c>, <c>max</c> and lowers it.
    /// </summary>
    [Theory]
    [Trait("BR", "CH-356")]
    [InlineData("glm-5.2", "xhigh")]
    [InlineData("glm-5.3", "high")]
    public async Task ShouldSendXhighWhereTheModelTakesIt_OnZai(string model, string sent)
    {
        var probe = await ProviderProbe.CapturePayloadWithLogAsync(nameof(ZaiLlmProvider), config => config with
        {
            Model = model,
            Thinking = new LlmThinkingConfig { Effort = "xhigh" },
        });

        Assert.Equal(sent, probe.Body.GetProperty("reasoning_effort").GetString());
        Assert.Equal(sent != "xhigh", probe.Warnings.Any(w => w.Contains("Effort 'xhigh'", StringComparison.Ordinal)));
    }

    /// <summary>DeepSeek's field knows every level since campaign M18 (#356): both travel untouched.</summary>
    [Theory]
    [InlineData("max", "max")]
    [InlineData("xhigh", "xhigh")]
    public async Task ShouldSendTheNearestKnownLevel_OnDeepSeek(string effort, string sent)
    {
        var probe = await ProviderProbe.CapturePayloadWithLogAsync(nameof(DeepSeekLlmProvider), config => config with
        {
            Thinking = new LlmThinkingConfig { Effort = effort },
        });

        Assert.Equal(sent, probe.Body.GetProperty("reasoning_effort").GetString());
        Assert.Equal(sent != effort, probe.Warnings.Any(w => w.Contains($"Effort '{effort}'", StringComparison.Ordinal)));
    }

    /// <summary>Ollama's <c>think</c> field knows the same three levels: the same lowering, said aloud.</summary>
    [Fact]
    public async Task ShouldLowerTheEffortToHighAndReportIt_OnOllama()
    {
        var probe = await ProviderProbe.CapturePayloadWithLogAsync(nameof(OllamaLlmProvider), config => config with
        {
            Thinking = new LlmThinkingConfig { Effort = "max" },
        });

        Assert.Equal("high", probe.Body.GetProperty("think").GetString());
        Assert.Contains(probe.Warnings, w => w.Contains("Effort 'max'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ShouldSendThinkField_OnOllama()
    {
        var enabled = await ProviderProbe.CapturePayloadAsync(nameof(OllamaLlmProvider), config => config with
        {
            Thinking = new LlmThinkingConfig { Enabled = true },
        });
        Assert.True(enabled.GetProperty("think").GetBoolean());

        var effort = await ProviderProbe.CapturePayloadAsync(nameof(OllamaLlmProvider), config => config with
        {
            Thinking = new LlmThinkingConfig { Effort = "medium" },
        });
        Assert.Equal("medium", effort.GetProperty("think").GetString());
    }

    [Fact]
    public async Task ShouldSendDashScopeThinkingFields_OnQwen()
    {
        var body = await ProviderProbe.CapturePayloadAsync(nameof(QwenLlmProvider), config => config with
        {
            Thinking = new LlmThinkingConfig { Enabled = true, BudgetTokens = 2048 },
        });

        Assert.True(body.GetProperty("enable_thinking").GetBoolean());
        Assert.Equal(2048, body.GetProperty("thinking_budget").GetInt32());
        // The OpenAI-shaped block must not leak: DashScope does not read it.
        Assert.False(body.TryGetProperty("thinking", out _));
    }

    /// <summary>
    /// Qwen is the only API that takes a reasoning budget. Everywhere else the option is
    /// reported — the exact failure mode the audit found for every option before LLM-02.
    /// </summary>
    [Theory]
    [InlineData(nameof(OpenAILlmProvider))]
    [InlineData(nameof(DeepSeekLlmProvider))]
    [InlineData(nameof(AnthropicLlmProvider))]
    [InlineData(nameof(OllamaLlmProvider))]
    public async Task ShouldReportTheBudget_WhereTheApiTakesNone(string providerTypeName)
    {
        var probe = await ProviderProbe.CapturePayloadWithLogAsync(providerTypeName, config => config with
        {
            Thinking = new LlmThinkingConfig { BudgetTokens = 4096 },
        });

        Assert.False(probe.Body.TryGetProperty("thinking_budget", out _));
        Assert.Contains(probe.Warnings, w => w.Contains("budgetTokens", StringComparison.OrdinalIgnoreCase));
    }

    // ── A gateway's own entry, read before the vendor's (#356) ──────────────

    /// <summary>Mammouth takes minimal to max on Gemini 3.7, where Google stops at high: xhigh travels untouched.</summary>
    [Fact]
    public async Task ShouldSendTheGatewaysOwnLevel_OnMammouthGemini()
    {
        var probe = await ProviderProbe.CapturePayloadWithLogAsync(nameof(MammouthLlmProvider), config => config with
        {
            Model = "gemini-3.7",
            Thinking = new LlmThinkingConfig { Effort = "xhigh" },
        });

        Assert.Equal("xhigh", probe.Body.GetProperty("reasoning_effort").GetString());
        Assert.Empty(probe.Warnings);
    }

    /// <summary>Mammouth refuses Google's <c>none</c> on Gemini 3.7: dropped, and the values it takes are named.</summary>
    [Fact]
    public async Task ShouldDropTheVendorsNone_OnMammouthGemini()
    {
        var probe = await ProviderProbe.CapturePayloadWithLogAsync(nameof(MammouthLlmProvider), config => config with
        {
            Model = "gemini-3.7",
            Thinking = new LlmThinkingConfig { Effort = "none" },
        });

        Assert.False(probe.Body.TryGetProperty("reasoning_effort", out _));
        var warning = Assert.Single(probe.Warnings);
        Assert.Contains("thinking.effort", warning, StringComparison.Ordinal);
        Assert.Contains("minimal, low, medium, high, xhigh, max only", warning, StringComparison.Ordinal);
    }

    /// <summary>OpenRouter converts a budget on Gemini 3.7, which cannot switch thinking off: the budget is kept.</summary>
    [Fact]
    public async Task ShouldKeepTheBudget_OnOpenRouterGemini()
    {
        var probe = await ProviderProbe.CapturePayloadWithLogAsync(nameof(OpenRouterLlmProvider), config => config with
        {
            Model = "google/gemini-3.7",
            Thinking = new LlmThinkingConfig { Effort = "max", BudgetTokens = 2048 },
        });

        var reasoning = probe.Body.GetProperty("reasoning");
        Assert.Equal(2048, reasoning.GetProperty("max_tokens").GetInt32());
        // The vendor's row would lower `max` to `high`; OpenRouter's own row takes it.
        Assert.Equal("max", reasoning.GetProperty("effort").GetString());
        Assert.False(reasoning.TryGetProperty("enabled", out _));
        Assert.Empty(probe.Warnings);
    }

    /// <summary>The same model behind a provider that takes no budget: the budget is still refused, said aloud.</summary>
    [Fact]
    public async Task ShouldReportTheBudget_OnMammouthGemini()
    {
        var probe = await ProviderProbe.CapturePayloadWithLogAsync(nameof(MammouthLlmProvider), config => config with
        {
            Model = "gemini-3.7",
            Thinking = new LlmThinkingConfig { Effort = "high", BudgetTokens = 2048 },
        });

        Assert.Equal("high", probe.Body.GetProperty("reasoning_effort").GetString());
        Assert.False(probe.Body.TryGetProperty("reasoning", out _));
        Assert.False(probe.Body.TryGetProperty("thinking_budget", out _));
        Assert.Contains(probe.Warnings, w => w.Contains("thinking.budgetTokens", StringComparison.Ordinal));
    }

    /// <summary>Together serves GLM-5.3 with every level and the off switch, which Z.AI refuses.</summary>
    [Fact]
    public async Task ShouldKeepTheEffortAndTheOffSwitch_OnTogetherGlm()
    {
        var probe = await ProviderProbe.CapturePayloadWithLogAsync(nameof(TogetherAiLlmProvider), config => config with
        {
            Model = "zai-org/GLM-5.3-Flash",
            Thinking = new LlmThinkingConfig { Enabled = false, Effort = "medium" },
        });

        Assert.Equal("medium", probe.Body.GetProperty("reasoning_effort").GetString());
        Assert.False(probe.Body.GetProperty("reasoning").GetProperty("enabled").GetBoolean());
        Assert.Empty(probe.Warnings);
    }

    /// <summary>Qwen writes the effort as <c>reasoning_effort</c>; Qwen 3.7 Plus refuses <c>max</c>, lowered to <c>xhigh</c>.</summary>
    [Theory]
    [InlineData("high", "high")]
    [InlineData("max", "xhigh")]
    public async Task ShouldSendTheEffortTheModelTakes_OnQwen(string effort, string sent)
    {
        var probe = await ProviderProbe.CapturePayloadWithLogAsync(nameof(QwenLlmProvider), config => config with
        {
            Model = "qwen3.7-plus",
            Thinking = new LlmThinkingConfig { Effort = effort },
        });

        Assert.Equal(sent, probe.Body.GetProperty("reasoning_effort").GetString());
        Assert.False(probe.Body.TryGetProperty("thinking", out _));
        if (sent == effort)
        {
            Assert.Empty(probe.Warnings);
        }
        else
        {
            var warning = Assert.Single(probe.Warnings);
            Assert.Contains($"Effort '{effort}'", warning, StringComparison.Ordinal);
            Assert.Contains($"'{sent}' was sent in its place", warning, StringComparison.Ordinal);
        }
    }

    // ── GBNF grammar: only where the configuration says the endpoint takes it (GAP-14) ──

    private const string SampleGrammar = "root ::= \"ok\"";

    /// <summary>
    /// A <c>structured_output</c> deliverable used to put a <c>grammar</c> field on every
    /// OpenAI-compatible request, a field none of these APIs documents, without a word. The
    /// field is now dropped unless <c>Llm:Grammar</c> says the endpoint takes it, and the drop
    /// names the option and the key to set.
    /// </summary>
    [Theory]
    [InlineData(nameof(OpenAILlmProvider))]
    [InlineData(nameof(MistralLlmProvider))]
    [InlineData(nameof(DeepSeekLlmProvider))]
    [InlineData(nameof(AnthropicLlmProvider))]
    [InlineData(nameof(OllamaLlmProvider))]
    public async Task ShouldDropTheGrammarAndSaySo_WhenTheConfigurationDoesNotEnableIt(string providerTypeName)
    {
        var probe = await ProviderProbe.CapturePayloadWithLogAsync(providerTypeName, config => config with
        {
            GrammarGbnf = SampleGrammar,
        });

        Assert.False(probe.Body.TryGetProperty("grammar", out _), "no `grammar` field may reach this API");
        Assert.Contains(probe.Warnings, w =>
            w.Contains("grammar", StringComparison.Ordinal)
            && w.Contains("Llm:Grammar", StringComparison.Ordinal));
    }

    /// <summary>
    /// Docker Model Runner and <c>llama-server</c> sit behind the OpenAI provider with another
    /// <c>BaseUrl</c>: the one real use of the field. <c>Llm:Grammar: true</c> keeps it working.
    /// </summary>
    [Theory]
    [InlineData(nameof(OpenAILlmProvider))]
    [InlineData(nameof(MistralLlmProvider))]
    [InlineData(nameof(OllamaLlmProvider))]
    public async Task ShouldSendTheGrammar_WhenTheConfigurationEnablesIt(string providerTypeName)
    {
        var probe = await ProviderProbe.CapturePayloadWithLogAsync(
            providerTypeName,
            config => config with { GrammarGbnf = SampleGrammar },
            construct: config => config with { GrammarEnabled = true });

        Assert.Equal(SampleGrammar, probe.Body.GetProperty("grammar").GetString());
        Assert.DoesNotContain(probe.Warnings, w => w.Contains("grammar", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(nameof(OpenAILlmProvider), true)]
    [InlineData(nameof(OllamaLlmProvider), true)]
    [InlineData(nameof(AnthropicLlmProvider), false)]
    public void ShouldDeclareTheGrammarCapability_FromTheConfiguration(string providerTypeName, bool expected)
    {
        var provider = ProviderProbe.Create(providerTypeName, construct: config => config with { GrammarEnabled = true });
        try
        {
            // Anthropic's Messages API has nowhere to put the field, whatever the key says.
            Assert.Equal(expected, provider.Capabilities.GbnfGrammar);
        }
        finally
        {
            (provider as IDisposable)?.Dispose();
        }
    }

    /// <summary>Test harness: builds a provider, captures the request body and its warnings.</summary>
    private static class ProviderProbe
    {
        internal sealed record Capture(JsonElement Body, IReadOnlyList<string> Warnings);

        private static readonly string OkBody = JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content = "ok" } } },
            content = new[] { new { type = "text", text = "ok" } },     // Anthropic shape
            response = "ok",                                            // Ollama shape
            usage = new { total_tokens = 1, prompt_tokens = 1, completion_tokens = 0, input_tokens = 1, output_tokens = 0 },
        });

        internal static ILlmProvider Create(
            string providerTypeName, IHttpClientFactory? factory = null, WarningSink? sink = null,
            Func<LlmConfig, LlmConfig>? construct = null)
        {
            var type = typeof(OpenAILlmProvider).Assembly.GetType(
                $"Orkeon.Infrastructure.LLMs.{providerTypeName}", throwOnError: true)!;

            // Prefer the tool-calling overload, but fall back to the resilience-policy one:
            // Ollama deliberately ships without a native tool-calling strategy (R10.7).
            var candidates = Array.FindAll(
                type.GetConstructors(),
                c => c.GetParameters() is [var cfg, var http, var third, var fourth]
                     && cfg.ParameterType == typeof(LlmConfig)
                     && http.ParameterType == typeof(IHttpClientFactory)
                     && (third.ParameterType == typeof(IToolCallingStrategy)
                         || third.ParameterType == typeof(IAsyncPolicy<HttpResponseMessage>))
                     && typeof(ILogger).IsAssignableFrom(fourth.ParameterType));

            var ctor = Array.Find(candidates, c => c.GetParameters()[2].ParameterType == typeof(IToolCallingStrategy))
                       ?? candidates.FirstOrDefault();
            Assert.NotNull(ctor);

            var parameters = ctor.GetParameters();
            var args = new object?[parameters.Length];
            args[0] = construct is null ? BaseConfig() : construct(BaseConfig());
            args[1] = factory ?? new TestHttpClientFactory();
            // The logger parameter is ILogger<TProvider>, so the sink is wrapped in a
            // closed generic built from the provider type itself.
            if (sink is not null && parameters.Length > 3)
                args[3] = Activator.CreateInstance(typeof(CollectingLogger<>).MakeGenericType(type), sink);

            return (ILlmProvider)ctor.Invoke(args);
        }

        internal static async Task<JsonElement> CapturePayloadAsync(
            string providerTypeName, Func<LlmConfig, LlmConfig> configure) =>
            (await CapturePayloadWithLogAsync(providerTypeName, configure)).Body;

        internal static async Task<Capture> CapturePayloadWithLogAsync(
            string providerTypeName, Func<LlmConfig, LlmConfig> configure,
            Func<LlmConfig, LlmConfig>? construct = null)
        {
            using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, OkBody);
            var factory = new TestHttpClientFactory();
            factory.RegisterClient(providerTypeName, new HttpClient(handler));

            var sink = new WarningSink();
            var provider = Create(providerTypeName, factory, sink, construct);
            try
            {
                await provider.GenerateAsync(
                    TestPrompt, configure(BaseConfig()), TestContext.Current.CancellationToken);
            }
            finally
            {
                (provider as IDisposable)?.Dispose();
            }

            var request = Assert.Single(handler.CapturedRequests);
            var raw = await request.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
            using var doc = JsonDocument.Parse(raw);
            return new Capture(doc.RootElement.Clone(), sink.Warnings);
        }

        /// <summary>
        /// A configuration valid for every provider at once: Azure needs an explicit resource
        /// URL, Ollama ignores the key, and the others need a key.
        /// </summary>
        private static LlmConfig BaseConfig() =>
            LlmConfig.Create("test-model", TestApiKey) with
            {
                BaseUrl = new Uri("https://provider.test/v1"),
            };
    }

    /// <summary>Collects the warnings emitted during one captured call.</summary>
    private sealed class WarningSink
    {
        public List<string> Warnings { get; } = [];
    }

    /// <summary>
    /// Captures warning-level messages so "was it reported?" can be asserted. Generic because
    /// providers take an <c>ILogger&lt;TProvider&gt;</c>; the sink is shared and untyped.
    /// </summary>
    private sealed class CollectingLogger<T>(WarningSink sink) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            if (logLevel >= LogLevel.Warning)
                sink.Warnings.Add(formatter(state, exception));
        }
    }
}
