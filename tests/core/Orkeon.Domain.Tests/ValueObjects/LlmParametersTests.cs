using System.Collections.Immutable;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Domain.Tests.ValueObjects;

public class LlmParametersTests
{
    #region Constructor Tests

    [Fact]
    public void ShouldUseDefaultValues_WhenConstructingWithNoParameters()
    {
        // Act
        var parameters = LlmParameters.Create();

        // Assert
        Assert.Equal(0.7, parameters.Temperature);
        Assert.Equal(4000, parameters.MaxTokens);
        Assert.Equal(1.0, parameters.TopP);
        Assert.Equal(0.0, parameters.FrequencyPenalty);
        Assert.Equal(0.0, parameters.PresencePenalty);
        Assert.True(parameters.StopSequences.IsDefaultOrEmpty);
        Assert.Null(parameters.Seed);
    }

    [Fact]
    public void ShouldCreateInstance_WhenConstructingWithAllParameters()
    {
        // Arrange
        var temperature = 0.5;
        var maxTokens = 2000;
        var topP = 0.9;
        var frequencyPenalty = 0.5;
        var presencePenalty = 0.3;
        var stopSequences = ImmutableArray.Create("STOP", "END", "\n\n");
        var seed = 42;

        // Act
        var parameters = LlmParameters.Create(
            temperature: temperature,
            maxTokens: maxTokens,
            topP: topP,
            frequencyPenalty: frequencyPenalty,
            presencePenalty: presencePenalty,
            stopSequences: stopSequences,
            seed: seed);

        // Assert
        Assert.Equal(temperature, parameters.Temperature);
        Assert.Equal(maxTokens, parameters.MaxTokens);
        Assert.Equal(topP, parameters.TopP);
        Assert.Equal(frequencyPenalty, parameters.FrequencyPenalty);
        Assert.Equal(presencePenalty, parameters.PresencePenalty);
        Assert.Equal(stopSequences, parameters.StopSequences);
        Assert.Equal(seed, parameters.Seed);
    }

    [Fact]
    public void ShouldUseSpecifiedAndDefaults_WhenConstructingWithPartialParameters()
    {
        // Act
        var parameters = LlmParameters.Create(
            temperature: 0.3,
            maxTokens: 1000);

        // Assert
        Assert.Equal(0.3, parameters.Temperature);
        Assert.Equal(1000, parameters.MaxTokens);
        Assert.Equal(1.0, parameters.TopP); // Default
        Assert.Equal(0.0, parameters.FrequencyPenalty); // Default
        Assert.Equal(0.0, parameters.PresencePenalty); // Default
        Assert.True(parameters.StopSequences.IsDefaultOrEmpty); // Default
        Assert.Null(parameters.Seed); // Default
    }

    #endregion

    #region Record Behavior Tests

    [Fact]
    public void ShouldBeEqual_WhenRecordingEqualityWithSameValues()
    {
        // Arrange
        var stopSequences = ImmutableArray.Create("STOP", "END");
        var parameters1 = LlmParameters.Create(
            temperature: 0.8,
            maxTokens: 3000,
            topP: 0.95,
            frequencyPenalty: 0.2,
            presencePenalty: 0.1,
            stopSequences: stopSequences,
            seed: 123);

        var parameters2 = LlmParameters.Create(
            temperature: 0.8,
            maxTokens: 3000,
            topP: 0.95,
            frequencyPenalty: 0.2,
            presencePenalty: 0.1,
            stopSequences: stopSequences,
            seed: 123);

        // Act & Assert
        Assert.Equal(parameters1, parameters2);
        Assert.True(parameters1 == parameters2);
        Assert.False(parameters1 != parameters2);
        Assert.Equal(parameters1.GetHashCode(), parameters2.GetHashCode());
    }

    [Fact]
    public void ShouldNotBeEqual_WhenRecordingEqualityWithDifferentValues()
    {
        // Arrange
        var parameters1 = LlmParameters.Create(temperature: 0.7);
        var parameters2 = LlmParameters.Create(temperature: 0.8);

        // Act & Assert
        Assert.NotEqual(parameters1, parameters2);
        Assert.False(parameters1 == parameters2);
        Assert.True(parameters1 != parameters2);
    }

    [Fact]
    public void ShouldNotBeEqual_WhenRecordingEqualityWithDifferentStopSequences()
    {
        // Arrange
        var parameters1 = LlmParameters.Create(stopSequences: ImmutableArray.Create("STOP"));
        var parameters2 = LlmParameters.Create(stopSequences: ImmutableArray.Create("END"));

        // Act & Assert
        Assert.NotEqual(parameters1, parameters2);
    }

    [Fact]
    public void ShouldCreateModifiedCopy_WhenUsingWith()
    {
        // Arrange
        var original = LlmParameters.Create(
            temperature: 0.7,
            maxTokens: 2000);

        // Act
        var modified = original with { Temperature = 0.9 };

        // Assert
        Assert.Equal(0.7, original.Temperature);
        Assert.Equal(0.9, modified.Temperature);
        Assert.Equal(original.MaxTokens, modified.MaxTokens);
        Assert.Equal(original.TopP, modified.TopP);
    }

    [Fact]
    public void ShouldReturnFormattedString_WhenCallingToString()
    {
        // Arrange
        var parameters = LlmParameters.Create(
            temperature: 0.5,
            maxTokens: 1500,
            topP: 0.95,
            frequencyPenalty: 0.1,
            presencePenalty: 0.2,
            stopSequences: ImmutableArray.Create("STOP"),
            seed: 42);

        // Act
        var result = parameters.ToJson();

        // Assert
        Assert.Contains("0.5", result);
        Assert.Contains("1500", result);
        Assert.Contains("0.95", result);
        Assert.Contains("42", result);
    }

    [Fact]
    public void ShouldReturnAllValues_WhenUsingDeconstruct()
    {
        // Arrange
        var stopSequences = ImmutableArray.Create("END");
        var parameters = LlmParameters.Create(
            temperature: 0.6,
            maxTokens: 2500,
            topP: 0.85,
            frequencyPenalty: 0.15,
            presencePenalty: 0.25,
            stopSequences: stopSequences,
            seed: 999);

        // Act
        var (temperature, maxTokens, topP, frequencyPenalty, presencePenalty, sequences, seed) = parameters;

        // Assert
        Assert.Equal(0.6, temperature);
        Assert.Equal(2500, maxTokens);
        Assert.Equal(0.85, topP);
        Assert.Equal(0.15, frequencyPenalty);
        Assert.Equal(0.25, presencePenalty);
        Assert.Equal(stopSequences, sequences);
        Assert.Equal(999, seed);
    }

    #endregion

    #region Parameter Validation Scenarios

    [Fact]
    public void ShouldConservativeParameters_WhenUsingScenario()
    {
        // Low temperature, limited tokens for more focused output
        var parameters = LlmParameters.Create(
            temperature: 0.2,
            maxTokens: 500,
            topP: 0.8,
            frequencyPenalty: 0.5,
            presencePenalty: 0.5,
            stopSequences: ImmutableArray.Create("\n", ".", ";"));

        // Assert
        Assert.True(parameters.Temperature < 0.5);
        Assert.True(parameters.MaxTokens < 1000);
        Assert.True(parameters.TopP < 1.0);
        Assert.True(parameters.FrequencyPenalty > 0);
        Assert.True(parameters.PresencePenalty > 0);
        Assert.Equal(3, parameters.StopSequences.Length);
    }

    [Fact]
    public void ShouldCreativeParameters_WhenUsingScenario()
    {
        // High temperature, many tokens for creative output
        var parameters = LlmParameters.Create(
            temperature: 1.2,
            maxTokens: 8000,
            topP: 0.99,
            frequencyPenalty: 0.0,
            presencePenalty: 0.0,
            stopSequences: []);

        // Assert
        Assert.True(parameters.Temperature > 1.0);
        Assert.True(parameters.MaxTokens > 4000);
        Assert.True(parameters.TopP > 0.95);
        Assert.Equal(0.0, parameters.FrequencyPenalty);
        Assert.Equal(0.0, parameters.PresencePenalty);
        Assert.True(parameters.StopSequences.IsEmpty);
    }

    [Fact]
    public void ShouldDeterministicParameters_WhenUsingScenario()
    {
        // Zero temperature with seed for deterministic output
        var parameters = LlmParameters.Create(
            temperature: 0.0,
            maxTokens: 1000,
            topP: 1.0,
            seed: 12345);

        // Assert
        Assert.Equal(0.0, parameters.Temperature);
        Assert.NotNull(parameters.Seed);
        Assert.Equal(12345, parameters.Seed);
    }

    #endregion

    #region ImmutableArray Specific Tests

    [Fact]
    public void ShouldBeEmpty_WhenStoppingSequencesWithDefaultValue()
    {
        // Act
        var parameters = LlmParameters.Create();

        // Assert
        Assert.True(parameters.StopSequences.IsDefault);
        Assert.True(parameters.StopSequences.IsDefaultOrEmpty);
        // Cannot call Assert.Empty on a default ImmutableArray - it would throw
    }

    [Fact]
    public void ShouldNotBeDefault_WhenStoppingSequencesWithEmptyArray()
    {
        // Act
        var parameters = LlmParameters.Create(
            stopSequences: []);

        // Assert
        Assert.False(parameters.StopSequences.IsDefault);
        Assert.True(parameters.StopSequences.IsEmpty);
        Assert.True(parameters.StopSequences.IsDefaultOrEmpty);
        Assert.Empty(parameters.StopSequences);
    }

    [Fact]
    public void ShouldContainAll_WhenStoppingSequencesWithValues()
    {
        // Arrange
        var sequences = new[] { "STOP", "END", "HALT", "\n\n\n" };

        // Act
        var parameters = LlmParameters.Create(
            stopSequences: ImmutableArray.Create(sequences));

        // Assert
        Assert.Equal(4, parameters.StopSequences.Length);
        Assert.Equal("STOP", parameters.StopSequences[0]);
        Assert.Equal("END", parameters.StopSequences[1]);
        Assert.Equal("HALT", parameters.StopSequences[2]);
        Assert.Equal("\n\n\n", parameters.StopSequences[3]);
        Assert.Contains("STOP", parameters.StopSequences);
        Assert.Contains("END", parameters.StopSequences);
    }

    [Fact]
    public void ShouldNotAllowModification_WhenStoppingSequencesImmutability()
    {
        // Arrange
        var original = new[] { "A", "B", "C" };
        var parameters = LlmParameters.Create(
            stopSequences: ImmutableArray.Create(original));

        // Act - Modify original array
        original[0] = "Z";

        // Assert - Parameters should be unchanged
        Assert.Equal("A", parameters.StopSequences[0]);
        Assert.NotEqual("Z", parameters.StopSequences[0]);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void ShouldExtremeTemperatureValues_WhenUsingEdgeCase()
    {
        // Test valid boundary temperature values
        var zeroTemp = LlmParameters.Create(temperature: 0.0);
        var highTemp = LlmParameters.Create(temperature: 2.0);

        // Assert
        Assert.Equal(0.0, zeroTemp.Temperature);
        Assert.Equal(2.0, highTemp.Temperature);
    }

    [Fact]
    public void ShouldThrow_WhenTemperatureOutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LlmParameters.Create(temperature: -0.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => LlmParameters.Create(temperature: 2.1));
    }

    [Fact]
    public void ShouldExtremeTokenValues_WhenUsingEdgeCase()
    {
        // Test valid token values
        var oneToken = LlmParameters.Create(maxTokens: 1);
        var manyTokens = LlmParameters.Create(maxTokens: 100000);

        // Assert
        Assert.Equal(1, oneToken.MaxTokens);
        Assert.Equal(100000, manyTokens.MaxTokens);
    }

    [Fact]
    public void ShouldThrow_WhenMaxTokensNotPositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LlmParameters.Create(maxTokens: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => LlmParameters.Create(maxTokens: -10));
    }

    [Fact]
    public void ShouldPenaltyValues_WhenUsingEdgeCase()
    {
        // Test penalty value ranges
        var negativePenalties = LlmParameters.Create(
            frequencyPenalty: -2.0,
            presencePenalty: -2.0);

        var positivePenalties = LlmParameters.Create(
            frequencyPenalty: 2.0,
            presencePenalty: 2.0);

        // Assert
        Assert.Equal(-2.0, negativePenalties.FrequencyPenalty);
        Assert.Equal(-2.0, negativePenalties.PresencePenalty);
        Assert.Equal(2.0, positivePenalties.FrequencyPenalty);
        Assert.Equal(2.0, positivePenalties.PresencePenalty);
    }

    [Fact]
    public void ShouldTopPBoundaries_WhenUsingEdgeCase()
    {
        // Test valid TopP boundaries
        var zeroTopP = LlmParameters.Create(topP: 0.0);
        var oneTopP = LlmParameters.Create(topP: 1.0);

        // Assert
        Assert.Equal(0.0, zeroTopP.TopP);
        Assert.Equal(1.0, oneTopP.TopP);
    }

    [Fact]
    public void ShouldThrow_WhenTopPOutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LlmParameters.Create(topP: 1.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => LlmParameters.Create(topP: -0.1));
    }

    [Fact]
    public void ShouldEmptyStringInStopSequences_WhenUsingEdgeCase()
    {
        // Act
        var parameters = LlmParameters.Create(
            stopSequences: ImmutableArray.Create("", "STOP", "", "END", ""));

        // Assert
        Assert.Equal(5, parameters.StopSequences.Length);
        Assert.Equal(string.Empty, parameters.StopSequences[0]);
        Assert.Equal("STOP", parameters.StopSequences[1]);
        Assert.Equal(string.Empty, parameters.StopSequences[2]);
    }

    [Fact]
    public void ShouldSpecialCharactersInStopSequences_WhenUsingEdgeCase()
    {
        // Act
        var parameters = LlmParameters.Create(
            stopSequences: ImmutableArray.Create(
                "\n",
                "\r\n",
                "\t",
                "\\n",
                "\"",
                "'",
                "```",
                "###",
                "---",
                "==="));

        // Assert
        Assert.Equal(10, parameters.StopSequences.Length);
        Assert.Contains("\n", parameters.StopSequences);
        Assert.Contains("\r\n", parameters.StopSequences);
        Assert.Contains("\t", parameters.StopSequences);
        Assert.Contains("```", parameters.StopSequences);
    }

    [Fact]
    public void ShouldVeryLongStopSequence_WhenUsingEdgeCase()
    {
        // Arrange
        var longSequence = new string('X', 1000);

        // Act
        var parameters = LlmParameters.Create(
            stopSequences: ImmutableArray.Create(longSequence));

        // Assert
        Assert.Single(parameters.StopSequences);
        Assert.Equal(1000, parameters.StopSequences[0].Length);
    }

    #endregion

    #region Complex Scenarios

    [Fact]
    public void ShouldModifyingParameters_WhenUsingComplexScenario()
    {
        // Start with default parameters
        var initial = LlmParameters.Create();

        // Adjust for code generation
        var codeGen = initial with
        {
            Temperature = 0.3,
            MaxTokens = 2000,
            StopSequences = ImmutableArray.Create("```", "\n\n", "// END")
        };

        // Adjust for creative writing
        var creative = codeGen with
        {
            Temperature = 1.2,
            MaxTokens = 5000,
            TopP = 0.95,
            FrequencyPenalty = 0.5,
            PresencePenalty = 0.3,
            StopSequences = ImmutableArray.Create("THE END", "***")
        };

        // Assert initial unchanged
        Assert.Equal(0.7, initial.Temperature);
        Assert.Equal(4000, initial.MaxTokens);
        Assert.True(initial.StopSequences.IsDefaultOrEmpty);

        // Assert code generation settings
        Assert.Equal(0.3, codeGen.Temperature);
        Assert.Equal(2000, codeGen.MaxTokens);
        Assert.Equal(3, codeGen.StopSequences.Length);

        // Assert creative settings
        Assert.Equal(1.2, creative.Temperature);
        Assert.Equal(5000, creative.MaxTokens);
        Assert.Equal(2, creative.StopSequences.Length);
    }

    [Fact]
    public void ShouldParameterProfiles_WhenUsingComplexScenario()
    {
        // Different parameter profiles for different use cases
        var profiles = new[]
        {
            // Precise factual answers
            LlmParameters.Create(temperature: 0.1, maxTokens: 500, topP: 0.5, seed: 42),
            
            // Balanced conversation
            LlmParameters.Create(temperature: 0.7, maxTokens: 1500, topP: 0.9),
            
            // Creative storytelling
            LlmParameters.Create(temperature: 1.5, maxTokens: 4000, topP: 0.99,
                frequencyPenalty: 0.8, presencePenalty: 0.6),
            
            // Code completion
            LlmParameters.Create(temperature: 0.2, maxTokens: 1000,
                stopSequences: ImmutableArray.Create("```", "}", ";", "\n\n"))
        };

        // Verify each profile has distinct characteristics
        Assert.True(profiles[0].Temperature < profiles[1].Temperature);
        Assert.True(profiles[1].Temperature < profiles[2].Temperature);
        // Check if StopSequences are initialized before comparing lengths
        Assert.True(!profiles[3].StopSequences.IsDefault &&
                    (profiles[0].StopSequences.IsDefault ||
                     profiles[3].StopSequences.Length > profiles[0].StopSequences.Length));
        Assert.NotNull(profiles[0].Seed);
        Assert.Null(profiles[1].Seed);
    }

    #endregion
}
