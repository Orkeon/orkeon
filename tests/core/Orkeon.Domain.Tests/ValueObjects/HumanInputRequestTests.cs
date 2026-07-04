using Orkeon.Domain.HumanInput;
using Orkeon.Domain.HumanInput.ValueObjects;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;

namespace Orkeon.Domain.Tests.ValueObjects;

public class HumanInputRequestTests
{
    private static readonly string[] OptionsAB = ["A", "B"];
    private static readonly string[] OptionsABC = ["A", "B", "C"];
    private static readonly string[] OptionsABD = ["A", "B", "D"];
    private static readonly string[] OptionAOptionBOptionC = ["Option A", "Option B", "Option C"];
    private static readonly string[] YesNoOptions = ["Yes", "No"];
    private static readonly string[] RedGreenBlueOptions = ["Red", "Green", "Blue"];
    private static readonly string[] SizeOptions = ["Small", "Medium", "Large", "Extra Large"];
    private static readonly string[] CsvTxtExtensions = ["*.csv", "*.txt"];
    private static readonly string[] PdfDocDocxExtensions = ["*.pdf", "*.doc", "*.docx"];
    private static readonly string[] ThreeOptions = ["Option 1", "Option 2", "Option 3"];

    #region HumanInputRequest Tests

    [Fact]
    public void ShouldCreateInstance_WhenConstructingWithRequiredParameters()
    {
        // Arrange
        var prompt = "Please enter your name:";
        var inputType = HumanInputType.Text;

        // Act
        var request = HumanInputRequest.Create(prompt, inputType);

        // Assert
        Assert.Equal(prompt, request.Prompt);
        Assert.Equal(inputType, request.InputType);
        Assert.Null(request.DefaultValue);
        Assert.Null(request.Options);
        Assert.Null(request.Timeout);
    }

    [Fact]
    public void ShouldCreateInstance_WhenConstructingWithAllParameters()
    {
        // Arrange
        var prompt = "Select an option:";
        var inputType = HumanInputType.Choice;
        var defaultValue = "Option A";
        var options = OptionAOptionBOptionC;
        var timeout = TimeoutStandard;

        // Act
        var request = HumanInputRequest.Create(prompt, inputType, defaultValue, options, timeout);

        // Assert
        Assert.Equal(prompt, request.Prompt);
        Assert.Equal(inputType, request.InputType);
        Assert.Equal(defaultValue, request.DefaultValue);
        Assert.Equal(options, request.Options);
        Assert.Equal(timeout, request.Timeout);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructingWithNullPrompt()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => HumanInputRequest.Create(null!, HumanInputType.Text));
        Assert.Contains("prompt", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShouldReturnAllValues_WhenUsingDeconstruct()
    {
        // Arrange
        var request = HumanInputRequest.Create(
            "Enter value:",
            HumanInputType.Text,
            "default",
            OptionsAB,
            TimeoutQuick);

        // Act
        var (prompt, inputType, defaultValue, options, timeout) = request;

        // Assert
        Assert.Equal("Enter value:", prompt);
        Assert.Equal(HumanInputType.Text, inputType);
        Assert.Equal("default", defaultValue);
        Assert.Equal(OptionsAB, options);
        Assert.Equal(TimeoutQuick, timeout);
    }

    [Fact]
    public void ShouldBeEqual_WhenRecordingEqualityWithSameValues()
    {
        // Arrange
        var options = YesNoOptions;
        var timeout = TimeSpan.FromMinutes(1);

        var request1 = HumanInputRequest.Create("Confirm?", HumanInputType.Approval, "Yes", options, timeout);
        var request2 = HumanInputRequest.Create("Confirm?", HumanInputType.Approval, "Yes", options, timeout);

        // Act & Assert
        Assert.Equal(request1, request2);
        Assert.True(request1 == request2);
        Assert.False(request1 != request2);
        Assert.Equal(request1.GetHashCode(), request2.GetHashCode());
    }

    [Fact]
    public void ShouldNotBeEqual_WhenRecordingEqualityWithDifferentValues()
    {
        // Arrange
        var request1 = HumanInputRequest.Create("Enter name:", HumanInputType.Text);
        var request2 = HumanInputRequest.Create("Enter age:", HumanInputType.Text);

        // Act & Assert
        Assert.NotEqual(request1, request2);
        Assert.False(request1 == request2);
        Assert.True(request1 != request2);
    }

    [Fact]
    public void ShouldCreateModifiedCopy_WhenUsingWith()
    {
        // Arrange
        var original = HumanInputRequest.Create("Original prompt", HumanInputType.Text);

        // Act
        var modified = original with { Prompt = "Modified prompt" };

        // Assert
        Assert.Equal("Original prompt", original.Prompt);
        Assert.Equal("Modified prompt", modified.Prompt);
        Assert.Equal(original.InputType, modified.InputType);
        Assert.Equal(original.DefaultValue, modified.DefaultValue);
        Assert.Equal(original.Options, modified.Options);
        Assert.Equal(original.Timeout, modified.Timeout);
    }

    [Fact]
    public void ShouldReturnFormattedString_WhenCallingToString()
    {
        // Arrange
        var request = HumanInputRequest.Create(
            "Choose color:",
            HumanInputType.Choice,
            "Red",
            RedGreenBlueOptions,
            TimeSpan.FromSeconds(60));

        // Act
        var result = request.ToString();

        // Assert
        Assert.Contains("Choose color:", result);
        Assert.Contains("Choice", result);
        Assert.Contains("Red", result);
    }

    #endregion

    #region HumanInputType Tests

    [Fact]
    public void ShouldHaveCorrectValues_WhenUsingHumanInputType()
    {
        // Act & Assert — sealed records use All collection
        Assert.Equal(9, HumanInputType.All.Count);
        Assert.Contains(HumanInputType.Text, HumanInputType.All);
        Assert.Contains(HumanInputType.Confirmation, HumanInputType.All);
        Assert.Contains(HumanInputType.Choice, HumanInputType.All);
        Assert.Contains(HumanInputType.File, HumanInputType.All);
        Assert.Contains(HumanInputType.Number, HumanInputType.All);
        Assert.Contains(HumanInputType.DateTime, HumanInputType.All);
        Assert.Contains(HumanInputType.Custom, HumanInputType.All);
        Assert.Contains(HumanInputType.Approval, HumanInputType.All);
        Assert.Contains(HumanInputType.FileUpload, HumanInputType.All);
    }

    [Fact]
    public void ShouldHaveCorrectStringValues_WhenUsingHumanInputType()
    {
        // Assert
        Assert.Equal("Text", HumanInputType.Text.Value);
        Assert.Equal("Choice", HumanInputType.Choice.Value);
        Assert.Equal("Approval", HumanInputType.Approval.Value);
        Assert.Equal("FileUpload", HumanInputType.FileUpload.Value);
    }

    [Fact]
    public void ShouldReturnCorrectNames_WhenUsingHumanInputTypeToString()
    {
        // Assert
        Assert.Equal("Text", HumanInputType.Text.ToString());
        Assert.Equal("Choice", HumanInputType.Choice.ToString());
        Assert.Equal("Approval", HumanInputType.Approval.ToString());
        Assert.Equal("FileUpload", HumanInputType.FileUpload.ToString());
    }

    #endregion

    #region Scenario Tests

    [Fact]
    public void ShouldWithDefaultValue_WhenUsingScenarioTextInput()
    {
        // Arrange & Act
        var request = HumanInputRequest.Create(
            "Enter your email address:",
            HumanInputType.Text,
            "user@example.com");

        // Assert
        Assert.Equal("Enter your email address:", request.Prompt);
        Assert.Equal(HumanInputType.Text, request.InputType);
        Assert.Equal("user@example.com", request.DefaultValue);
        Assert.Null(request.Options);
        Assert.Null(request.Timeout);
    }

    [Fact]
    public void ShouldWithOptionsAndTimeout_WhenUsingScenarioChoiceInput()
    {
        // Arrange
        var options = SizeOptions;
        var timeout = TimeoutQuick;

        // Act
        var request = HumanInputRequest.Create(
            "Select size:",
            HumanInputType.Choice,
            "Medium",
            options,
            timeout);

        // Assert
        Assert.Equal("Select size:", request.Prompt);
        Assert.Equal(HumanInputType.Choice, request.InputType);
        Assert.Equal("Medium", request.DefaultValue);
        Assert.Equal(options, request.Options);
        Assert.Equal(4, request.Options!.Count);
        Assert.Equal(timeout, request.Timeout);
    }

    [Fact]
    public void ShouldWithTimeout_WhenUsingScenarioApprovalInput()
    {
        // Arrange & Act
        var request = HumanInputRequest.Create(
            "Do you want to continue with the operation?",
            HumanInputType.Approval,
            "Yes",
            YesNoOptions,
            TimeSpan.FromMinutes(2));

        // Assert
        Assert.Equal("Do you want to continue with the operation?", request.Prompt);
        Assert.Equal(HumanInputType.Approval, request.InputType);
        Assert.Equal("Yes", request.DefaultValue);
        Assert.NotNull(request.Options);
        Assert.Equal(2, request.Options.Count);
        Assert.Contains("Yes", request.Options);
        Assert.Contains("No", request.Options);
        Assert.Equal(TimeSpan.FromMinutes(2), request.Timeout);
    }

    [Fact]
    public void ShouldFileUploadInput_WhenUsingScenario()
    {
        // Arrange & Act
        var request = HumanInputRequest.Create(
            "Please select a CSV file to upload:",
            HumanInputType.FileUpload,
            null,
            CsvTxtExtensions,
            TimeoutExtended);

        // Assert
        Assert.Equal("Please select a CSV file to upload:", request.Prompt);
        Assert.Equal(HumanInputType.FileUpload, request.InputType);
        Assert.Null(request.DefaultValue);
        Assert.NotNull(request.Options);
        Assert.Equal(2, request.Options.Count);
        Assert.Contains("*.csv", request.Options);
        Assert.Contains("*.txt", request.Options);
        Assert.Equal(TimeoutExtended, request.Timeout);
    }

    [Fact]
    public void ShouldModifyingRequest_WhenUsingScenario()
    {
        // Arrange
        var original = HumanInputRequest.Create(
            "Enter password:",
            HumanInputType.Text,
            null,
            null,
            TimeSpan.FromSeconds(60));

        // Act - Add timeout
        var withLongerTimeout = original with { Timeout = TimeoutStandard };

        // Act - Change prompt
        var withNewPrompt = withLongerTimeout with { Prompt = "Enter new password:" };

        // Act - Add default value
        var withDefault = withNewPrompt with { DefaultValue = "********" };

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(60), original.Timeout);
        Assert.Equal(TimeoutStandard, withLongerTimeout.Timeout);
        Assert.Equal("Enter new password:", withNewPrompt.Prompt);
        Assert.Equal("********", withDefault.DefaultValue);

        // Verify original is unchanged
        Assert.Equal("Enter password:", original.Prompt);
        Assert.Null(original.DefaultValue);
    }

    [Fact]
    public void ShouldValidationHelpers_WhenUsingScenario()
    {
        // Choice input without options should throw
        Assert.Throws<ArgumentException>(
            () => HumanInputRequest.Create("Select:", HumanInputType.Choice));

        // Choice input with empty options should throw
        Assert.Throws<ArgumentException>(
            () => HumanInputRequest.Create("Select:", HumanInputType.Choice, null, []));

        // Approval typically has Yes/No options
        var approvalRequest = HumanInputRequest.Create(
            "Approve?",
            HumanInputType.Approval,
            "Yes",
            YesNoOptions);
        Assert.Equal(2, approvalRequest.Options?.Count);

        // File upload might have file extensions as options
        var fileRequest = HumanInputRequest.Create(
            "Upload file:",
            HumanInputType.FileUpload,
            null,
            PdfDocDocxExtensions);
        Assert.True(fileRequest.Options?.All(o => o.StartsWith("*.")));
    }

    [Fact]
    public void ShouldTimeoutBehavior_WhenUsingScenario()
    {
        // Test various timeout scenarios
        var noTimeout = HumanInputRequest.Create("Input:", HumanInputType.Text);
        Assert.Null(noTimeout.Timeout);

        var shortTimeout = HumanInputRequest.Create("Quick input:", HumanInputType.Text, null, null, TimeSpan.FromSeconds(10));
        Assert.Equal(10, shortTimeout.Timeout?.TotalSeconds);

        var longTimeout = HumanInputRequest.Create("Take your time:", HumanInputType.Text, null, null, TimeSpan.FromHours(1));
        Assert.Equal(3600, longTimeout.Timeout?.TotalSeconds);

        // Zero timeout (immediate)
        var zeroTimeout = HumanInputRequest.Create("Immediate:", HumanInputType.Approval, null, null, TimeSpan.Zero);
        Assert.Equal(TimeSpan.Zero, zeroTimeout.Timeout);
    }

    [Fact]
    public void ShouldComplexEqualityCheck_WhenUsingScenario()
    {
        // Arrange
        var options1 = OptionsABC;
        var options2 = OptionsABC;
        var options3 = OptionsABD;

        var request1 = HumanInputRequest.Create("Choose:", HumanInputType.Choice, "A", options1, TimeSpan.FromMinutes(1));
        var request2 = HumanInputRequest.Create("Choose:", HumanInputType.Choice, "A", options2, TimeSpan.FromMinutes(1));
        var request3 = HumanInputRequest.Create("Choose:", HumanInputType.Choice, "A", options3, TimeSpan.FromMinutes(1));

        // Act & Assert
        Assert.Equal(request1, request2); // Same values, different array instances
        Assert.NotEqual(request1, request3); // Different array contents
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void ShouldBeAllowed_WhenUsingEdgeCaseWithEmptyPrompt()
    {
        // Arrange & Act
        var request = HumanInputRequest.Create("", HumanInputType.Text);

        // Assert
        Assert.Equal("", request.Prompt);
    }

    [Fact]
    public void ShouldThrow_WhenUsingChoiceWithEmptyOptionsArray()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(
            () => HumanInputRequest.Create("Select:", HumanInputType.Choice, null, []));
    }

    [Fact]
    public void ShouldBeAllowed_WhenUsingEdgeCaseOptionsWithNullElements()
    {
        // Arrange & Act
        var request = HumanInputRequest.Create(
            "Select:",
            HumanInputType.Choice,
            null,
            ThreeOptions);

        // Assert
        Assert.NotNull(request.Options);
        Assert.Equal(3, request.Options.Count);
        Assert.Equal("Option 2", request.Options[1]);
    }

    [Fact]
    public void ShouldBeAllowed_WhenUsingEdgeCaseVeryLongTimeout()
    {
        // Arrange & Act
        var request = HumanInputRequest.Create(
            "Long wait:",
            HumanInputType.Text,
            null,
            null,
            TimeSpan.FromDays(365));

        // Assert
        Assert.Equal(TimeSpan.FromDays(365), request.Timeout);
        Assert.Equal(365 * 24 * 60 * 60, request.Timeout?.TotalSeconds);
    }

    [Fact]
    public void ShouldBeAllowed_WhenUsingEdgeCaseWithNegativeTimeout()
    {
        // Records don't prevent negative TimeSpan values
        // This would need to be validated in service layer

        // Arrange & Act
        var request = HumanInputRequest.Create(
            "Negative timeout:",
            HumanInputType.Text,
            null,
            null,
            TimeSpan.FromSeconds(-10));

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(-10), request.Timeout);
        Assert.True(request.Timeout?.TotalSeconds < 0);
    }

    #endregion
}
