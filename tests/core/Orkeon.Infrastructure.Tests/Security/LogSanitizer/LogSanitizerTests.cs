using LogSanitizerSut = Orkeon.Infrastructure.Security.LogSanitizer;

namespace Orkeon.Infrastructure.Tests.Security;

public class LogSanitizerTests
{
    // ──────────────────────────────────────────────────────────────
    // Sanitize (existing mask-based method)
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void ShouldMaskOpenAIKey_WhenPresent()
    {
        var input = "Using key sk-abcdefghij1234567890abcdef for request";
        var result = LogSanitizerSut.Sanitize(input);

        Assert.NotNull(result);
        Assert.DoesNotContain("sk-abcdefghij1234567890abcdef", result);
        Assert.Contains("sk-...", result);
    }

    [Fact]
    public void ShouldMaskAnthropicKey_WhenPresent()
    {
        var input = "Key: sk-ant-abcdefghij1234567890abcdef";
        var result = LogSanitizerSut.Sanitize(input);

        Assert.NotNull(result);
        Assert.DoesNotContain("sk-ant-abcdefghij1234567890abcdef", result);
        Assert.Contains("sk-...", result);
    }

    [Fact]
    public void ShouldMaskBearerToken_WhenPresent()
    {
        var input = "Authorization: Bearer eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.abcdef";
        var result = LogSanitizerSut.Sanitize(input);

        Assert.NotNull(result);
        Assert.DoesNotContain("eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9", result);
        Assert.Contains("Bearer ", result);
        Assert.Contains("...", result);
    }

    [Fact]
    public void ShouldMaskApiKeyValue_WhenApiKeyPatternPresent()
    {
        var input = "api_key: abcdefgh1234567890xyz";
        var result = LogSanitizerSut.Sanitize(input);

        Assert.NotNull(result);
        Assert.DoesNotContain("abcdefgh1234567890xyz", result);
        Assert.Contains("...", result);
    }

    [Fact]
    public void ShouldRemainUnchanged_WhenTextContainsNoSecrets()
    {
        var input = "This is a normal log message with no secrets.";
        var result = LogSanitizerSut.Sanitize(input);

        Assert.Equal(input, result);
    }

    [Fact]
    public void ShouldReturnNull_WhenTextIsNull()
    {
        Assert.Null(LogSanitizerSut.Sanitize(null));
    }

    [Fact]
    public void ShouldReturnEmpty_WhenTextIsEmpty()
    {
        Assert.Equal(string.Empty, LogSanitizerSut.Sanitize(string.Empty));
    }

    [Fact]
    public void ShouldMaskPasswordValue_WhenPasswordPatternPresent()
    {
        var input = "password=SuperSecret12345678";
        var result = LogSanitizerSut.Sanitize(input);

        Assert.NotNull(result);
        Assert.DoesNotContain("SuperSecret12345678", result);
        Assert.Contains("...", result);
    }

    [Fact]
    public void ShouldMaskTokenValue_WhenTokenPatternPresent()
    {
        var input = "token: mytoken123456789abc";
        var result = LogSanitizerSut.Sanitize(input);

        Assert.NotNull(result);
        Assert.DoesNotContain("mytoken123456789abc", result);
        Assert.Contains("...", result);
    }

    [Fact]
    public void ShouldMaskAllSecrets_WhenMultipleSecretsPresent()
    {
        var input = "key1=sk-abcdefghij1234567890abcdef and token: mytoken123456789abc";
        var result = LogSanitizerSut.Sanitize(input);

        Assert.NotNull(result);
        Assert.DoesNotContain("sk-abcdefghij1234567890abcdef", result);
        Assert.DoesNotContain("mytoken123456789abc", result);
    }

    [Fact]
    public void ShouldMaskGitHubToken_WhenPresent()
    {
        var input = "Using ghp_abcdefghij1234567890abcdef for GitHub access";
        var result = LogSanitizerSut.Sanitize(input);

        Assert.NotNull(result);
        Assert.DoesNotContain("ghp_abcdefghij1234567890abcdef", result);
        Assert.Contains("ghp...", result);
    }

    // ──────────────────────────────────────────────────────────────
    // SanitizeString (***REDACTED*** replacement)
    // ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("api_key=sk-abc123xyz4567890abcdefghij", "sk-abc123xyz4567890abcdefghij")]
    [InlineData("Authorization: Bearer sk-proj-1234567890abcdefghij", "sk-proj-1234567890abcdefghij")]
    [InlineData("password=SuperSecret12345678", "SuperSecret12345678")]
    [InlineData("ghp_abcdefghij1234567890abcdef", "ghp_abcdefghij1234567890abcdef")]
    public void SanitizeString_ShouldRedactSensitiveData(string input, string secretValue)
    {
        var result = LogSanitizerSut.SanitizeString(input);

        Assert.Contains("***REDACTED***", result);
        Assert.DoesNotContain(secretValue, result);
    }

    [Fact]
    public void SanitizeString_ShouldReturnEmpty_WhenInputIsEmpty()
    {
        Assert.Equal(string.Empty, LogSanitizerSut.SanitizeString(string.Empty));
    }

    [Fact]
    public void SanitizeString_ShouldReturnNull_WhenInputIsNull()
    {
        Assert.Null(LogSanitizerSut.SanitizeString(null!));
    }

    [Fact]
    public void SanitizeString_ShouldNotAlterSafeText()
    {
        var input = "Normal log message with no secrets at all.";
        var result = LogSanitizerSut.SanitizeString(input);

        Assert.Equal(input, result);
    }

    [Fact]
    public void SanitizeString_ShouldRedactBearerToken()
    {
        var input = "Header: Bearer eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.abcdef";
        var result = LogSanitizerSut.SanitizeString(input);

        Assert.Contains("***REDACTED***", result);
        Assert.DoesNotContain("eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9", result);
    }

    [Fact]
    public void SanitizeString_ShouldRedactMultipleSecrets()
    {
        var input = "key=sk-abcdefghij1234567890abcdef and ghp_xyzxyzxyz1234567890abcdef";
        var result = LogSanitizerSut.SanitizeString(input);

        Assert.DoesNotContain("sk-abcdefghij1234567890abcdef", result);
        Assert.DoesNotContain("ghp_xyzxyzxyz1234567890abcdef", result);
    }

    // ──────────────────────────────────────────────────────────────
    // SanitizeException
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void SanitizeException_ShouldRedactApiKeyInExceptionMessage()
    {
        var ex = new HttpRequestException(
            "Failed to connect with ApiKey=sk-abc123xyz4567890abcdefghij");

        var result = LogSanitizerSut.SanitizeException(ex);

        Assert.DoesNotContain("sk-abc123xyz4567890abcdefghij", result);
        Assert.Contains("***REDACTED***", result);
        Assert.Contains("HttpRequestException", result);
    }

    [Fact]
    public void SanitizeException_ShouldReturnEmpty_WhenExceptionIsNull()
    {
        var result = LogSanitizerSut.SanitizeException(null!);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void SanitizeException_ShouldSanitizeInnerException()
    {
        var inner = new Exception("Inner has token: ghp_innertoken12345678901234");
        var outer = new Exception("Outer has key sk-outerkey12345678901234567", inner);

        var result = LogSanitizerSut.SanitizeException(outer);

        Assert.DoesNotContain("ghp_innertoken12345678901234", result);
        Assert.DoesNotContain("sk-outerkey12345678901234567", result);
        Assert.Contains("Inner Exception", result);
    }

    // ──────────────────────────────────────────────────────────────
    // CreateSanitizedException
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void CreateSanitizedException_ShouldHideApiKey()
    {
        var originalEx = new HttpRequestException(
            "Failed to call https://api.openai.com with ApiKey=sk-abc123xyz4567890abcdefghij");

        var sanitized = LogSanitizerSut.CreateSanitizedException(originalEx);

        Assert.DoesNotContain("sk-abc123xyz4567890abcdefghij", sanitized.Message);
        Assert.Contains("[SANITIZED]", sanitized.Message);
        Assert.Contains("***REDACTED***", sanitized.Message);
    }

    [Fact]
    public void CreateSanitizedException_ShouldReturnSanitizedExceptionType()
    {
        var originalEx = new InvalidOperationException("secret=MySuperSecret12345678");

        var sanitized = LogSanitizerSut.CreateSanitizedException(originalEx);

        // The wrapper is a dedicated SanitizedException with a sanitized message.
        Assert.IsType<Orkeon.Infrastructure.Security.SanitizedException>(sanitized);
        Assert.DoesNotContain("MySuperSecret12345678", sanitized.Message);
        Assert.Contains("[SANITIZED]", sanitized.Message);
    }

    [Fact]
    public void CreateSanitizedException_ShouldPreserveOriginalAsInner()
    {
        var originalEx = new InvalidOperationException("boom");

        var sanitized = LogSanitizerSut.CreateSanitizedException(originalEx);

        // The original exception (and thus its real type and stack trace) is preserved
        // as the inner exception for diagnostics, mirroring the OllamaLlmProvider policy.
        Assert.Same(originalEx, sanitized.InnerException);
        Assert.IsType<InvalidOperationException>(sanitized.InnerException);
    }

    [Fact]
    public void CreateSanitizedException_ShouldThrow_WhenExceptionIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => LogSanitizerSut.CreateSanitizedException(null!));
    }

    [Fact]
    public void CreateSanitizedException_SurfacedMessageShouldNotLeakApiKey()
    {
        var originalEx = new HttpRequestException(
            "Connection failed to api with Bearer sk-proj-1234567890abcdefghij");

        var sanitized = LogSanitizerSut.CreateSanitizedException(originalEx);

        // The surfaced (outer) message — the one logged/returned — must not leak the secret.
        Assert.DoesNotContain("sk-proj-1234567890abcdefghij", sanitized.Message);
    }

    // ──────────────────────────────────────────────────────────────
    // GitHub fine-grained tokens
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void SanitizeString_ShouldRedactGitHubFineGrainedToken()
    {
        var input = "token: github_pat_abcdefghij1234567890abcdef";
        var result = LogSanitizerSut.SanitizeString(input);

        Assert.DoesNotContain("github_pat_abcdefghij1234567890abcdef", result);
        Assert.Contains("***REDACTED***", result);
    }

    // ──────────────────────────────────────────────────────────────
    // Sensitive headers by name
    // ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Authorization")]
    [InlineData("api-key")]
    [InlineData("API-KEY")]
    [InlineData("x-api-key")]
    [InlineData("x-goog-api-key")]
    [InlineData("proxy-authorization")]
    [InlineData("x-amz-security-token")]
    [InlineData("x-amz-date")]
    [InlineData("x-subscription-token")]
    [InlineData("X-Subscription-Token")]
    [InlineData("ocp-apim-subscription-key")]
    public void IsSensitiveHeader_ShouldReturnTrue_ForKnownSensitiveNames(string headerName)
    {
        Assert.True(LogSanitizerSut.IsSensitiveHeader(headerName));
    }

    [Theory]
    [InlineData("Content-Type")]
    [InlineData("Accept")]
    [InlineData("User-Agent")]
    [InlineData("")]
    [InlineData(null)]
    public void IsSensitiveHeader_ShouldReturnFalse_ForNonSensitiveNames(string? headerName)
    {
        Assert.False(LogSanitizerSut.IsSensitiveHeader(headerName));
    }

    [Fact]
    public void SanitizeHeaderValue_ShouldRedactBareHexApiKey_WhenHeaderNameIsSensitive()
    {
        // Azure OpenAI api-key: 32-char hex with no sk-/Bearer prefix —
        // not matched by value patterns, must be redacted by header name.
        const string azureApiKey = "0123456789abcdef0123456789abcdef";
        var result = LogSanitizerSut.SanitizeHeaderValue("api-key", azureApiKey);

        Assert.DoesNotContain(azureApiKey, result);
        Assert.Equal("***REDACTED***", result);
    }

    [Fact]
    public void SanitizeHeaderValue_ShouldRedactAuthorizationBearer_ByName()
    {
        var result = LogSanitizerSut.SanitizeHeaderValue(
            "Authorization", "Bearer sk-proj-abc123def456ghi789jkl012mno345");

        Assert.DoesNotContain("sk-proj-abc123def456ghi789jkl012mno345", result);
        Assert.Contains("REDACTED", result);
    }

    [Fact]
    public void SanitizeHeaderValue_ShouldPatternSanitize_WhenHeaderNameIsNotSensitive()
    {
        // Non-sensitive header still passes through pattern sanitization for
        // embedded secrets.
        var result = LogSanitizerSut.SanitizeHeaderValue(
            "X-Custom", "key sk-abcdefghij1234567890abcdef here");

        Assert.DoesNotContain("sk-abcdefghij1234567890abcdef", result);
        Assert.Contains("***REDACTED***", result);
    }

    [Fact]
    public void SanitizeHeaderValue_ShouldLeaveBenignValueUntouched_ForNonSensitiveHeader()
    {
        var result = LogSanitizerSut.SanitizeHeaderValue("Content-Type", "application/json");

        Assert.Equal("application/json", result);
    }

    [Fact]
    public void ShouldRedactXSubscriptionTokenHeader_ByName()
    {
        // Brave Search authenticates with X-Subscription-Token, and the value it carries is
        // an opaque vendor string: no value pattern can recognise it, so the header name is
        // the only thing that can keep it out of an exchange log.
        const string braveHeaderValue = "BSAfake-fake-fake-fake-0000";
        var result = LogSanitizerSut.SanitizeHeaderValue("X-Subscription-Token", braveHeaderValue);

        Assert.DoesNotContain(braveHeaderValue, result);
        Assert.Equal("***REDACTED***", result);
    }

    [Fact]
    public void ShouldRedactOcpApimSubscriptionKeyHeader_ByName()
    {
        // Azure API Management fronts OpenAI-compatible endpoints and takes its credential
        // in Ocp-Apim-Subscription-Key, again as a bare opaque value.
        const string apimHeaderValue = "0000fake0000apim0000fake000";
        var result = LogSanitizerSut.SanitizeHeaderValue("Ocp-Apim-Subscription-Key", apimHeaderValue);

        Assert.DoesNotContain(apimHeaderValue, result);
        Assert.Equal("***REDACTED***", result);
    }

    // ────────────────────────────────────────────────────────────
    // Key shapes the shipped providers actually issue
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void ShouldRedactWholeKey_WhenKeyContainsUnderscore()
    {
        // OpenAI project and service-account keys, and Anthropic sk-ant-api03- keys, carry
        // a URL-safe base64 body: the underscore is part of the key, not a delimiter.
        var key = CredentialShaped("sk-proj-", "fake_underscore_fixture_0000");
        var result = LogSanitizerSut.SanitizeString($"Using key {key} for the request");

        Assert.DoesNotContain(key, result);
        Assert.DoesNotContain("underscore_fixture_0000", result);
        Assert.Contains("***REDACTED***", result);
    }

    [Fact]
    public void ShouldRedactWholeKey_WhenDashScopeKeyContainsDot()
    {
        // Alibaba DashScope (Qwen) issues workspace-scoped keys whose body is split by a
        // dot, so a key regex made of one unbroken run of characters stops on the dot.
        var key = CredentialShaped("sk-ws-", "H.fake_dashscope_fixture_00");
        var result = LogSanitizerSut.SanitizeString($"Using key {key} for the request");

        Assert.DoesNotContain(key, result);
        Assert.DoesNotContain("fake_dashscope_fixture_00", result);
        Assert.Contains("***REDACTED***", result);
    }

    [Fact]
    public void ShouldRedactWholeKey_WhenMiniMaxKeyContainsUnderscore()
    {
        var key = CredentialShaped("sk-api-", "fake_minimax_fixture_00000");
        var result = LogSanitizerSut.SanitizeString($"key={key}");

        Assert.DoesNotContain(key, result);
        Assert.DoesNotContain("minimax_fixture_00000", result);
        Assert.Contains("***REDACTED***", result);
    }

    /// <summary>
    /// One case per vendor prefix Orkeon ships a client for. Header-name redaction covers
    /// these keys only while they travel in a header; in a request body, an error message
    /// or a stack trace, nothing else recognises them.
    /// </summary>
    [Theory]
    [InlineData("xai-", "fake_grok_fixture_0000000")]
    [InlineData("hf_", "fake_huggingface_fixture0")]
    [InlineData("tgp_v1_", "fake_together_fixture_000")]
    [InlineData("tvly-", "fake_tavily_fixture_00000")]
    [InlineData("xoxb-", "fake-slack-bot-fixture-00")]
    [InlineData("xoxp-", "fake-slack-user-fixture-0")]
    [InlineData("AIza", "fake_gemini_fixture_00000")]
    public void ShouldRedactVendorPrefixedKey_ForShippedProviderFamilies(string prefix, string body)
    {
        var key = CredentialShaped(prefix, body);
        var result = LogSanitizerSut.SanitizeString($"provider call failed with {key} attached");

        Assert.DoesNotContain(key, result);
        Assert.DoesNotContain(body, result);
        Assert.Contains("***REDACTED***", result);
    }

    [Fact]
    public void ShouldLeaveOrdinaryWordsUntouched_WhenTheyOnlyResembleAVendorPrefix()
    {
        // The vendor patterns must not eat identifiers that merely contain the same
        // letters: a prefix counts only at a token boundary, followed by a key-length body.
        var nearMiss = CredentialShaped("shf_", "abcdefghijklmnopqrst");
        var input = $"half_a_dozen_shelf_entries and {nearMiss} stay readable";
        var result = LogSanitizerSut.SanitizeString(input);

        Assert.Equal(input, result);
    }

    /// <summary>
    /// Assembles a credential-shaped fixture from its prefix and its body at run time. The
    /// committed source therefore never holds a literal shaped like a live key, so the
    /// secret scanners keep watching these files instead of learning to ignore a value.
    /// </summary>
    private static string CredentialShaped(string prefix, string body) => prefix + body;
}
