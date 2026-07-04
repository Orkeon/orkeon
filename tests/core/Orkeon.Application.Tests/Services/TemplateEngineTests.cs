using Microsoft.Extensions.Logging;
using Orkeon.Application.Common;
using Orkeon.Infrastructure.Templating;

namespace Orkeon.Application.Tests.Services;

public class TemplateEngineTests
{
    #region Test Doubles

    private class TestLogger : ILogger<TemplateEngine>
    {
        public List<string> LoggedMessages { get; } = [];
        public List<Exception> LoggedExceptions { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            LoggedMessages.Add($"[{logLevel}] {message}");
            if (exception != null)
            {
                LoggedExceptions.Add(exception);
            }
        }

        public bool HasLoggedWarning(string partialMessage)
        {
            return LoggedMessages.Any(m => m.StartsWith("[Warning]") && m.Contains(partialMessage));
        }
    }

    #endregion

    #region Test Helpers

    private static TemplateEngine CreateTemplateEngine(TestLogger? logger = null)
    {
        return new TemplateEngine(logger ?? new TestLogger());
    }

    private static TemplateInstantiationParameters CreateTestParameters(Dictionary<string, object>? values = null)
    {
        if (values == null || values.Count == 0)
            return TemplateInstantiationParameters.Empty;

        return TemplateInstantiationParameters.FromDictionary(values);
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructingWithNullLogger()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => new TemplateEngine(null!));
        Assert.Equal("logger", exception.ParamName);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldInitializeWithBuiltInFunctions_WhenConstructingWithValidLogger()
    {
        // Arrange
        var logger = new TestLogger();

        // Act
        var engine = new TemplateEngine(logger);

        // Assert
        Assert.NotNull(engine);
        // Verify built-in functions work
        var template = "{{ upper('test') }}";
        var result = await engine.RenderAsync(template, TemplateInstantiationParameters.Empty, TestContext.Current.CancellationToken);
        Assert.Equal("TEST", result);
    }

    #endregion

    #region RenderAsync Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnEmptyString_WhenRenderingAsyncWithEmptyTemplate()
    {
        // Arrange
        var engine = CreateTemplateEngine();

        // Act
        var result = await engine.RenderAsync(string.Empty, TemplateInstantiationParameters.Empty, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnUnchangedTemplate_WhenRenderingAsyncWithNoParameters()
    {
        // Arrange
        var engine = CreateTemplateEngine();
        var template = "This is a plain template with no parameters";

        // Act
        var result = await engine.RenderAsync(template, TemplateInstantiationParameters.Empty, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(template, result);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReplaceCorrectly_WhenRenderingAsyncWithSimpleParameter()
    {
        // Arrange
        var engine = CreateTemplateEngine();
        var template = "Hello, {{ name }}!";
        var parameters = CreateTestParameters(new Dictionary<string, object> { ["name"] = "Alice" });

        // Act
        var result = await engine.RenderAsync(template, parameters, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Hello, Alice!", result);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReplaceAll_WhenRenderingAsyncWithMultipleParameters()
    {
        // Arrange
        var engine = CreateTemplateEngine();
        var template = "{{ greeting }}, {{ name }}! You are {{ age }} years old.";
        var parameters = CreateTestParameters(new Dictionary<string, object>
        {
            ["greeting"] = "Hello",
            ["name"] = "Bob",
            ["age"] = 25
        });

        // Act
        var result = await engine.RenderAsync(template, parameters, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Hello, Bob! You are 25 years old.", result);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldLeaveUnchanged_WhenRenderingAsyncWithMissingParameter()
    {
        // Arrange
        var logger = new TestLogger();
        var engine = CreateTemplateEngine(logger);
        var template = "Hello, {{ name }}!";
        var parameters = TemplateInstantiationParameters.Empty;

        // Act
        var result = await engine.RenderAsync(template, parameters, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Hello, {{ name }}!", result);
        Assert.True(logger.HasLoggedWarning("Failed to evaluate expression: name"));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldAccessCorrectly_WhenRenderingAsyncWithNestedProperty()
    {
        // Arrange
        var engine = CreateTemplateEngine();
        var template = "Agent {{ agent.name }} has role {{ agent.role }}";
        var parameters = CreateTestParameters(new Dictionary<string, object>
        {
            ["agent"] = new Dictionary<string, object>
            {
                ["name"] = "Assistant",
                ["role"] = "Helper"
            }
        });

        // Act
        var result = await engine.RenderAsync(template, parameters, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Agent Assistant has role Helper", result);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldApplyCorrectly_WhenRenderingAsyncWithUpperFilter()
    {
        // Arrange
        var engine = CreateTemplateEngine();
        var template = "{{ name | upper }}";
        var parameters = CreateTestParameters(new Dictionary<string, object> { ["name"] = "alice" });

        // Act
        var result = await engine.RenderAsync(template, parameters, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("ALICE", result);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldApplyCorrectly_WhenRenderingAsyncWithLowerFilter()
    {
        // Arrange
        var engine = CreateTemplateEngine();
        var template = "{{ name | lower }}";
        var parameters = CreateTestParameters(new Dictionary<string, object> { ["name"] = "BOB" });

        // Act
        var result = await engine.RenderAsync(template, parameters, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("bob", result);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldUseDefaultWhenEmpty_WhenRenderingAsyncWithDefaultFilter()
    {
        // Arrange
        var engine = CreateTemplateEngine();
        var template = "{{ missing | default('Unknown') }}";
        var parameters = TemplateInstantiationParameters.Empty;

        // Act
        var result = await engine.RenderAsync(template, parameters, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Unknown", result);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldTruncateLongText_WhenRenderingAsyncWithTruncateFilter()
    {
        // Arrange
        var engine = CreateTemplateEngine();
        var template = "{{ text | truncate(10) }}";
        var parameters = CreateTestParameters(new Dictionary<string, object>
        {
            ["text"] = "This is a very long text that should be truncated"
        });

        // Act
        var result = await engine.RenderAsync(template, parameters, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("This is a ...", result);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldJoinArray_WhenRenderingAsyncWithJoinFilter()
    {
        // Arrange
        var engine = CreateTemplateEngine();
        var template = "{{ items | join(', ') }}";
        var parameters = CreateTestParameters(new Dictionary<string, object>
        {
            ["items"] = new List<object> { "apple", "banana", "cherry" }
        });

        // Act
        var result = await engine.RenderAsync(template, parameters, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("apple, banana, cherry", result);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldExecuteFunction_WhenRenderingAsyncWithFunctionCall()
    {
        // Arrange
        var engine = CreateTemplateEngine();
        var template = "{{ upper('hello world') }}";
        var parameters = TemplateInstantiationParameters.Empty;

        // Act
        var result = await engine.RenderAsync(template, parameters, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("HELLO WORLD", result);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldCountItems_WhenRenderingAsyncWithCountFunction()
    {
        // Arrange
        var engine = CreateTemplateEngine();
        var template = "There are {{ count(items) }} items";
        var parameters = CreateTestParameters(new Dictionary<string, object>
        {
            ["items"] = new List<object> { "a", "b", "c", "d" }
        });

        // Act
        var result = await engine.RenderAsync(template, parameters, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("There are 4 items", result);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldRespectCancellation_WhenRenderingAsyncWithCancellation()
    {
        // Arrange
        var engine = CreateTemplateEngine();
        var template = "{{ name }}";
        var parameters = CreateTestParameters(new Dictionary<string, object> { ["name"] = "Test" });
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await engine.RenderAsync(template, parameters, cts.Token));
    }

    #endregion

    #region RenderWithInheritanceAsync Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldRenderNormally_WhenRenderingWithInheritanceAsyncWithNoBaseTemplate()
    {
        // Arrange
        var engine = CreateTemplateEngine();
        var template = "Hello, {{ name }}!";
        var parameters = CreateTestParameters(new Dictionary<string, object> { ["name"] = "Alice" });

        // Act
        var result = await engine.RenderWithInheritanceAsync(template, null, parameters, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Hello, Alice!", result);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldMergeTemplates_WhenRenderingWithInheritanceAsyncWithBaseTemplate()
    {
        // Arrange
        var engine = CreateTemplateEngine();
        var baseTemplate = "Base: {{ base }}";
        var template = "Child: {{ child }}";
        var parameters = CreateTestParameters(new Dictionary<string, object>
        {
            ["base"] = "BaseValue",
            ["child"] = "ChildValue"
        });

        // Act
        var result = await engine.RenderWithInheritanceAsync(template, baseTemplate, parameters, TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains("Base: BaseValue", result);
        Assert.Contains("Child: ChildValue", result);
    }

    #endregion

    #region ExtractParametersAsync Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnEmpty_WhenExtractingParametersAsyncWithNoParameters()
    {
        // Arrange
        var engine = CreateTemplateEngine();
        var template = "This is a plain template";

        // Act
        var parameters = await engine.ExtractParametersAsync(template, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(parameters);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldExtractOne_WhenExtractingParametersAsyncWithSingleParameter()
    {
        // Arrange
        var engine = CreateTemplateEngine();
        var template = "Hello, {{ name }}!";

        // Act
        var parameters = await engine.ExtractParametersAsync(template, TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(parameters);
        Assert.Contains("name", parameters);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldExtractAll_WhenExtractingParametersAsyncWithMultipleParameters()
    {
        // Arrange
        var engine = CreateTemplateEngine();
        var template = "{{ greeting }}, {{ name }}! You are {{ age }} years old.";

        // Act
        var parameters = await engine.ExtractParametersAsync(template, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, parameters.Count());
        Assert.Contains("greeting", parameters);
        Assert.Contains("name", parameters);
        Assert.Contains("age", parameters);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnUnique_WhenExtractingParametersAsyncWithDuplicateParameters()
    {
        // Arrange
        var engine = CreateTemplateEngine();
        var template = "{{ name }} and {{ name }} are the same {{ name }}";

        // Act
        var parameters = await engine.ExtractParametersAsync(template, TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(parameters);
        Assert.Contains("name", parameters);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldExtractBaseParameter_WhenExtractingParametersAsyncWithFilters()
    {
        // Arrange
        var engine = CreateTemplateEngine();
        var template = "{{ name | upper }} and {{ age | default(0) }}";

        // Act
        var parameters = await engine.ExtractParametersAsync(template, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, parameters.Count());
        Assert.Contains("name", parameters);
        Assert.Contains("age", parameters);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldExtractRootParameter_WhenExtractingParametersAsyncWithNestedProperties()
    {
        // Arrange
        var engine = CreateTemplateEngine();
        var template = "{{ agent.name }} has {{ agent.role }}";

        // Act
        var parameters = await engine.ExtractParametersAsync(template, TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(parameters);
        Assert.Contains("agent", parameters);
    }

    #endregion

    #region ValidateTemplateAsync Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnValid_WhenValidatingTemplateAsyncWithValidTemplate()
    {
        // Arrange
        var engine = CreateTemplateEngine();
        var template = "Hello, {{ name }}!";

        // Act
        var validation = await engine.ValidateTemplateAsync(template, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(validation.IsValid);
        Assert.Empty(validation.Errors);
        Assert.Single(validation.Parameters);
        Assert.Contains("name", validation.Parameters);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnInvalid_WhenValidatingTemplateAsyncWithUnbalancedBrackets()
    {
        // Arrange
        var engine = CreateTemplateEngine();
        var template = "Hello, {{ name }!"; // Missing closing bracket

        // Act
        var validation = await engine.ValidateTemplateAsync(template, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(validation.IsValid);
        Assert.Contains("Mismatched brackets", validation.Errors[0]);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnInvalid_WhenValidatingTemplateAsyncWithUnbalancedParentheses()
    {
        // Arrange
        var engine = CreateTemplateEngine();
        var template = "{{ upper(name }}"; // Missing closing parenthesis

        // Act
        var validation = await engine.ValidateTemplateAsync(template, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(validation.IsValid);
        Assert.Contains("Unbalanced parentheses", validation.Errors[0]);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnInvalid_WhenValidatingTemplateAsyncWithUnbalancedSquareBrackets()
    {
        // Arrange
        var engine = CreateTemplateEngine();
        var template = "{{ items[0 }}"; // Missing closing bracket

        // Act
        var validation = await engine.ValidateTemplateAsync(template, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(validation.IsValid);
        Assert.Contains("Unbalanced brackets", validation.Errors[0]);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnValid_WhenValidatingTemplateAsyncWithComplexValidTemplate()
    {
        // Arrange
        var engine = CreateTemplateEngine();
        var template = "{{ greeting | upper }}, {{ agent.name }}! Count: {{ count(items) }}";

        // Act
        var validation = await engine.ValidateTemplateAsync(template, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(validation.IsValid);
        Assert.Empty(validation.Errors);
        Assert.Equal(3, validation.Parameters.Count);
    }

    #endregion

    #region RegisterFunction Tests

    [Fact]
    public void ShouldThrowArgumentNullException_WhenRegisteringFunctionWithNullFunction()
    {
        // Arrange
        var engine = CreateTemplateEngine();

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            engine.RegisterFunction("test", null!));
        // The parameter is named `func` (CA1716 forbids `function` on interface members,
        // CA1725 requires the impl to match the interface), so ThrowIfNull reports "func".
        Assert.Equal("func", exception.ParamName);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldBeUsableInTemplate_WhenRegisteringFunction()
    {
        // Arrange
        var engine = CreateTemplateEngine();
        engine.RegisterFunction("double", args =>
        {
            if (args.Length > 0 && int.TryParse(args[0].ToString(), out var num))
                return num * 2;
            return 0;
        });
        var template = "{{ double(5) }}";

        // Act
        var result = await engine.RenderAsync(template, TemplateInstantiationParameters.Empty, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("10", result);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldOverrideExisting_WhenRegisteringFunction()
    {
        // Arrange
        var engine = CreateTemplateEngine();
        engine.RegisterFunction("test", args => "first");
        engine.RegisterFunction("test", args => "second");
        var template = "{{ test() }}";

        // Act
        var result = await engine.RenderAsync(template, TemplateInstantiationParameters.Empty, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("second", result);
    }

    #endregion

    #region RegisterFilter Tests

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingRegisterFilterWithNullFilter()
    {
        // Arrange
        var engine = CreateTemplateEngine();

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            engine.RegisterFilter("test", null!));
        Assert.Equal("filter", exception.ParamName);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldBeUsableInTemplate_WhenUsingRegisterFilter()
    {
        // Arrange
        var engine = CreateTemplateEngine();
        engine.RegisterFilter("reverse", (value, args) =>
        {
            var str = value?.ToString() ?? string.Empty;
            return new string(str.Reverse().ToArray());
        });
        var template = "{{ name | reverse }}";
        var parameters = CreateTestParameters(new Dictionary<string, object> { ["name"] = "hello" });

        // Act
        var result = await engine.RenderAsync(template, parameters, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("olleh", result);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldPassArgumentsCorrectly_WhenUsingRegisterFilterWithArguments()
    {
        // Arrange
        var engine = CreateTemplateEngine();
        engine.RegisterFilter("repeat", (value, args) =>
        {
            var str = value?.ToString() ?? string.Empty;
            if (args.Length > 0 && int.TryParse(args[0].ToString(), out var count))
                return string.Concat(Enumerable.Repeat(str, count));
            return str;
        });
        var template = "{{ text | repeat(3) }}";
        var parameters = CreateTestParameters(new Dictionary<string, object> { ["text"] = "Hi" });

        // Act
        var result = await engine.RenderAsync(template, parameters, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("HiHiHi", result);
    }

    #endregion

    #region Complex Scenario Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldApplyInOrder_WhenRenderingAsyncWithChainedFilters()
    {
        // Arrange
        var engine = CreateTemplateEngine();
        var template = "{{ name | lower | capitalize }}";
        var parameters = CreateTestParameters(new Dictionary<string, object> { ["name"] = "ALICE SMITH" });

        // Act
        var result = await engine.RenderAsync(template, parameters, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Alice smith", result); // First lower, then capitalize
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldAccessCorrectElement_WhenRenderingAsyncWithArrayAccess()
    {
        // Arrange
        var engine = CreateTemplateEngine();
        var template = "First: {{ items[0] }}, Second: {{ items[1] }}";
        var parameters = CreateTestParameters(new Dictionary<string, object>
        {
            ["items"] = new List<object> { "apple", "banana", "cherry" }
        });

        // Act
        var result = await engine.RenderAsync(template, parameters, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("First: apple, Second: banana", result);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldResolveCorrectly_WhenRenderingAsyncWithComplexNesting()
    {
        // Arrange
        var engine = CreateTemplateEngine();
        var template = "{{ agent.skills[0].name | upper }} at level {{ agent.skills[0].level }}";
        var parameters = CreateTestParameters(new Dictionary<string, object>
        {
            ["agent"] = new Dictionary<string, object>
            {
                ["skills"] = new List<object>
                {
                    new Dictionary<string, object>
                    {
                        ["name"] = "coding",
                        ["level"] = 9
                    }
                }
            }
        });

        // Act
        var result = await engine.RenderAsync(template, parameters, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("CODING at level 9", result);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldPreserveNonTemplateText_WhenRenderingAsyncWithMixedContent()
    {
        // Arrange
        var engine = CreateTemplateEngine();
        var template = @"Dear {{ name }},

Thank you for your {{ count(orders) }} orders.
Your status is: {{ status | upper }}.

Best regards,
{{ company }}";
        var parameters = CreateTestParameters(new Dictionary<string, object>
        {
            ["name"] = "John",
            ["orders"] = new List<object> { "order1", "order2", "order3" },
            ["status"] = "gold",
            ["company"] = "Acme Corp"
        });

        // Act
        var result = await engine.RenderAsync(template, parameters, TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains("Dear John", result);
        Assert.Contains("Thank you for your 3 orders", result);
        Assert.Contains("Your status is: GOLD", result);
        Assert.Contains("Best regards,", result);
        Assert.Contains("Acme Corp", result);
    }

    #endregion
}
