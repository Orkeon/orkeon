using Orkeon.Domain.Common;
using Orkeon.Domain.HumanInput;
using Orkeon.Domain.HumanInput.ValueObjects;
using Orkeon.Infrastructure.HumanInput;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.Services;

public sealed class ConsoleHumanInputProviderTests : IDisposable
{
    private readonly FakeTextReader _input = new();
    private readonly StringWriter _output = new();

    public void Dispose()
    {
        _input.Dispose();
        _output.Dispose();
    }

    [Fact]
    public async Task ShouldReturnInput_WhenGetInputAsyncReadsFromConsole()
    {
        var logger = new TestLogger<ConsoleHumanInputProvider>();
        var provider = new ConsoleHumanInputProvider(logger, _input, _output);
        var context = new HumanInputContext
        {
            AgentId = AgentId.Create(),
            TaskId = TaskId.Create(),
            Prompt = "Enter value",
            DefaultValue = "default"
        };

        var sr = new StringReader("hello input\n");
        _input.Inner = sr;

        var result = await provider.GetInputAsync(context, TestContext.Current.CancellationToken);
        Assert.Equal("hello input", result);
    }

    [Theory]
    [InlineData("y", true)]
    [InlineData("yes", true)]
    [InlineData("n", false)]
    [InlineData("no", false)]
    [InlineData("", false)]
    public async Task ShouldParseYesNo_WhenGetConfirmationAsync(string input, bool expected)
    {
        var logger = new TestLogger<ConsoleHumanInputProvider>();
        var provider = new ConsoleHumanInputProvider(logger, _input, _output);
        var context = new HumanInputContext
        {
            AgentId = AgentId.Create(),
            TaskId = TaskId.Create(),
            Prompt = "Confirm?"
        };

        var sr = new StringReader(input + "\n");
        _input.Inner = sr;

        var result = await provider.GetConfirmationAsync(context, TestContext.Current.CancellationToken);
        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task ShouldReturnCorrectOption_WhenGetChoiceAsyncByIndex()
    {
        var logger = new TestLogger<ConsoleHumanInputProvider>();
        var provider = new ConsoleHumanInputProvider(logger, _input, _output);
        var options = new[] { "A", "B", "C" };
        var context = new HumanInputContext
        {
            AgentId = AgentId.Create(),
            TaskId = TaskId.Create(),
            Prompt = "Choose",
            Options = options.ToList()
        };

        var sr = new StringReader("2\n");
        _input.Inner = sr;

        var result = await provider.GetChoiceAsync(context, TestContext.Current.CancellationToken);
        Assert.Equal("B", result);
    }

    [Fact]
    public async Task ShouldReturnInput_WhenGetChoiceAsyncByText()
    {
        var logger = new TestLogger<ConsoleHumanInputProvider>();
        var provider = new ConsoleHumanInputProvider(logger, _input, _output);
        var options = new[] { "A", "B", "C" };
        var context = new HumanInputContext
        {
            AgentId = AgentId.Create(),
            TaskId = TaskId.Create(),
            Prompt = "Choose",
            Options = options.ToList()
        };

        var sr = new StringReader("C\n");
        _input.Inner = sr;

        var result = await provider.GetChoiceAsync(context, TestContext.Current.CancellationToken);
        Assert.Equal("C", result);
    }

    [Fact]
    public async Task ShouldAlwaysTrue_WhenIsAvailableAsync()
    {
        var logger = new TestLogger<ConsoleHumanInputProvider>();
        var provider = new ConsoleHumanInputProvider(logger, _input, _output);
        var available = await provider.IsAvailableAsync(TestContext.Current.CancellationToken);
        Assert.True(available);
    }

    [Fact]
    public async Task ShouldReturnDefaultValue_WhenGetInputAsyncWithEmptyInput()
    {
        // Arrange
        var logger = new TestLogger<ConsoleHumanInputProvider>();
        var provider = new ConsoleHumanInputProvider(logger, _input, _output);
        var context = new HumanInputContext
        {
            AgentId = AgentId.Create(),
            TaskId = TaskId.Create(),
            Prompt = "Enter value",
            DefaultValue = "default-value"
        };

        var sr = new StringReader("\n");
        _input.Inner = sr;

        // Act
        var result = await provider.GetInputAsync(context, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("default-value", result);
    }

    [Fact]
    public async Task ShouldReturnDefaultValue_WhenGetInputAsyncWithWhitespaceInput()
    {
        // Arrange
        var logger = new TestLogger<ConsoleHumanInputProvider>();
        var provider = new ConsoleHumanInputProvider(logger, _input, _output);
        var context = new HumanInputContext
        {
            AgentId = AgentId.Create(),
            TaskId = TaskId.Create(),
            Prompt = "Enter value",
            DefaultValue = "fallback"
        };

        var sr = new StringReader("   \n");
        _input.Inner = sr;

        // Act
        var result = await provider.GetInputAsync(context, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("fallback", result);
    }

    [Fact]
    public async Task ShouldReturnFirstLine_WhenGetInputAsyncWithMultilineInput()
    {
        // Arrange
        var logger = new TestLogger<ConsoleHumanInputProvider>();
        var provider = new ConsoleHumanInputProvider(logger, _input, _output);
        var context = new HumanInputContext
        {
            AgentId = AgentId.Create(),
            TaskId = TaskId.Create(),
            Prompt = "Enter value"
        };

        var sr = new StringReader("first line\nsecond line\n");
        _input.Inner = sr;

        // Act
        var result = await provider.GetInputAsync(context, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("first line", result);
    }

    [Theory]
    [InlineData("Y", true)]
    [InlineData("YES", true)]
    [InlineData("Yes", true)]
    [InlineData("yEs", true)]
    [InlineData("N", false)]
    [InlineData("NO", false)]
    [InlineData("No", false)]
    [InlineData("nO", false)]
    [InlineData("maybe", false)]
    [InlineData("123", false)]
    public async Task ShouldParseCorrectly_WhenGetConfirmationAsyncCaseInsensitive(string input, bool expected)
    {
        // Arrange
        var logger = new TestLogger<ConsoleHumanInputProvider>();
        var provider = new ConsoleHumanInputProvider(logger, _input, _output);
        var context = new HumanInputContext
        {
            AgentId = AgentId.Create(),
            TaskId = TaskId.Create(),
            Prompt = "Confirm?"
        };

        var sr = new StringReader(input + "\n");
        _input.Inner = sr;

        // Act
        var result = await provider.GetConfirmationAsync(context, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task ShouldReturnInvalidChoice_WhenGetChoiceAsyncWithInvalidIndex()
    {
        // Arrange
        var logger = new TestLogger<ConsoleHumanInputProvider>();
        var provider = new ConsoleHumanInputProvider(logger, _input, _output);
        var options = new[] { "Option1", "Option2", "Option3" };
        var context = new HumanInputContext
        {
            AgentId = AgentId.Create(),
            TaskId = TaskId.Create(),
            Prompt = "Choose an option",
            Options = options.ToList()
        };

        var sr = new StringReader("99\n");
        _input.Inner = sr;

        // Act
        var result = await provider.GetChoiceAsync(context, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("99", result); // Returns the input as-is when invalid
    }

    [Fact]
    public async Task ShouldReturnInvalidChoice_WhenGetChoiceAsyncWithZeroIndex()
    {
        // Arrange
        var logger = new TestLogger<ConsoleHumanInputProvider>();
        var provider = new ConsoleHumanInputProvider(logger, _input, _output);
        var options = new[] { "A", "B", "C" };
        var context = new HumanInputContext
        {
            AgentId = AgentId.Create(),
            TaskId = TaskId.Create(),
            Prompt = "Choose",
            Options = options.ToList()
        };

        var sr = new StringReader("0\n");
        _input.Inner = sr;

        // Act
        var result = await provider.GetChoiceAsync(context, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("0", result); // 0 is invalid (1-based indexing)
    }

    [Fact]
    public async Task ShouldReturnInput_WhenGetChoiceAsyncWithNegativeIndex()
    {
        // Arrange
        var logger = new TestLogger<ConsoleHumanInputProvider>();
        var provider = new ConsoleHumanInputProvider(logger, _input, _output);
        var options = new[] { "Red", "Green", "Blue" };
        var context = new HumanInputContext
        {
            AgentId = AgentId.Create(),
            TaskId = TaskId.Create(),
            Prompt = "Pick a color",
            Options = options.ToList()
        };

        var sr = new StringReader("-1\n");
        _input.Inner = sr;

        // Act
        var result = await provider.GetChoiceAsync(context, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("-1", result);
    }

    [Fact]
    public async Task ShouldReturnInput_WhenGetChoiceAsyncWithPartialMatch()
    {
        // Arrange
        var logger = new TestLogger<ConsoleHumanInputProvider>();
        var provider = new ConsoleHumanInputProvider(logger, _input, _output);
        var options = new[] { "Apple", "Apricot", "Banana" };
        var context = new HumanInputContext
        {
            AgentId = AgentId.Create(),
            TaskId = TaskId.Create(),
            Prompt = "Choose fruit",
            Options = options.ToList()
        };

        var sr = new StringReader("Ap\n");
        _input.Inner = sr;

        // Act
        var result = await provider.GetChoiceAsync(context, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Ap", result); // Partial match is not resolved
    }

    [Fact]
    public async Task ShouldReturnInput_WhenGetChoiceAsyncWithEmptyOptions()
    {
        // Arrange
        var logger = new TestLogger<ConsoleHumanInputProvider>();
        var provider = new ConsoleHumanInputProvider(logger, _input, _output);
        var context = new HumanInputContext
        {
            AgentId = AgentId.Create(),
            TaskId = TaskId.Create(),
            Prompt = "Choose",
            Options = []
        };

        var sr = new StringReader("anything\n");
        _input.Inner = sr;

        // Act
        var result = await provider.GetChoiceAsync(context, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("anything", result);
    }

    [Fact]
    public async Task ShouldReturnCorrectly_WhenGetInputAsyncWithSpecialCharacters()
    {
        // Arrange
        var logger = new TestLogger<ConsoleHumanInputProvider>();
        var provider = new ConsoleHumanInputProvider(logger, _input, _output);
        var context = new HumanInputContext
        {
            AgentId = AgentId.Create(),
            TaskId = TaskId.Create(),
            Prompt = "Enter special text"
        };

        var specialInput = "Hello @#$%^&*() World!";
        var sr = new StringReader(specialInput + "\n");
        _input.Inner = sr;

        // Act
        var result = await provider.GetInputAsync(context, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(specialInput, result);
    }

    [Fact]
    public async Task ShouldReturnCorrectly_WhenGetInputAsyncWithUnicodeCharacters()
    {
        // Arrange
        var logger = new TestLogger<ConsoleHumanInputProvider>();
        var provider = new ConsoleHumanInputProvider(logger, _input, _output);
        var context = new HumanInputContext
        {
            AgentId = AgentId.Create(),
            TaskId = TaskId.Create(),
            Prompt = "Enter unicode text"
        };

        var unicodeInput = "Hello 世界 ☺ ❤";
        var sr = new StringReader(unicodeInput + "\n");
        _input.Inner = sr;

        // Act
        var result = await provider.GetInputAsync(context, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(unicodeInput, result);
    }

    [Fact]
    public async Task ShouldReturnFullInput_WhenGetInputAsyncWithLongInput()
    {
        // Arrange
        var logger = new TestLogger<ConsoleHumanInputProvider>();
        var provider = new ConsoleHumanInputProvider(logger, _input, _output);
        var context = new HumanInputContext
        {
            AgentId = AgentId.Create(),
            TaskId = TaskId.Create(),
            Prompt = "Enter long text"
        };

        var longInput = string.Join(" ", Enumerable.Repeat("word", 1000));
        var sr = new StringReader(longInput + "\n");
        _input.Inner = sr;

        // Act
        var result = await provider.GetInputAsync(context, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(longInput, result);
        Assert.True(result.Length > 3000);
    }

    [Fact]
    public async Task ShouldReturnFalse_WhenGetConfirmationAsyncWithEmptyInput()
    {
        // Arrange
        var logger = new TestLogger<ConsoleHumanInputProvider>();
        var provider = new ConsoleHumanInputProvider(logger, _input, _output);
        var context = new HumanInputContext
        {
            AgentId = AgentId.Create(),
            TaskId = TaskId.Create(),
            Prompt = "Confirm?"
        };

        var sr = new StringReader("\n");
        _input.Inner = sr;

        // Act
        var result = await provider.GetConfirmationAsync(context, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task ShouldReturnOption_WhenGetChoiceAsyncWithExactMatchCase()
    {
        // Arrange
        var logger = new TestLogger<ConsoleHumanInputProvider>();
        var provider = new ConsoleHumanInputProvider(logger, _input, _output);
        var options = new[] { "Production", "Staging", "Development" };
        var context = new HumanInputContext
        {
            AgentId = AgentId.Create(),
            TaskId = TaskId.Create(),
            Prompt = "Select environment",
            Options = options.ToList()
        };

        var sr = new StringReader("Staging\n");
        _input.Inner = sr;

        // Act
        var result = await provider.GetChoiceAsync(context, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Staging", result);
    }

    [Fact]
    public async Task ShouldReturnInput_WhenRequestInputAsyncWithTextType()
    {
        // Arrange
        var logger = new TestLogger<ConsoleHumanInputProvider>();
        var provider = new ConsoleHumanInputProvider(logger, _input, _output);
        var request = HumanInputRequest.Create(
            "Enter your name",
            Orkeon.Domain.HumanInput.HumanInputType.Text,
            "Anonymous"
        );

        var sr = new StringReader("John Doe\n");
        _input.Inner = sr;

        // Act
        var result = await ConsoleHumanInputProvider.RequestInputAsync(request, _input, _output, CancellationToken.None);

        // Assert
        Assert.Equal("John Doe", result);
    }

    [Fact]
    public async Task ShouldWithChoiceTypeDisplaysOptions_WhenRequestInputAsync()
    {
        // Arrange
        var logger = new TestLogger<ConsoleHumanInputProvider>();
        var provider = new ConsoleHumanInputProvider(logger, _input, _output);
        var options = new[] { "Option A", "Option B", "Option C" };
        var request = HumanInputRequest.Create(
            "Choose an option",
            Orkeon.Domain.HumanInput.HumanInputType.Choice,
            "Option A",
            options
        );

        var sr = new StringReader("2\n");
        _input.Inner = sr;

        // Act
        var result = await ConsoleHumanInputProvider.RequestInputAsync(request, _input, _output, CancellationToken.None);

        // Assert
        Assert.Equal("2", result); // Returns raw input from RequestInputAsync
    }

    [Fact]
    public async Task ShouldReturnInput_WhenRequestInputAsyncWithApprovalType()
    {
        // Arrange
        var logger = new TestLogger<ConsoleHumanInputProvider>();
        var provider = new ConsoleHumanInputProvider(logger, _input, _output);
        var request = HumanInputRequest.Create(
            "Do you approve?",
            Orkeon.Domain.HumanInput.HumanInputType.Approval,
            "no"
        );

        var sr = new StringReader("yes\n");
        _input.Inner = sr;

        // Act
        var result = await ConsoleHumanInputProvider.RequestInputAsync(request, _input, _output, CancellationToken.None);

        // Assert
        Assert.Equal("yes", result);
    }

    [Fact]
    public async Task ShouldReturnDefaultValue_WhenRequestInputAsyncWithNullInput()
    {
        // Arrange
        var logger = new TestLogger<ConsoleHumanInputProvider>();
        var provider = new ConsoleHumanInputProvider(logger, _input, _output);
        var request = HumanInputRequest.Create(
            "Enter value",
            Orkeon.Domain.HumanInput.HumanInputType.Text,
            "default-value"
        );

        // Simulate Console.ReadLine returning null
        var sr = new StringReader("");
        _input.Inner = sr;

        // Act
        var result = await ConsoleHumanInputProvider.RequestInputAsync(request, _input, _output, CancellationToken.None);

        // Assert
        Assert.Equal("default-value", result);
    }

    [Fact]
    public async Task ShouldReturnEmptyString_WhenRequestInputAsyncWithNoDefaultAndNullInput()
    {
        // Arrange
        var logger = new TestLogger<ConsoleHumanInputProvider>();
        var provider = new ConsoleHumanInputProvider(logger, _input, _output);
        var request = HumanInputRequest.Create(
            "Enter value",
            Orkeon.Domain.HumanInput.HumanInputType.Text,
            null
        );

        var sr = new StringReader("");
        _input.Inner = sr;

        // Act
        var result = await ConsoleHumanInputProvider.RequestInputAsync(request, _input, _output, CancellationToken.None);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async Task ShouldFallBackToGetInput_WhenGetChoiceAsyncWithNullOptions()
    {
        // Arrange
        var logger = new TestLogger<ConsoleHumanInputProvider>();
        var provider = new ConsoleHumanInputProvider(logger, _input, _output);
        var context = new HumanInputContext
        {
            AgentId = AgentId.Create(),
            TaskId = TaskId.Create(),
            Prompt = "Choose",
            Options = null!,
            DefaultValue = "default"
        };

        _input.Inner = new StringReader(""); // Empty input

        // Act
        var result = await provider.GetChoiceAsync(context, TestContext.Current.CancellationToken);

        // Assert - should return default value when input is empty
        Assert.Equal("default", result);
    }

    [Fact]
    public async Task ShouldThrowOperationCanceledException_WhenGetInputAsyncWithCancellation()
    {
        // Arrange
        var logger = new TestLogger<ConsoleHumanInputProvider>();
        var provider = new ConsoleHumanInputProvider(logger, _input, _output);
        var context = new HumanInputContext
        {
            AgentId = AgentId.Create(),
            TaskId = TaskId.Create(),
            Prompt = "Enter value"
        };

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.GetInputAsync(context, cts.Token));
    }

    [Fact]
    public async Task ShouldThrowOperationCanceledException_WhenGetConfirmationAsyncWithCancellation()
    {
        // Arrange
        var logger = new TestLogger<ConsoleHumanInputProvider>();
        var provider = new ConsoleHumanInputProvider(logger, _input, _output);
        var context = new HumanInputContext
        {
            AgentId = AgentId.Create(),
            TaskId = TaskId.Create(),
            Prompt = "Confirm?"
        };

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.GetConfirmationAsync(context, cts.Token));
    }

    [Fact]
    public async Task ShouldThrowOperationCanceledException_WhenGetChoiceAsyncWithCancellation()
    {
        // Arrange
        var logger = new TestLogger<ConsoleHumanInputProvider>();
        var provider = new ConsoleHumanInputProvider(logger, _input, _output);
        var context = new HumanInputContext
        {
            AgentId = AgentId.Create(),
            TaskId = TaskId.Create(),
            Prompt = "Choose",
            Options = ["A", "B"]
        };

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.GetChoiceAsync(context, cts.Token));
    }

    [Fact]
    public async Task ShouldReturnTrue_WhenGetConfirmationAsyncWithWhitespaceYes()
    {
        // Arrange
        var logger = new TestLogger<ConsoleHumanInputProvider>();
        var provider = new ConsoleHumanInputProvider(logger, _input, _output);
        var context = new HumanInputContext
        {
            AgentId = AgentId.Create(),
            TaskId = TaskId.Create(),
            Prompt = "Confirm?"
        };

        var sr = new StringReader("  yes  \n");
        _input.Inner = sr;

        // Act
        var result = await provider.GetConfirmationAsync(context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task ShouldReturnFalse_WhenGetConfirmationAsyncWithWhitespaceNo()
    {
        // Arrange
        var logger = new TestLogger<ConsoleHumanInputProvider>();
        var provider = new ConsoleHumanInputProvider(logger, _input, _output);
        var context = new HumanInputContext
        {
            AgentId = AgentId.Create(),
            TaskId = TaskId.Create(),
            Prompt = "Confirm?"
        };

        var sr = new StringReader("  no  \n");
        _input.Inner = sr;

        // Act
        var result = await provider.GetConfirmationAsync(context, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task ShouldReturnExactInput_WhenGetChoiceAsyncWithMixedCaseOption()
    {
        // Arrange
        var logger = new TestLogger<ConsoleHumanInputProvider>();
        var provider = new ConsoleHumanInputProvider(logger, _input, _output);
        var options = new[] { "FirstOption", "SecondOption", "ThirdOption" };
        var context = new HumanInputContext
        {
            AgentId = AgentId.Create(),
            TaskId = TaskId.Create(),
            Prompt = "Choose",
            Options = options.ToList()
        };

        var sr = new StringReader("secondoption\n");
        _input.Inner = sr;

        // Act
        var result = await provider.GetChoiceAsync(context, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("secondoption", result); // Returns as-is, no case matching
    }

    [Fact]
    public async Task ShouldPreserveFirstLine_WhenGetInputAsyncWithTabsAndNewlines()
    {
        // Arrange
        var logger = new TestLogger<ConsoleHumanInputProvider>();
        var provider = new ConsoleHumanInputProvider(logger, _input, _output);
        var context = new HumanInputContext
        {
            AgentId = AgentId.Create(),
            TaskId = TaskId.Create(),
            Prompt = "Enter text"
        };

        var sr = new StringReader("\tTabbed text\nSecond line\n");
        _input.Inner = sr;

        // Act
        var result = await provider.GetInputAsync(context, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("\tTabbed text", result);
    }

    [Fact]
    public async Task ShouldWithCancellationStillReturnsTrue_WhenIsAvailableAsync()
    {
        // Arrange
        var logger = new TestLogger<ConsoleHumanInputProvider>();
        var provider = new ConsoleHumanInputProvider(logger, _input, _output);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        var result = await provider.IsAvailableAsync(cts.Token);

        // Assert
        Assert.True(result); // Always returns true regardless of cancellation
    }

    [Fact]
    public async Task ShouldWithWhitespaceAroundIndexTrimsAndParses_WhenGetChoiceAsync()
    {
        // Arrange
        var logger = new TestLogger<ConsoleHumanInputProvider>();
        var provider = new ConsoleHumanInputProvider(logger, _input, _output);
        var options = new[] { "First", "Second", "Third" };
        var context = new HumanInputContext
        {
            AgentId = AgentId.Create(),
            TaskId = TaskId.Create(),
            Prompt = "Choose",
            Options = options.ToList()
        };

        var sr = new StringReader("  2  \n");
        _input.Inner = sr;

        // Act
        var result = await provider.GetChoiceAsync(context, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Second", result);
    }

    [Fact]
    public async Task ShouldWithEmptyOptionsDoesNotPrintOptions_WhenRequestInputAsync()
    {
        // Arrange
        var logger = new TestLogger<ConsoleHumanInputProvider>();
        var provider = new ConsoleHumanInputProvider(logger, _input, _output);
        var request = HumanInputRequest.Create(
            "Enter value",
            Orkeon.Domain.HumanInput.HumanInputType.Text,
            "default",
            []
        );

        var sr = new StringReader("test\n");
        _input.Inner = sr;

        // Act
        var result = await ConsoleHumanInputProvider.RequestInputAsync(request, _input, _output, CancellationToken.None);

        // Assert
        Assert.Equal("test", result);
    }

    [Fact]
    public async Task ShouldReturnCorrectly_WhenGetChoiceAsyncWithSingleOption()
    {
        // Arrange
        var logger = new TestLogger<ConsoleHumanInputProvider>();
        var provider = new ConsoleHumanInputProvider(logger, _input, _output);
        var options = new[] { "OnlyOption" };
        var context = new HumanInputContext
        {
            AgentId = AgentId.Create(),
            TaskId = TaskId.Create(),
            Prompt = "Choose the only option",
            Options = options.ToList()
        };

        var sr = new StringReader("1\n");
        _input.Inner = sr;

        // Act
        var result = await provider.GetChoiceAsync(context, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("OnlyOption", result);
    }

    [Fact]
    public async Task ShouldReturnAsString_WhenGetChoiceAsyncWithDecimalInput()
    {
        // Arrange
        var logger = new TestLogger<ConsoleHumanInputProvider>();
        var provider = new ConsoleHumanInputProvider(logger, _input, _output);
        var options = new[] { "A", "B", "C" };
        var context = new HumanInputContext
        {
            AgentId = AgentId.Create(),
            TaskId = TaskId.Create(),
            Prompt = "Choose",
            Options = options.ToList()
        };

        var sr = new StringReader("1.5\n");
        _input.Inner = sr;

        // Act
        var result = await provider.GetChoiceAsync(context, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("1.5", result); // Not a valid index, returns as-is
    }

    [Fact]
    public async Task ShouldReturnDefaultValue_WhenGetInputAsyncWithOnlyNewline()
    {
        // Arrange
        var logger = new TestLogger<ConsoleHumanInputProvider>();
        var provider = new ConsoleHumanInputProvider(logger, _input, _output);
        var context = new HumanInputContext
        {
            AgentId = AgentId.Create(),
            TaskId = TaskId.Create(),
            Prompt = "Press enter for default",
            DefaultValue = "default-option"
        };

        var sr = new StringReader("\n");
        _input.Inner = sr;

        // Act
        var result = await provider.GetInputAsync(context, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("default-option", result);
    }
}
